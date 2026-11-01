
using System.Drawing;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CameraLockMonitor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new CameraMonitorContext());
    }
}

internal enum CameraState
{
    Available,
    Locked,
    Unknown,
    Missing
}

internal sealed class CameraMonitorContext : ApplicationContext
{
    private const string AppName = "CameraLockMonitor";

    private const string SamsungCameraPrefix =
        @"USB\VID_2B7E&PID_0171";

    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static readonly HashSet<string> CameraClassGuids =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "{ca3e7ab9-b4c3-4ae6-8251-579ef933890f}",
            "{6bdd1fc6-810f-11d0-bec7-08002be2092f}"
        };

    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly Form _dispatcher;

    private readonly System.Windows.Forms.Timer _debounceTimer;
    private readonly System.Windows.Forms.Timer _fallbackTimer;

    private ManagementEventWatcher? _watcher;

    private CameraState? _currentState;
    private int _queryRunning;
    private bool _exiting;

    public CameraMonitorContext()
    {
        _dispatcher = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            Opacity = 0,
            WindowState = FormWindowState.Minimized
        };

        _ = _dispatcher.Handle;

        _statusItem = new ToolStripMenuItem(
            "Kamera: tarkistetaan...")
        {
            Enabled = false
        };

        _startupItem = new ToolStripMenuItem(
            "Käynnistä Windowsin mukana")
        {
            CheckOnClick = true
        };

        _startupItem.Checked = IsStartupEnabled();

        _startupItem.CheckedChanged += (_, _) =>
            SetStartup(_startupItem.Checked);

        var menu = new ContextMenuStrip();

        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Tarkista nyt", null,
            (_, _) => ScheduleRefresh());

        menu.Items.Add(_startupItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Sulje", null,
            (_, _) => ExitThread());

        _trayIcon = new NotifyIcon
        {
            Icon = CreateStatusIcon(Color.Gray),
            Text = "Camera Lock Monitor: käynnistyy",
            ContextMenuStrip = menu,
            Visible = true
        };

        _trayIcon.DoubleClick += (_, _) => ScheduleRefresh();

        _debounceTimer = new System.Windows.Forms.Timer
        {
            Interval = 350
        };

        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            _ = RefreshStatusAsync();
        };

        _fallbackTimer = new System.Windows.Forms.Timer
        {
            Interval = 5000
        };

        _fallbackTimer.Tick += (_, _) => ScheduleRefresh();
        _fallbackTimer.Start();

        EnsureStartupRegistration();
        StartDeviceWatcher();

        _ = RefreshStatusAsync();
    }

    private void StartDeviceWatcher()
    {
        try
        {
            var query = new WqlEventQuery(
                "SELECT * FROM Win32_DeviceChangeEvent");

            _watcher = new ManagementEventWatcher(query);

            _watcher.EventArrived += (_, _) =>
            {
                try
                {
                    _dispatcher.BeginInvoke(
                        new Action(ScheduleRefresh));
                }
                catch (InvalidOperationException)
                {
                    // Application is closing.
                }
            };

            _watcher.Start();
        }
        catch
        {
            // Fallback timer remains active.
        }
    }

    private void ScheduleRefresh()
    {
        if (_exiting)
            return;

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async Task RefreshStatusAsync()
    {
        if (_exiting)
            return;

        if (Interlocked.Exchange(ref _queryRunning, 1) != 0)
            return;

        try
        {
            CameraState state =
                await Task.Run(ReadCameraState);

            if (_exiting || _dispatcher.IsDisposed)
                return;

            ApplyState(state);
        }
        catch
        {
            ApplyState(CameraState.Unknown);
        }
        finally
        {
            Interlocked.Exchange(ref _queryRunning, 0);
        }
    }

    private static CameraState ReadCameraState()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity");

            using var devices = searcher.Get();

            bool cameraFound = false;
            bool cameraAvailable = false;
            bool cameraLocked = false;
            bool cameraUnknown = false;

            foreach (ManagementObject device in devices)
            {
                string? instanceId =
                    device["PNPDeviceID"]?.ToString();

                string? pnpClass =
                    device["PNPClass"]?.ToString();

                string? classGuid =
                    device["ClassGuid"]?.ToString();

                if (!IsCameraDevice(pnpClass, classGuid))
                    continue;

                cameraFound = true;

                string status =
                    device["Status"]?.ToString() ?? "";

                int errorCode = 0;

                try
                {
                    if (device["ConfigManagerErrorCode"] != null)
                    {
                        errorCode = Convert.ToInt32(
                            device["ConfigManagerErrorCode"]);
                    }
                }
                catch
                {
                    // Leave error code as zero.
                }

                // Windows error code 22:
                // The device has been disabled.
                if (errorCode == 22)
                {
                    cameraLocked = true;
                    continue;
                }

                if (status.Equals(
                    "OK",
                    StringComparison.OrdinalIgnoreCase))
                {
                    cameraAvailable = true;
                    continue;
                }

                // Samsung-specific behavior observed on the
                // original test laptop when camera privacy lock
                // is enabled.
                if (!string.IsNullOrEmpty(instanceId) &&
                    instanceId.StartsWith(
                        SamsungCameraPrefix,
                        StringComparison.OrdinalIgnoreCase) &&
                    status.Equals(
                        "Unknown",
                        StringComparison.OrdinalIgnoreCase))
                {
                    cameraLocked = true;
                    continue;
                }

                cameraUnknown = true;
            }

            if (!cameraFound)
                return CameraState.Missing;

            // An available camera takes priority when multiple
            // cameras are connected.
            if (cameraAvailable)
                return CameraState.Available;

            if (cameraLocked)
                return CameraState.Locked;

            if (cameraUnknown)
                return CameraState.Unknown;

            return CameraState.Unknown;
        }
        catch
        {
            return CameraState.Unknown;
        }
    }

    private static bool IsCameraDevice(
        string? pnpClass,
        string? classGuid)
    {
        if (pnpClass != null &&
            (pnpClass.Equals(
                "Camera",
                StringComparison.OrdinalIgnoreCase) ||
             pnpClass.Equals(
                "Image",
                StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (classGuid != null &&
            CameraClassGuids.Contains(classGuid))
        {
            return true;
        }

        return false;
    }

    private void ApplyState(CameraState state)
    {
        if (_exiting)
            return;

        if (_currentState == state)
            return;

        _currentState = state;

        Color color;
        string label;

        switch (state)
        {
            case CameraState.Available:
                color = Color.LimeGreen;
                label = "Kamera käytettävissä";
                break;

            case CameraState.Locked:
                color = Color.Red;
                label = "KAMERA LUKITTU";
                break;

            case CameraState.Missing:
                color = Color.Gray;
                label = "Kameraa ei löydy";
                break;

            default:
                color = Color.Gold;
                label = "Kameran tila tuntematon";
                break;
        }

        _trayIcon.Icon = CreateStatusIcon(color);
        _trayIcon.Text = "Camera Lock Monitor: " + label;
        _statusItem.Text = label;
    }

    private static Icon CreateStatusIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode =
                System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            graphics.Clear(Color.Transparent);

            using var brush = new SolidBrush(color);
            using var pen = new Pen(Color.White, 2.5f);

            graphics.FillEllipse(brush, 1, 1, 30, 30);

            graphics.DrawRectangle(pen, 8, 11, 16, 11);
            graphics.DrawEllipse(pen, 13, 13, 6, 6);

            graphics.DrawLine(pen, 11, 11, 13, 8);
            graphics.DrawLine(pen, 13, 8, 19, 8);
            graphics.DrawLine(pen, 19, 8, 21, 11);
        }

        IntPtr handle = bitmap.GetHicon();

        try
        {
            using Icon icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            RunKeyPath, false);

        return key?.GetValue(AppName) != null;
    }

    private static void EnsureStartupRegistration()
    {
        if (IsStartupEnabled())
            return;

        SetStartup(true);
    }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            RunKeyPath);

        if (key == null)
            return;

        if (enabled)
        {
            string processPath =
                Environment.ProcessPath ??
                Application.ExecutablePath;

            string command = $"\"{processPath}\"";

            if (Path.GetFileNameWithoutExtension(processPath)
                .Equals(
                    "dotnet",
                    StringComparison.OrdinalIgnoreCase))
            {
                command +=
                    $" \"{Assembly.GetExecutingAssembly().Location}\"";
            }

            key.SetValue(AppName, command);
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }

    protected override void ExitThreadCore()
    {
        _exiting = true;

        _debounceTimer.Stop();
        _fallbackTimer.Stop();

        _debounceTimer.Dispose();
        _fallbackTimer.Dispose();

        if (_watcher != null)
        {
            try
            {
                _watcher.Stop();
                _watcher.Dispose();
            }
            catch
            {
                // Ignore shutdown errors.
            }
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        _dispatcher.Dispose();

        base.ExitThreadCore();
    }
}
