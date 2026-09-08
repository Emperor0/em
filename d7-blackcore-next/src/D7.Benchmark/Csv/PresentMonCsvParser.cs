using System.Globalization;
using D7.Benchmark.Models;
using D7.Benchmark.Statistics;

namespace D7.Benchmark.Csv;

public static class PresentMonCsvParser
{
    private static readonly string[] FrameColumns =
    [
        "MsBetweenPresents",
        "MsBetweenDisplayChange",
        "DisplayedTime"
    ];

    public static async Task<FrameAnalysis> ParseAsync(string csvPath, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(csvPath);
        var headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return Invalid("ملف القياس فارغ.");
        }

        var headers = SplitCsvLine(headerLine);
        var frameColumn = FrameColumns.FirstOrDefault(c => headers.Contains(c, StringComparer.OrdinalIgnoreCase));
        if (frameColumn is null)
        {
            return Invalid("لم يعثر D7 على عمود زمن إطار متوافق في ملف PresentMon.", headers);
        }

        var index = Array.FindIndex(headers, h => string.Equals(h, frameColumn, StringComparison.OrdinalIgnoreCase));
        var frameTimes = new List<double>(4096);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = SplitCsvLine(line);
            if (index >= fields.Length) continue;
            if (double.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0)
            {
                frameTimes.Add(value);
            }
        }

        return FrameStatistics.Analyze(frameTimes, frameColumn);
    }

    internal static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var value = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (ch == ',' && !quoted)
            {
                fields.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(ch);
            }
        }
        fields.Add(value.ToString());
        return fields.ToArray();
    }

    private static FrameAnalysis Invalid(string warning, IReadOnlyList<string>? columns = null) =>
        new(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            columns is null ? string.Empty : string.Join(',', columns), FrameStatistics.MethodVersion, [warning]);
}
