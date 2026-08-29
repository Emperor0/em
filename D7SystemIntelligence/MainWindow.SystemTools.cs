using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace D7SystemIntelligence;

internal static class SystemToolsBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((MainWindow)sender).InitializeSystemTools()), true);
    }
}

public partial class MainWindow
{
    private bool _systemToolsInjected;

    internal void InitializeSystemTools()
    {
        if (_systemToolsInjected) return;
        _systemToolsInjected = true;
        var sidebar = FindVisualChildren<StackPanel>(this)
            .FirstOrDefault(stack => stack.Children.OfType<Button>()
                .Any(b => string.Equals(b.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal)));
        if (sidebar == null) return;
        var update = sidebar.Children.OfType<Button>()
            .First(b => string.Equals(b.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal));
        var index = sidebar.Children.IndexOf(update);

        AddButton(sidebar, ref index, "التخزين والأقراص", () => new StorageCenterWindow { Owner = this }.ShowDialog(), "Storage Center");
        AddButton(sidebar, ref index, "برامج بدء التشغيل", () => new StartupManagerWindow { Owner = this }.ShowDialog(), "Startup Manager");
        AddButton(sidebar, ref index, "تطبيقات الخلفية", () => new BackgroundAppsWindow { Owner = this }.ShowDialog());
        AddButton(sidebar, ref index, "الحذف الذكي من الجذور", () => new SmartRemovalWindow { Owner = this }.ShowDialog());
    }

    private static void AddButton(StackPanel sidebar, ref int index, string label, Action action, string? legacyLabel = null)
    {
        if (sidebar.Children.OfType<Button>().Any(x => Equals(x.Content, label) || (legacyLabel != null && Equals(x.Content, legacyLabel))))
            return;
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        sidebar.Children.Insert(index++, button);
    }
}
