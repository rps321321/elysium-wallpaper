using System.Management;
using System.Runtime.InteropServices;
using ElysiumWallpaper.Models;

namespace ElysiumWallpaper.Services;

/// <summary>
/// Collects machine hardware info: CPU (WMI Win32_Processor), GPU (WMI Win32_VideoController),
/// memory (GlobalMemoryStatusEx), and per-monitor native resolution + refresh + arrangement
/// (EnumDisplayDevices + EnumDisplaySettings).
/// All queries run off the UI thread via <see cref="Task.Run(Action)"/>.
/// </summary>
public static class SystemInfoService
{
    public static Task<SystemSpecs> GetSpecsAsync() => Task.Run(() => new SystemSpecs
    {
        MachineName = Environment.MachineName,
        OsDescription = RuntimeInformation.OSDescription,
        ProcessArchitecture = RuntimeInformation.OSArchitecture.ToString(),
        Cpu = GetCpu(),
        Memory = GetMemory(),
        Gpus = GetGpus(),
        Displays = GetDisplays()
    });

    private static CpuInfo GetCpu()
    {
        var info = new CpuInfo { LogicalProcessors = Environment.ProcessorCount };
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                info.Name = obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                info.PhysicalCores = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                info.LogicalProcessors = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? info.LogicalProcessors);
                info.MaxClockMhz = Convert.ToUInt32(obj["MaxClockSpeed"] ?? 0u);
                break;
            }
        }
        catch
        {
            // WMI unavailable or access denied - keep environment fallbacks.
        }
        return info;
    }

    private static MemoryInfo GetMemory()
    {
        var info = new MemoryInfo();
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref status))
        {
            info.TotalBytes = status.ullTotalPhys;
            info.AvailableBytes = status.ullAvailPhys;
        }
        return info;
    }

    private static List<GpuInfo> GetGpus()
    {
        var list = new List<GpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion, VideoModeDescription FROM Win32_VideoController");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                list.Add(new GpuInfo
                {
                    Name = obj["Name"]?.ToString() ?? "Unknown GPU",
                    DriverVersion = obj["DriverVersion"]?.ToString() ?? "",
                    AdapterRamBytes = SafeToUInt64(obj["AdapterRAM"]),
                    VideoMode = obj["VideoModeDescription"]?.ToString() ?? ""
                });
            }
        }
        catch
        {
        }
        return list;
    }

    private static ulong SafeToUInt64(object? value)
    {
        if (value is null) return 0;
        // Win32_VideoController.AdapterRAM is reported as uint32 but may overflow for 4GB+ cards;
        // Microsoft exposes it signed so large VRAM gets clamped by Windows itself. We widen carefully.
        try { return Convert.ToUInt64(value); }
        catch { return 0; }
    }

    private static List<DisplayInfo> GetDisplays()
    {
        var list = new List<DisplayInfo>();
        uint index = 0;
        while (true)
        {
            var adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, index, ref adapter, 0))
            {
                break;
            }
            index++;

            if ((adapter.StateFlags & DisplayDeviceStateFlags.AttachedToDesktop) == 0)
            {
                continue;
            }

            var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(adapter.DeviceName, ENUM_CURRENT_SETTINGS, ref mode))
            {
                continue;
            }

            // Second-level EnumDisplayDevices call with the adapter name gives the monitor model.
            string friendly = adapter.DeviceString;
            var monitor = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0)
                && !string.IsNullOrWhiteSpace(monitor.DeviceString))
            {
                friendly = monitor.DeviceString;
            }

            list.Add(new DisplayInfo
            {
                DeviceName = adapter.DeviceName,
                FriendlyName = friendly,
                Width = mode.dmPelsWidth,
                Height = mode.dmPelsHeight,
                RefreshHz = mode.dmDisplayFrequency,
                PositionX = mode.dmPositionX,
                PositionY = mode.dmPositionY,
                IsPrimary = (adapter.StateFlags & DisplayDeviceStateFlags.PrimaryDevice) != 0
            });
        }

        // Sort primary first, then left-to-right by origin.
        list.Sort((a, b) =>
        {
            int primary = b.IsPrimary.CompareTo(a.IsPrimary);
            return primary != 0 ? primary : a.PositionX.CompareTo(b.PositionX);
        });
        return list;
    }

    // ------- P/Invoke surface -------

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    private const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [Flags]
    private enum DisplayDeviceStateFlags : int
    {
        AttachedToDesktop = 0x1,
        PrimaryDevice = 0x4
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public DisplayDeviceStateFlags StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public uint dmDisplayFlags;
        public int dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }
}
