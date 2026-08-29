using D7SystemIntelligence.Core;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace D7SystemIntelligence;

internal static class GameSessionsBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((MainWindow)sender).InitializeGameSessions()),
            true);
    }
}

public partial class MainWindow
{
    private bool _gameSessionsInjected;
    private GameSessionService? _gameSessions;
    private DispatcherTimer? _gameSessionTimer;
    private DateTime _lastSessionStartAttempt = DateTime.MinValue;

    internal void InitializeGameSessions()
    {
        if (_gameSessionsInjected) return;
        _gameSessionsInjected = true;
        _gameSessions = new GameSessionService(_hardware);
        _gameSessions.StatusChanged += message => Dispatcher.Invoke(() => StatusText.Text = message);

        var sidebar = FindVisualChildren<StackPanel>(this)
            .FirstOrDefault(stack => stack.Children.OfType<Button>()
                .Any(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal)));
        if (sidebar != null)
        {
            var update = sidebar.Children.OfType<Button>()
                .First(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal));
            var index = sidebar.Children.IndexOf(update);
            var button = new Button { Content = "جلسات اللعب" };
            button.Click += (_, _) =>
            {
                if (_gameSessions != null)
                    new SessionHistoryWindow(_gameSessions) { Owner = this }.ShowDialog();
            };
            sidebar.Children.Insert(index, button);
        }

        _gameSessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _gameSessionTimer.Tick += async (_, _) => await SyncGameSessionAsync();
        _gameSessionTimer.Start();

        Closed += async (_, _) =>
        {
            _gameSessionTimer?.Stop();
            if (_gameSessions != null)
            {
                try { await _gameSessions.DisposeAsync(); } catch { }
            }
        };
    }

    private async Task SyncGameSessionAsync()
    {
        if (_gameSessions == null) return;
        var game = _orchestrator.LastStatus?.Context.PrimaryGame;

        if (string.IsNullOrWhiteSpace(game))
        {
            if (_gameSessions.IsRunning)
            {
                try { await _gameSessions.StopAsync(); }
                catch (Exception ex) { StatusText.Text = "Game Session: " + ex.Message; }
            }
            return;
        }

        if (_gameSessions.IsRunning && string.Equals(_gameSessions.ActiveGame, game, StringComparison.OrdinalIgnoreCase))
            return;

        if ((DateTime.Now - _lastSessionStartAttempt).TotalSeconds < 20) return;
        _lastSessionStartAttempt = DateTime.Now;
        try
        {
            if (_gameSessions.IsRunning) await _gameSessions.StopAsync();
            await _gameSessions.StartAsync(game);
        }
        catch (Exception ex)
        {
            StatusText.Text = "تعذر بدء Stutter Black Box: " + ex.Message;
        }
    }
}
