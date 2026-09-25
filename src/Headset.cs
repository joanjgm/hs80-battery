using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Hs80Battery
{
    enum HeadsetState { NoReceiver, Off, Discharging, Charging, Full }

    struct Reading
    {
        public HeadsetState State;
        public int Percent;     // 0-100, meaningful only when the headset is on
        public int Tenths;      // raw level, tenths of a percent
    }

    // Talks to the HS80 RGB Wireless receiver with Corsair's "Bragi" property protocol on the
    // vendor collection (usage page 0xFF42, usage 1). Request: 02 09 02 <prop>; reply:
    // 01 xx 02 <status> <value LE16>. Only GETs are sent, so the headset stays in hardware mode.
    sealed class Headset : IDisposable
    {
        const ushort Vid = 0x1B1C, Pid = 0x0A6B;
        const byte TargetHeadset = 0x09, CmdGet = 0x02;
        const byte PropBatteryLevel = 0x0F, PropBatteryStatus = 0x10;
        const int ReplyTimeoutMs = 700;

        HidDevice dev;
        readonly BlockingCollection<byte[]> replies = new BlockingCollection<byte[]>();
        readonly object gate = new object();

        public Reading Read()
        {
            lock (gate)
            {
                var r = new Reading { State = HeadsetState.NoReceiver };
                if (!EnsureOpen()) return r;
                try
                {
                    int? level = Get(PropBatteryLevel);
                    int? status = Get(PropBatteryStatus);
                    if (level == null || level.Value <= 0 || level.Value > 1000)
                    {
                        r.State = HeadsetState.Off;
                        return r;
                    }
                    r.Tenths = level.Value;
                    r.Percent = (int)Math.Round(level.Value / 10.0, MidpointRounding.AwayFromZero);
                    r.State = status == 1 ? HeadsetState.Charging
                            : status == 3 ? HeadsetState.Full
                            : HeadsetState.Discharging;
                    return r;
                }
                catch (Exception)
                {
                    // Receiver unplugged mid-request (or handle went stale after sleep): reopen next time.
                    Close();
                    return r;
                }
            }
        }

        int? Get(byte prop)
        {
            byte[] stale;
            while (replies.TryTake(out stale)) { }
            dev.Write(new byte[] { 0x02, TargetHeadset, CmdGet, prop });

            var deadline = DateTime.UtcNow.AddMilliseconds(ReplyTimeoutMs);
            byte[] rep;
            while (true)
            {
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0 || !replies.TryTake(out rep, left)) return null;
                if (rep.Length >= 6 && rep[0] == 0x01 && rep[2] == CmdGet) break;
            }
            if (rep[3] != 0x00) return null;   // non-zero status: headset off / property refused
            return rep[4] | (rep[5] << 8);
        }

        bool EnsureOpen()
        {
            if (dev != null) return true;
            var info = HidDevice.Enumerate(Vid, Pid)
                .FirstOrDefault(i => i.UsagePage == 0xFF42 && i.Usage == 0x0001 && i.OutputLength > 0);
            if (info == null) return false;
            try
            {
                var opened = HidDevice.Open(info);
                opened.Report += rep => replies.Add(rep);
                opened.Disconnected += () => { lock (gate) if (dev == opened) Close(); };
                dev = opened;
                return true;
            }
            catch (Exception)
            {
                dev = null;
                return false;
            }
        }

        void Close()
        {
            if (dev == null) return;
            dev.Dispose();
            dev = null;
        }

        public void Dispose()
        {
            lock (gate) Close();
        }
    }
}
