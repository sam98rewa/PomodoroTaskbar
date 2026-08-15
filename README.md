# Pomodoro Taskbar — Final Visible Build

A Windows .NET 8 WPF Pomodoro timer.

Features:
- Main window is explicitly shown at startup
- Start / Pause / Resume
- Reset
- Skip
- Custom work and break durations
- System tray controls
- Windows notifications
- Native taskbar progress indicator
- In-app progress bar
- Self-contained Windows x64 EXE through GitHub Actions

Taskbar and tray integration are optional and are isolated so failures in those
components cannot prevent the main Pomodoro window from appearing.
