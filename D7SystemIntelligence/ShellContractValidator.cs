using System.Windows;
using System.Windows.Controls;

namespace D7SystemIntelligence;

internal static class ShellContractValidator
{
    private static readonly string[] Expected =
    [
        "dashboard", "health", "gaming", "devices", "capture", "updates"
    ];

    public static void Validate(D7KtShellWindow shell)
    {
        if (shell.Content is not DependencyObject root)
            throw new InvalidOperationException("D7KT shell contract failed: shell content is missing.");

        var dashboard = FindLogicalDescendants<Button>(root)
            .FirstOrDefault(x => string.Equals(x.Tag as string, "dashboard", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("D7KT shell contract failed: dashboard navigation button is missing.");

        var panel = LogicalTreeHelper.GetParent(dashboard) as Panel ?? dashboard.Parent as Panel;
        if (panel == null)
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

    private static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (var item in FindLogicalDescendants<T>(child))
                yield return item;
        }
    }
}
