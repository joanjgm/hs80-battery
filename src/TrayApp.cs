using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Hs80Battery
{
    // The tray icon, plus the Stream Deck keys when Stream Deck launched us as its plugin.
    // Either way this is the only process reading the receiver (see Program).
    sealed class TrayApp : ApplicationContext
    {
        const int PollMs = 10 * 1000;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "HS80Battery";

        readonly Headset headset = new Headset();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ToolStripMenuItem statusItem = new ToolStripMenuItem();
        readonly ToolStripMenuItem startupItem = new ToolStripMenuItem("Start with Windows");
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly SynchronizationContext ui;
        readonly StreamDeckPlugin plugin;
        readonly RegisteredWaitHandle quitWait;

        Reading last = new Reading { State = HeadsetState.NoReceiver };
        bool warnedLow, exited;
        int polling;

        // `plugin` is null when started by hand; `quit` is signalled when another instance takes over.
        public TrayApp(StreamDeckPlugin plugin, WaitHandle quit)
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            this.plugin = plugin;

            statusItem.Enabled = false;
            startupItem.Checked = StartsWithWindows();
            startupItem.Click += (s, e) => ToggleStartup();

            var menu = new ContextMenuStrip();
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Refresh now", null, (s, e) => Poll());
            // Stream Deck already starts with Windows and launches us; an autostart entry would
            // only start a second copy that hands straight back to this one.
            if (plugin == null) menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitThread());

            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => Poll();
            Show(last);
            // Windows 11 keeps the first tooltip as the icon's name in taskbar settings.
            tray.Text = "HS80 Battery";
            statusItem.Text = "HS80: reading…";
            tray.Visible = true;

            timer.Interval = PollMs;
            timer.Tick += (s, e) => Poll();
            timer.Start();

            SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            quitWait = ThreadPool.RegisterWaitForSingleObject(quit,
                (s, timedOut) => ui.Post(_ => ExitThread(), null), null, Timeout.Infinite, true);

            if (plugin != null)
            {
                plugin.RefreshRequested += () => ui.Post(_ => Poll(), null);
                // Stream Deck closed the socket (quit, plugin disabled or updated): go with it.
                plugin.Closed += () => ui.Post(_ => ExitThread(), null);
                plugin.Start();
            }

            Poll();
        }

        void Poll()
        {
            // HID requests block for up to a second when the headset is off; keep them off the UI thread.
            if (Interlocked.Exchange(ref polling, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = new Reading { State = HeadsetState.NoReceiver };
                try { r = headset.Read(); }
                catch (Exception) { }
                finally { Interlocked.Exchange(ref polling, 0); }
                ui.Post(__ => Show(r), null);
            });
        }

        void Show(Reading r)
        {
            last = r;
            string status = Describe(r);
            statusItem.Text = "HS80: " + status;
            tray.Text = Truncate("HS80 · " + status, 63);

            int size = SystemInformation.SmallIconSize.Width;
            var old = tray.Icon;
            using (var bmp = BatteryIcon.Render(r, size, BatteryIcon.TaskbarIsLight()))
                tray.Icon = BatteryIcon.ToIcon(bmp);
            if (old != null) old.Dispose();

            if (plugin != null) plugin.SetImage(BatteryIcon.KeyImage(r));
            WarnIfLow(r);
        }

        void WarnIfLow(Reading r)
        {
            if (r.State != HeadsetState.Discharging || r.Percent > BatteryIcon.LowPercent + 5)
            {
                warnedLow = false;
                return;
            }
            if (warnedLow || r.Percent > BatteryIcon.LowPercent) return;
            warnedLow = true;
            tray.ShowBalloonTip(8000, "HS80 battery low",
                r.Percent + "% left. Plug the headset in to charge it.", ToolTipIcon.Warning);
        }

        static string Describe(Reading r)
        {
            switch (r.State)
            {
                case HeadsetState.NoReceiver: return "receiver not connected";
                case HeadsetState.Off: return "headset off";
                case HeadsetState.Charging: return r.Percent + "% · charging";
                case HeadsetState.Full: return r.Percent + "% · fully charged";
                default: return r.Percent + "%";
            }
        }

        static string Truncate(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max);
        }

        void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // Light/dark taskbar switch arrives as a General change.
            if (e.Category == UserPreferenceCategory.General) ui.Post(_ => Show(last), null);
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            ui.Post(_ => Show(last), null);
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) ui.Post(_ => Poll(), null);
        }

        static bool StartsWithWindows()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunValue) != null;
        }

        void ToggleStartup()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (StartsWithWindows()) k.DeleteValue(RunValue, false);
                else k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
            }
            startupItem.Checked = StartsWithWindows();
        }

        protected override void ExitThreadCore()
        {
            // Exit, a takeover and Stream Deck closing can all arrive; tear down once.
            if (exited) return;
            exited = true;
            SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            quitWait.Unregister(null);
            timer.Stop();
            if (plugin != null) plugin.Dispose();
            tray.Visible = false;
            tray.Dispose();
            headset.Dispose();
            base.ExitThreadCore();
        }
    }
}
