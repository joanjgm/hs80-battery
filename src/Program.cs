using System;
using System.Threading;
using System.Windows.Forms;

namespace Hs80Battery
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool first;
            using (new Mutex(true, @"Local\HS80Battery", out first))
            {
                if (!first) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }
}
