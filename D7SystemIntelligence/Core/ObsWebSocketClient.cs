using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed class ObsRequestException : Exception
{
    public int Code { get; }

    public ObsRequestException(string requestType, int code, string comment)
        : base($"OBS رفض الأمر {requestType}: {comment} (Code {code})")
    {
        Code = code;
    }
}

public sealed class ObsWebSocketClient : IAsyncDisposable
{
    private ClientWebSocket? _socket;
    private readonly SemaphoreSlim _requestLock = new(1, 1);

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public async Task ConnectAsync(string host, int port, string? password, CancellationToken cancellationToken = default)
    {
        await DisposeSocketAsync();

        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        var uri = new Uri($"ws://{host}:{port}");
        await socket.ConnectAsync(uri, cancellationToken);
        _socket = socket;

        using var hello = await ReceiveJsonAsync(cancellationToken);
        if (hello.RootElement.GetProperty("op").GetInt32() != 0)
            throw new InvalidOperationException("لم يستقبل D7 رسالة Hello الصحيحة من OBS WebSocket.");

        var data = hello.RootElement.GetProperty("d");
        string? authentication = null;

        if (data.TryGetProperty("authentication", out var authNode))
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("OBS WebSocket محمي بكلمة مرور. أدخلها في إعدادات D7 Shadow Capture.");

            var challenge = authNode.GetProperty("challenge").GetString() ?? string.Empty;
            var salt = authNode.GetProperty("salt").GetString() ?? string.Empty;
            authentication = ComputeAuthentication(password, salt, challenge);
        }

        var identifyData = new Dictionary<string, object?>
        {
            ["rpcVersion"] = 1,
            ["eventSubscriptions"] = 0
        };
        if (!string.IsNullOrEmpty(authentication))
            identifyData["authentication"] = authentication;

        await SendAsync(new { op = 1, d = identifyData }, cancellationToken);

        using var identified = await ReceiveJsonAsync(cancellationToken);
        var op = identified.RootElement.GetProperty("op").GetInt32();
        if (op != 2)
            throw new InvalidOperationException("فشل تعريف D7 لدى OBS WebSocket. تحقق من كلمة المرور وإعدادات WebSocket.");
    }

    public async Task<JsonElement> RequestAsync(string requestType, object? requestData = null, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) throw new InvalidOperationException("D7 غير متصل بـ OBS WebSocket.");

        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            var requestId = Guid.NewGuid().ToString("N");
            await SendAsync(new
            {
                op = 6,
                d = new
                {
                    requestType,
                    requestId,
                    requestData = requestData ?? new { }
                }
            }, cancellationToken);

            while (true)
            {
                using var response = await ReceiveJsonAsync(cancellationToken);
                var root = response.RootElement;
                if (!root.TryGetProperty("op", out var opNode) || opNode.GetInt32() != 7)
                    continue;

                var data = root.GetProperty("d");
                if (!data.TryGetProperty("requestId", out var idNode) || !string.Equals(idNode.GetString(), requestId, StringComparison.Ordinal))
                    continue;

                var status = data.GetProperty("requestStatus");
                var ok = status.GetProperty("result").GetBoolean();
                if (!ok)
                {
                    var code = status.TryGetProperty("code", out var codeNode) ? codeNode.GetInt32() : 0;
                    var comment = status.TryGetProperty("comment", out var commentNode) ? commentNode.GetString() ?? "خطأ غير معروف" : "خطأ غير معروف";
                    throw new ObsRequestException(requestType, code, comment);
                }

                if (data.TryGetProperty("responseData", out var responseData))
                    return responseData.Clone();

                using var empty = JsonDocument.Parse("{}");
                return empty.RootElement.Clone();
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public async Task<bool> IsReplayBufferActiveAsync(CancellationToken cancellationToken = default)
    {
        var data = await RequestAsync("GetReplayBufferStatus", cancellationToken: cancellationToken);
        return data.TryGetProperty("outputActive", out var active) && active.GetBoolean();
    }

    public Task StartReplayBufferAsync(CancellationToken cancellationToken = default)
        => RequestAsync("StartReplayBuffer", cancellationToken: cancellationToken);

    public Task StopReplayBufferAsync(CancellationToken cancellationToken = default)
        => RequestAsync("StopReplayBuffer", cancellationToken: cancellationToken);

    public Task SaveReplayBufferAsync(CancellationToken cancellationToken = default)
        => RequestAsync("SaveReplayBuffer", cancellationToken: cancellationToken);

    public Task SetRecordDirectoryAsync(string path, CancellationToken cancellationToken = default)
        => RequestAsync("SetRecordDirectory", new { recordDirectory = path }, cancellationToken);

    public async Task<string?> GetProfileParameterAsync(string category, string name, CancellationToken cancellationToken = default)
    {
        var data = await RequestAsync("GetProfileParameter", new
        {
            parameterCategory = category,
            parameterName = name
        }, cancellationToken);

        if (!data.TryGetProperty("parameterValue", out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.GetString();
    }

    public Task SetProfileParameterAsync(string category, string name, string value, CancellationToken cancellationToken = default)
        => RequestAsync("SetProfileParameter", new
        {
            parameterCategory = category,
            parameterName = name,
            parameterValue = value
        }, cancellationToken);

    private async Task SendAsync(object payload, CancellationToken cancellationToken)
    {
        var socket = _socket ?? throw new InvalidOperationException("OBS WebSocket غير متصل.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task<JsonDocument> ReceiveJsonAsync(CancellationToken cancellationToken)
    {
        var socket = _socket ?? throw new InvalidOperationException("OBS WebSocket غير متصل.");
        using var stream = new MemoryStream();
        var buffer = new byte[16 * 1024];

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("OBS أغلق اتصال WebSocket.");

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }

        stream.Position = 0;
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string ComputeAuthentication(string password, string salt, string challenge)
    {
        var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(password + salt));
        var secret = Convert.ToBase64String(secretHash);
        var authHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge));
        return Convert.ToBase64String(authHash);
    }

    private async Task DisposeSocketAsync()
    {
        if (_socket == null) return;
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "D7 disconnect", CancellationToken.None);
        }
        catch { }
        _socket.Dispose();
        _socket = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeSocketAsync();
        _requestLock.Dispose();
    }
}
