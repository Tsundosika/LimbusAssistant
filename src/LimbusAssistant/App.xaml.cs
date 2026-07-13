using System.IO;
using System.Windows;
using System.Windows.Threading;
using Tsundosika.LimbusAssistant.Engine;
using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant;

public partial class App : Application
{
    AppSettings? _settings;
    AdvisorLoop? _loop;
    HotkeyManager? _hotkeys;
    OverlayWindow? _overlay;
    MainWindow? _main;
    TemplateLibrary? _templates;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _settings = AppSettings.Load();
        _settings.Save();
        var data = GameData.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var calibration = CalibrationStore.Load();
        _templates = TemplateLibrary.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Assets", "Templates"));
        var reader = new WindowsNumberReader();
        var digitTemplates = DigitTemplateReader.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Assets", "DigitTemplates"));
        var pipeline = new VisionPipeline(reader, _templates, calibration, digitTemplates);

        _overlay = new OverlayWindow();
        _main = new MainWindow(data, calibration);
        _main.OverlayToggleRequested += ToggleOverlay;
        _main.GameWindowSelected += OnGameWindowSelected;
        MainWindow = _main;
        _main.Show();

        _loop = new AdvisorLoop(_settings, data, pipeline, reader);
        _main.LiveEnemySelected += enemy => _loop?.SetLiveEnemy(enemy);
        _main.TeamChanged += members => _loop?.SetTeam(members);
        _loop.SetLiveEnemy(_main.SelectedEnemy);
        _loop.SetTeam(_main.TeamMembers);
        _loop.SnapshotPublished += snapshot => Dispatcher.BeginInvoke(() =>
        {
            _overlay?.UpdateSnapshot(snapshot);
            _main?.UpdateSnapshot(snapshot);
        });
        _loop.Start();

        RegisterHotkeys();
    }

    void OnGameWindowSelected(string? title)
    {
        _loop?.SetTargetWindow(title);
        _settings = (_settings ?? new AppSettings()) with { WindowTitle = title ?? "" };
        _settings.Save();
    }

    void RegisterHotkeys()
    {
        _hotkeys = new HotkeyManager();
        RegisterToggle(_settings!.ToggleOverlayHotkey, ToggleOverlay);
        RegisterToggle(_settings.ToggleDebugHotkey, ToggleMain);
        RegisterToggle(_settings.DebugDumpHotkey, () => _loop?.DumpDebugSnapshot());
    }

    void RegisterToggle(string hotkeyText, Action action)
    {
        var binding = HotkeyBinding.Parse(hotkeyText);
        if (binding is not null)
        {
            _hotkeys!.Register(binding, action);
        }
    }

    void ToggleOverlay()
    {
        if (_overlay is null)
        {
            return;
        }
        if (_overlay.IsVisible)
        {
            _overlay.Hide();
        }
        else
        {
            _overlay.Show();
        }
        _main?.SetOverlayVisible(_overlay.IsVisible);
    }

    void ToggleMain()
    {
        if (_main is null)
        {
            return;
        }
        if (_main.IsVisible)
        {
            _main.Hide();
        }
        else
        {
            _main.Show();
        }
    }

    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Something went wrong: {e.Exception.Message}\n\nThe assistant keeps running. If this repeats, restart it.",
            "Limbus Assistant",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _loop?.Dispose();
        _templates?.Dispose();
        base.OnExit(e);
    }
}
