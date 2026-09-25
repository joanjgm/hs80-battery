using System;
using System.Linq;
using System.Threading;

namespace Hs80Battery
{
    // Diagnostic: lists the receiver's HID collections and dumps what the vendor collection
    // answers to Bragi GET requests. Build and run with tools\probe.ps1.
    static class Probe
    {
        static void Main(string[] args)
        {
            var all = HidDevice.Enumerate(0x1B1C, 0x0A6B);
            foreach (var i in all) Console.WriteLine(i);

            foreach (var info in all.Where(i => i.UsagePage >= 0xFF00 && i.OutputLength > 0))
            {
                Console.WriteLine();
                Console.WriteLine("== " + info);
                try
                {
                    using (var dev = HidDevice.Open(info))
                    {
                        dev.Report += r => Console.WriteLine("  <- " + Hex(r));
                        byte[][] cmds =
                        {
                            new byte[] { 0x02, 0x09, 0x12 },        // heartbeat
                            new byte[] { 0x02, 0x09, 0x02, 0x0F },  // GET battery level
                            new byte[] { 0x02, 0x09, 0x02, 0x10 },  // GET battery status
                            new byte[] { 0x02, 0x08, 0x02, 0x0F },  // GET battery via receiver target
                        };
                        foreach (var c in cmds)
                        {
                            Console.WriteLine("  -> " + Hex(c));
                            dev.Write(c);
                            Thread.Sleep(400);
                        }
                        Console.WriteLine("  (listening 3s for notifications)");
                        Thread.Sleep(3000);
                    }
                }
                catch (Exception e) { Console.WriteLine("  error: " + e.Message); }
            }
        }

        static string Hex(byte[] b)
        {
            int n = b.Length;
            while (n > 8 && b[n - 1] == 0) n--;
            return string.Join(" ", b.Take(n).Select(x => x.ToString("x2")));
        }
    }
}
