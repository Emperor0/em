using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class NetworkLabWindow : Window
{
    private readonly NetworkIntelligence _network = new();
    private readonly NetworkGamingProfileService _profile = new();
    private readonly BufferbloatDiagnosticsService _bufferbloat = new();
    private readonly TextBox _remote = new() { MinWidth = 250 };
    private readonly TextBlock _summary = new();
    private readonly TextBox _evidence = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 7 };
    private readonly List<Button> _buttons = [];

    public NetworkLabWindow()
    {
        Title = "D7KT • Network Lab";
        Width = 980;
        Height = 720;
        MinWidth = 820;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = B("Bg");
        Foreground = B("Text");
        FlowDirection = FlowDirection.RightToLeft;
        Content = Build();
        Loaded += async (_, _) => await DiagnoseAsync();
    }

    private UIElement Build()
    {
        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "NETWORK LAB", FontSize = 30, FontWeight = FontWeights.Bold, Foreground = B("Accent") });
        header.Children.Add(new TextBlock
        {
            Text = "يشخّص الطبقة المرجحة للمشكلة بدل Tweaks عمياء: PC/NIC → Router/Wi‑Fi → ISP → DNS → Remote/Game route. Gaming Profile لا يُعتمد إلا بعد Before/After، ويرجع تلقائيًا عند Regression واضح.",
            Foreground = B("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0)
        });
        Grid.SetRow(header, 0); root.Children.Add(header);

        var controls = new WrapPanel { Margin = new Thickness(0, 16, 0, 12) };
        controls.Children.Add(new TextBlock { Text = "Host/IP اختياري", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5) });
        _remote.ToolTip = "مثال: عنوان سيرفر/خدمة تريد مقارنة Route الخاص بها بالإنترنت العام. بعض السيرفرات تمنع ICMP، لذلك عدم الرد لا يعني Offline.";
        controls.Children.Add(_remote);
        controls.Children.Add(Btn("تشخيص الآن", DiagnoseAsync, true));
        controls.Children.Add(Btn("Gaming NIC • قياس واعتماد", ApplyMeasuredAsync, true));
        controls.Children.Add(Btn("Bufferbloat 50MB", BufferbloatAsync));
        controls.Children.Add(Btn("Restore NIC", RestoreAsync));
        Grid.SetRow(controls, 1); root.Children.Add(controls);

        var summaryCard = Card();
        _summary.Text = "جاري القياس…";
        _summary.TextWrapping = TextWrapping.Wrap;
        _summary.FontSize = 15;
        summaryCard.Child = _summary;
        Grid.SetRow(summaryCard, 2); root.Children.Add(summaryCard);

        _evidence.IsReadOnly = true;
        _evidence.AcceptsReturn = true;
        _evidence.TextWrapping = TextWrapping.Wrap;
        _evidence.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _evidence.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        _evidence.Margin = new Thickness(4, 10, 4, 10);
        Grid.SetRow(_evidence, 3); root.Children.Add(_evidence);

        var bottom = new StackPanel();
        _progress.Margin = new Thickness(4, 0, 4, 7);
        bottom.Children.Add(_progress);
        _status.Foreground = B("Muted");
        _status.TextWrapping = TextWrapping.Wrap;
        bottom.Children.Add(_status);
        Grid.SetRow(bottom, 4); root.Children.Add(bottom);
        return root;
    }

    private async Task DiagnoseAsync()
    {
        await Busy(async () =>
        {
            _status.Text = "قياس Gateway / Internet / DNS / Remote route…";
            var report = await _network.DiagnoseAsync(string.IsNullOrWhiteSpace(_remote.Text) ? null : _remote.Text.Trim());
            Render(report);
            _status.Text = "اكتمل التشخيص. لا يعتبر D7KT اختلاف Ping وحده دليلًا كافيًا لتعديل NIC.";
        });
    }

    private async Task ApplyMeasuredAsync()
    {
        if (MessageBox.Show(
                "D7KT سيقيس الشبكة، يغيّر فقط خصائص Energy/Power Saving المعروفة على NIC، يتحقق من القراءة، يعيد تهيئة المحول مرة واحدة عند الحاجة، ثم يقيس من جديد. إذا ظهر Regression واضح سيعمل Restore تلقائيًا.\n\nقد ينقطع الاتصال عدة ثوانٍ. متابعة؟",
                "D7KT • Measured Network Profile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information) != MessageBoxResult.Yes) return;

        await Busy(async () =>
        {
            var progress = new Progress<string>(x => _status.Text = x);
            var result = await _profile.ApplyMeasuredAsync(_network, progress);
            _summary.Text = result.Kept
                ? "✓ KEEP • Gaming NIC Profile اجتاز Guard ولم يظهر Regression واضح."
                : "! REJECT / ROLLBACK • لم يعتمد D7KT التعديل.";
            _summary.Foreground = result.Kept ? B("Success") : B("Danger");
            _evidence.Text = result.ApplyResult.Detail + Environment.NewLine + Environment.NewLine + result.Verdict;
            _status.Text = result.Kept ? "تم الاحتفاظ بالتغييرات المقاسة." : "تم رفض التغيير/الرجوع حسب نتيجة القياس.";
        });
    }

    private async Task BufferbloatAsync()
    {
        if (MessageBox.Show(
                "اختبار Bufferbloat يولّد Download load حتى 50 MB لمدة قصيرة. لا تشغله أثناء مباراة أو بث مهم. متابعة؟",
                "D7KT • Bufferbloat",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        await Busy(async () =>
        {
            var progress = new Progress<string>(x => _status.Text = x);
            var r = await _bufferbloat.RunDownloadTestAsync(progress);
            _summary.Text = r.Verdict;
            _summary.Foreground = r.AddedLatencyMs is <= 25 ? B("Success") : r.AddedLatencyMs is <= 50 ? B("Warning") : B("Danger");
            _evidence.Text = r.Detail + Environment.NewLine +
                             "ملاحظة: هذا Download-load test محدود، وليس بديلًا عن اختبار router SQM كامل ثنائي الاتجاه.";
            _status.Text = $"استهلاك الاختبار: {r.DownloadedBytes / 1_000_000.0:0.0} MB خلال {r.DurationSeconds:0.0}s";
        });
    }

    private async Task RestoreAsync()
    {
        await Busy(async () =>
        {
            _status.Text = "استعادة إعدادات NIC من Restore Vault…";
            var r = await _profile.RestoreAsync();
            await Task.Delay(3500);
            var diagnosis = await _network.DiagnoseAsync(string.IsNullOrWhiteSpace(_remote.Text) ? null : _remote.Text.Trim());
            _evidence.Text = r.Detail + Environment.NewLine + Environment.NewLine + "POST-RESTORE:" + Environment.NewLine + string.Join(Environment.NewLine, diagnosis.Evidence);
            _summary.Text = r.Success ? "تمت الاستعادة والتحقق من المسار بعد الرجوع." : r.Detail;
            _summary.Foreground = r.Success ? B("Success") : B("Danger");
            _status.Text = "Restore انتهى.";
        });
    }

    private void Render(NetworkDiagnosisReport r)
    {
        var b = r.BaseReport;
        _summary.Foreground = B("Text");
        _summary.Text = $"الطبقة المرجحة: {r.LikelyLayer}\n{r.Verdict}";
        _evidence.Text =
            $"Adapter: {b.AdapterName}\r\nIPv4: {b.IPv4}\r\nLink: {(b.LinkSpeedBps > 0 ? b.LinkSpeedBps / 1_000_000d + " Mbps" : "—")}\r\n" +
            $"Gateway: {Ms(b.GatewayLatencyMs)}\r\nInternet: {Ms(b.InternetLatencyMs)}\r\nJitter: {Ms(b.JitterMs)}\r\nLoss: {b.PacketLossPercent:0.#}%\r\n" +
            $"DNS resolve: {Ms(r.DnsResolutionMs)}\r\n\r\nEvidence:\r\n- {string.Join("\r\n- ", r.Evidence)}\r\n\r\n{b.Notes}";
        if (r.RemoteEndpoint != null)
            _evidence.AppendText($"\r\n\r\nRemote target: {r.RemoteEndpoint.Target}\r\n{r.RemoteEndpoint.Detail}");
    }

    private async Task Busy(Func<Task> action)
    {
        SetBusy(true);
        try { await action(); }
        catch (Exception ex)
        {
            _status.Text = "Network Lab: " + ex.Message;
            _summary.Text = "تعذر إكمال العملية.";
            _summary.Foreground = B("Danger");
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool value)
    {
        foreach (var b in _buttons) b.IsEnabled = !value;
        _progress.IsIndeterminate = value;
        if (!value) _progress.Value = 0;
    }

    private Button Btn(string text, Func<Task> action, bool accent = false)
    {
        var b = new Button { Content = text, MinWidth = 135, Background = accent ? B("AccentStrong") : B("Panel2") };
        b.Click += async (_, _) => await action();
        _buttons.Add(b);
        return b;
    }

    private Border Card() => new()
    {
        Background = B("Panel"), BorderBrush = B("Border"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(15), Padding = new Thickness(15), Margin = new Thickness(4)
    };

    private Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static string Ms(double? v) => v.HasValue ? $"{v.Value:0.0}ms" : "—";
}
