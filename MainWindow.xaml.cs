using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace PomodoroTaskbar;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer timer;
    private Forms.NotifyIcon? tray;
    private TimeSpan remaining = TimeSpan.FromMinutes(25);
    private bool workMode = true;
    private bool running;
    private IntPtr hwnd;

    private const int TBPF_NOPROGRESS = 0x00000000;
    private const int TBPF_NORMAL = 0x00000002;
    private const int TBPF_PAUSED = 0x00000008;

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F2F7F3E3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int flags);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11D0-958A-006097C9A090")]
    private class TaskbarList
    {
    }

    private ITaskbarList3? taskbar;

    public MainWindow()
    {
        InitializeComponent();

        timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        timer.Tick += Timer_Tick;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        StateChanged += MainWindow_StateChanged;

        UpdateDisplay();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // The window is deliberately shown BEFORE optional integrations.
        Show();
        WindowState = WindowState.Normal;
        Visibility = Visibility.Visible;
        Activate();
        Focus();

        hwnd = new WindowInteropHelper(this).Handle;

        // Optional taskbar integration. It can never stop the application.
        try
        {
            taskbar = (ITaskbarList3)new TaskbarList();
            taskbar.HrInit();
        }
        catch
        {
            taskbar = null;
        }

        // Optional tray integration. It can never stop the application.
        try
        {
            CreateTray();
        }
        catch
        {
            tray = null;
        }

        UpdateTaskbarProgress();
    }

    private void CreateTray()
    {
        tray = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Pomodoro Taskbar"
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Start / Pause", null, (_, _) => ToggleTimer());
        menu.Items.Add("Reset", null, (_, _) => ResetTimer());
        menu.Items.Add("Skip", null, (_, _) => SkipSession());
        menu.Items.Add("Show", null, (_, _) => ShowWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowWindow();
    }

    private void StartPause_Click(object sender, RoutedEventArgs e)
    {
        ToggleTimer();
    }

    private void ToggleTimer()
    {
        running = !running;

        if (running)
        {
            timer.Start();
            StartPauseButton.Content = "Pause";
            StatusText.Text = "Running";
        }
        else
        {
            timer.Stop();
            StartPauseButton.Content = "Resume";
            StatusText.Text = "Paused";
        }

        UpdateTaskbarProgress();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        ResetTimer();
    }

    private void ResetTimer()
    {
        timer.Stop();
        running = false;
        workMode = true;
        remaining = TimeSpan.FromMinutes(GetMinutes(WorkBox, 25));

        StartPauseButton.Content = "Start";
        StatusText.Text = "Ready";

        UpdateDisplay();
        ClearTaskbarProgress();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        SkipSession();
    }

    private void SkipSession()
    {
        timer.Stop();
        running = false;
        SwitchMode();
    }

    private static int GetMinutes(System.Windows.Controls.TextBox box, int fallback)
    {
        return int.TryParse(box.Text, out int value) &&
               value >= 1 &&
               value <= 240
            ? value
            : fallback;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (remaining.TotalSeconds > 1)
        {
            remaining -= TimeSpan.FromSeconds(1);
            UpdateDisplay();
            UpdateTaskbarProgress();
        }
        else
        {
            SwitchMode();
        }
    }

    private void SwitchMode()
    {
        timer.Stop();
        running = false;
        workMode = !workMode;

        remaining = TimeSpan.FromMinutes(
            GetMinutes(
                workMode ? WorkBox : BreakBox,
                workMode ? 25 : 5));

        try
        {
            tray?.ShowBalloonTip(
                5000,
                workMode ? "Pomodoro - Work" : "Pomodoro - Break",
                workMode
                    ? "Break finished. Time to work!"
                    : "Work session finished. Take a break!",
                Forms.ToolTipIcon.Info);
        }
        catch
        {
        }

        System.Media.SystemSounds.Exclamation.Play();

        StartPauseButton.Content = "Start";
        StatusText.Text = workMode ? "New work session" : "Break";

        UpdateDisplay();
        ClearTaskbarProgress();
    }

    private void UpdateDisplay()
    {
        ModeText.Text = workMode ? "WORK" : "BREAK";
        TimerText.Text = remaining.ToString(@"mm\:ss");

        double total = GetMinutes(
            workMode ? WorkBox : BreakBox,
            workMode ? 25 : 5) * 60.0;

        double elapsed = Math.Max(
            0,
            total - remaining.TotalSeconds);

        SessionProgress.Value =
            Math.Min(100, elapsed / total * 100);

        if (tray != null)
        {
            try
            {
                tray.Text =
                    $"Pomodoro {TimerText.Text} - {(workMode ? "Work" : "Break")}";
            }
            catch
            {
            }
        }
    }

    private void UpdateTaskbarProgress()
    {
        if (taskbar == null || hwnd == IntPtr.Zero)
            return;

        try
        {
            ulong total = (ulong)(
                GetMinutes(
                    workMode ? WorkBox : BreakBox,
                    workMode ? 25 : 5) * 60);

            ulong completed = (ulong)Math.Max(
                0,
                (long)total -
                (long)Math.Ceiling(remaining.TotalSeconds));

            taskbar.SetProgressState(
                hwnd,
                running ? TBPF_NORMAL : TBPF_PAUSED);

            taskbar.SetProgressValue(
                hwnd,
                Math.Min(completed, total),
                total);
        }
        catch
        {
        }
    }

    private void ClearTaskbarProgress()
    {
        if (taskbar == null || hwnd == IntPtr.Zero)
            return;

        try
        {
            taskbar.SetProgressState(hwnd, TBPF_NOPROGRESS);
        }
        catch
        {
        }
    }

    private void ShowWindow()
    {
        Show();
        Visibility = Visibility.Visible;
        WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        ClearTaskbarProgress();

        try
        {
            tray?.Dispose();
        }
        catch
        {
        }
    }

    private void ExitApp()
    {
        try
        {
            tray?.Dispose();
        }
        catch
        {
        }

        System.Windows.Application.Current.Shutdown();
    }
}