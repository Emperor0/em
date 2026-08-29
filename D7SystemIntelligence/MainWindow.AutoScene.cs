using D7SystemIntelligence.Core;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace D7SystemIntelligence;

internal static class AutoSceneBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                var window = (MainWindow)sender;
                window.Dispatcher.BeginInvoke(new Action(window.InitializeAutoScene), DispatcherPriority.Loaded);
            }),
            true);
    }
}

public partial class MainWindow
{
    private bool _autoSceneInjected;
    private readonly AutoSceneSettingsStore _autoSceneStore = new();
    private AutoSceneDirector? _autoScene;
    private DispatcherTimer? _autoSceneTimer;
    private bool _autoSceneBusy;
    private string _autoSceneStatus = "Auto Scene لم يبدأ بعد.";

    internal void InitializeAutoScene()
    {
        if (_autoSceneInjected) return;
        _autoSceneInjected = true;
        InitializeMissionControl();
        _autoScene = new AutoSceneDirector(_autoSceneStore);

        var sidebar = FindVisualChildren<StackPanel>(this)
            .FirstOrDefault(stack => stack.Children.OfType<Button>()
                .Any(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal)));
        if (sidebar != null)
        {
            var update = sidebar.Children.OfType<Button>()
                .First(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal));
            var index = sidebar.Children.IndexOf(update);
            var button = new Button { Content = "Auto Scene" };
            button.Click += (_, _) =>
            {
                if (_autoScene == null) return;
                new AutoSceneWindow(_autoScene, BuildAutoSceneStatus) { Owner = this }.ShowDialog();
            };
            sidebar.Children.Insert(index, button);
        }

        _autoSceneTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autoSceneTimer.Tick += async (_, _) => await EvaluateAutoSceneAsync();
        _autoSceneTimer.Start();
        Closed += (_, _) => _autoSceneTimer?.Stop();
    }

    private async Task EvaluateAutoSceneAsync()
    {
        if (_autoSceneBusy || _autoScene == null || _missionEngine == null) return;
        _autoSceneBusy = true;
        try
        {
            var evaluation = _autoScene.Evaluate(_orchestrator.LastStatus?.Context, _missionEngine.ActiveMission);
            _autoSceneStatus = evaluation.Reason;
            if (!evaluation.Ready) return;

            if (evaluation.Target == D7Mission.None)
            {
                var restored = await _missionEngine.RestoreAsync();
                _autoSceneStatus = restored.Summary;
            }
            else
            {
                var game = _orchestrator.LastStatus?.Context.PrimaryGame;
                var result = await _missionEngine.ApplyAsync(evaluation.Target, game);
                _autoSceneStatus = result.Summary;
            }
            StatusText.Text = "Auto Scene • " + _autoSceneStatus;
        }
        catch (Exception ex)
        {
            _autoSceneStatus = "Auto Scene: " + ex.Message;
        }
        finally { _autoSceneBusy = false; }
    }

    private string BuildAutoSceneStatus()
    {
        var settings = _autoScene?.Settings ?? _autoSceneStore.Load();
        var mission = _missionEngine == null ? D7Mission.None : _missionEngine.ActiveMission;
        var game = _orchestrator.LastStatus?.Context.PrimaryGame;
        return $"الحالة: {(settings.Enabled ? "ON" : "OFF")}\n" +
               $"المهمة الحالية: {D7MissionEngine.MissionArabic(mission)}\n" +
               $"اللعبة: {(string.IsNullOrWhiteSpace(game) ? "—" : game)}\n" +
               $"مهلة الثبات: {settings.StabilityDelaySeconds} ث\n" +
               _autoSceneStatus;
    }
}
