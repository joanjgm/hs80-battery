using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Hs80Battery
{
    // One top-level HID collection as Windows exposes it (a separate device path per collection).
    sealed class HidInfo
    {
        public string Path;
        public ushort VendorId;
        public ushort ProductId;
        public ushort UsagePage;
        public ushort Usage;
        public int InputLength;
        public int OutputLength;

        public override string ToString()
        {
            return string.Format("{0:X4}:{1:X4} page={2:X4} usage={3:X4} in={4} out={5}  {6}",
                VendorId, ProductId, UsagePage, Usage, InputLength, OutputLength, Path);
        }
    }

    // Minimal HID access over hid.dll/setupapi.dll. Reads run on a background thread and are
    // handed to the Report event, so both replies and unsolicited notifications are seen.
    sealed class HidDevice : IDisposable
    {
        public readonly HidInfo Info;
        public event Action<byte[]> Report;
        public event Action Disconnected;

        readonly SafeFileHandle handle;
        readonly FileStream stream;
        readonly Thread reader;
        volatile bool closing;

        HidDevice(HidInfo info, SafeFileHandle handle)
        {
            Info = info;
            this.handle = handle;
            stream = new FileStream(handle, FileAccess.ReadWrite, 1, true);
            reader = new Thread(ReadLoop);
            reader.IsBackground = true;
            reader.Name = "hid-reader";
        }

        public static HidDevice Open(HidInfo info)
        {
            var h = Native.CreateFile(info.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
                Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var dev = new HidDevice(info, h);
            dev.reader.Start();
            return dev;
        }

        // `data` starts with the report ID; it is zero-padded to the output report length.
        public void Write(byte[] data)
        {
            var buf = new byte[Info.OutputLength];
            Array.Copy(data, buf, Math.Min(data.Length, buf.Length));
            stream.Write(buf, 0, buf.Length);
        }

        void ReadLoop()
        {
            var buf = new byte[Info.InputLength];
            try
            {
                while (!closing)
                {
                    int n = stream.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    var copy = new byte[n];
                    Array.Copy(buf, copy, n);
                    var h = Report;
                    if (h != null) h(copy);
                }
            }
            catch (Exception) { }
            if (!closing)
            {
                var d = Disconnected;
                if (d != null) d();
            }
        }

        public void Dispose()
        {
            closing = true;
            try { Native.CancelIoEx(handle, IntPtr.Zero); } catch (Exception) { }
            try { stream.Dispose(); } catch (Exception) { }
        }

        public static List<HidInfo> Enumerate(ushort vid, ushort pid)
        {
            var result = new List<HidInfo>();
            Guid hidGuid;
            Native.HidD_GetHidGuid(out hidGuid);
            IntPtr set = Native.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
                Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
            if (set == new IntPtr(-1)) return result;
            try
            {
                var ifData = new Native.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                for (int i = 0; Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref ifData); i++)
                {
                    string path = GetPath(set, ref ifData);
                    if (path == null) continue;
                    var info = Probe(path, vid, pid);
                    if (info != null) result.Add(info);
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        static string GetPath(IntPtr set, ref Native.SP_DEVICE_INTERFACE_DATA ifData)
        {
            int size;
            Native.SetupDiGetDeviceInterfaceDetail(set, ref ifData, IntPtr.Zero, 0, out size, IntPtr.Zero);
            if (size <= 0) return null;
            IntPtr detail = Marshal.AllocHGlobal(size);
            try
            {
                // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on x64, 6 on x86.
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref ifData, detail, size, out size, IntPtr.Zero))
                    return null;
                return Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
            }
            finally { Marshal.FreeHGlobal(detail); }
        }

        static HidInfo Probe(string path, ushort vid, ushort pid)
        {
            // Access 0 lets us read attributes even from collections Windows keeps open (keyboard, mouse).
            using (var h = Native.CreateFile(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                var attr = new Native.HIDD_ATTRIBUTES();
                attr.Size = Marshal.SizeOf(attr);
                if (!Native.HidD_GetAttributes(h, ref attr)) return null;
                if (attr.VendorID != vid || attr.ProductID != pid) return null;

                var info = new HidInfo { Path = path, VendorId = attr.VendorID, ProductId = attr.ProductID };
                IntPtr pre;
                if (Native.HidD_GetPreparsedData(h, out pre))
                {
                    try
                    {
                        Native.HIDP_CAPS caps;
                        if (Native.HidP_GetCaps(pre, out caps) == Native.HIDP_STATUS_SUCCESS)
                        {
                            info.UsagePage = caps.UsagePage;
                            info.Usage = caps.Usage;
                            info.InputLength = caps.InputReportByteLength;
                            info.OutputLength = caps.OutputReportByteLength;
                        }
                    }
                    finally { Native.HidD_FreePreparsedData(pre); }
                }
                return info;
            }
        }
    }

    static class Native
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        public const int DIGCF_PRESENT = 2, DIGCF_DEVICEINTERFACE = 0x10;
        public const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll")]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, int index,
            ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data,
            IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec,
            uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll")] public static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlapped);
    }
}
