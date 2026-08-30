using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

internal static class ShellContractValidator
{
    private static readonly string[] Expected =
    [
        "dashboard", "health", "gaming", "devices", "capture", "updates"
    ];

    public static void Validate(D7KtShellWindow shell)
    {
        var dashboard = FindDescendants<Button>(shell)
            .FirstOrDefault(x => string.Equals(x.Tag as string, "dashboard", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("D7KT shell contract failed: dashboard navigation button is missing.");

        if (dashboard.Parent is not Panel panel)
            throw new InvalidOperationException("D7KT shell contract failed: primary navigation panel was not found.");

        var keys = panel.Children.OfType<Button>()
            .Select(x => x.Tag as string)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToArray();

        if (keys.Length != Expected.Length || !Expected.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(keys))
            throw new InvalidOperationException(
                "D7KT shell contract failed. Expected exactly six centers: " +
                string.Join(", ", Expected) + "; actual: " + string.Join(", ", keys));
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            foreach (var item in FindDescendants<T>(child))
                yield return item;
        }
    }
}
