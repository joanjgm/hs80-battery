using System;
using System.Threading;
using System.Windows.Forms;

namespace Hs80Battery
{
    static class Program
    {
        // One instance at a time owns the tray icon and the receiver: two processes on the same
        // HID collection would each receive the other's replies, and replies don't say which
        // request they answer. A copy started by hand defers to the running one; a copy started
        // by Stream Deck asks the running one to quit and takes over, since only it can paint keys.
        [STAThread]
        static void Main(string[] args)
        {
            var plugin = StreamDeckPlugin.FromArgs(args);
            using (var quit = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HS80Battery.Quit"))
            using (var owner = new Mutex(false, @"Local\HS80Battery"))
            {
                if (!Acquire(owner, 0))
                {
                    if (plugin == null) return;
                    quit.Set();
                    if (!Acquire(owner, 10000)) return;
                }
                // A request aimed at a previous owner must not end this one.
                quit.Reset();
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new TrayApp(plugin, quit));
                }
                finally { owner.ReleaseMutex(); }
            }
        }

        static bool Acquire(Mutex m, int timeoutMs)
        {
            try { return m.WaitOne(timeoutMs); }
            catch (AbandonedMutexException) { return true; }   // previous owner died; it's ours now
        }
    }
}
