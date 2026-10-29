using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using JackOfAll.AppUi.Themes;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Settings;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls.Factories;

namespace JackOfAll.AppUi;

public partial class App : Application
{
    private static Mutex? _mutex;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "JackOfAll_SingleInstance_Mutex", out var isNew);

        if (!isNew)
        {
            var result = MessageBox.Show(
                "JackOfAll is already running.\n\nSwitch to the running instance?",
                "JackOfAll",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                FocusExistingInstance();
                Shutdown();
                return;
            }

            if (result == MessageBoxResult.Cancel)
            {
                Shutdown();
                return;
            }
        }

        var settings = AppSettings.Load();
        var bus = new MessageBus();
        var registry = new ToolRegistry();

        void ApplyTheme() => ThemeManager.Apply(settings);

        registry.Register(new K8sToolFactory());
        registry.Register(new JwtToolFactory());
        registry.Register(new UtilityToolFactory());
        registry.Register(new Base64ToolFactory());
        registry.Register(new DiffToolFactory());
        registry.Register(new EnvVarToolFactory());
        registry.Register(new PortScannerToolFactory());
        registry.Register(new SshKeyToolFactory());
        registry.Register(new NotepadToolFactory());
        registry.Register(new WellbeingToolFactory());
        registry.Register(new GitHubToolFactory(settings));
        registry.Register(new ApiClientToolFactory());
        registry.Register(new CodeRunnerToolFactory());
        var clipboard = new ClipboardHistoryToolFactory();
        registry.Register(clipboard);
        registry.Register(new LogViewerToolFactory());
        registry.Register(new FerryToolFactory());
        var tally = new TallyToolFactory();
        registry.Register(tally);
        registry.Register(new UndertowToolFactory());

        ApplyTheme();

        var window = new MainWindow(registry, bus, settings, ApplyTheme);
        window.Show();
        clipboard.Start(window, settings);
        tally.Start(window);
    }

    private static void FocusExistingInstance()
    {
        var current = Process.GetCurrentProcess();
        foreach (var proc in Process.GetProcessesByName(current.ProcessName))
        {
            if (proc.Id == current.Id) continue;
            if (proc.MainWindowHandle == nint.Zero) continue;

            if (IsIconic(proc.MainWindowHandle))
                ShowWindow(proc.MainWindowHandle, SW_RESTORE);

            SetForegroundWindow(proc.MainWindowHandle);
            break;
        }
    }
}
