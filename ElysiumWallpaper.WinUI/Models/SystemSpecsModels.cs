namespace ElysiumWallpaper.Models;

/// <summary>Aggregate of hardware + OS info gathered at app start and on demand.</summary>
public sealed class SystemSpecs
{
    public string MachineName { get; set; } = "";
    public string OsDescription { get; set; } = "";
    public string ProcessArchitecture { get; set; } = "";
    public CpuInfo Cpu { get; set; } = new();
    public MemoryInfo Memory { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = [];
    public List<DisplayInfo> Displays { get; set; } = [];
}

public sealed class CpuInfo
{
    public string Name { get; set; } = "Unknown CPU";
    public int PhysicalCores { get; set; }
    public int LogicalProcessors { get; set; }
    public uint MaxClockMhz { get; set; }
    public string Summary => $"{PhysicalCores}C / {LogicalProcessors}T  @ {MaxClockMhz / 1000.0:F2} GHz";
}

public sealed class MemoryInfo
{
    public ulong TotalBytes { get; set; }
    public ulong AvailableBytes { get; set; }
    public string TotalFormatted => Format(TotalBytes);
    public string AvailableFormatted => Format(AvailableBytes);
    public string Summary => $"{AvailableFormatted} free of {TotalFormatted}";

    private static string Format(ulong bytes)
    {
        const double Gb = 1024.0 * 1024.0 * 1024.0;
        return $"{bytes / Gb:F1} GB";
    }
}

public sealed class GpuInfo
{
    public string Name { get; set; } = "";
    public string DriverVersion { get; set; } = "";
    public ulong AdapterRamBytes { get; set; }
    public string VideoMode { get; set; } = "";

    public string VramFormatted
    {
        get
        {
            if (AdapterRamBytes == 0) return "-";
            double gb = AdapterRamBytes / 1024.0 / 1024.0 / 1024.0;
            return gb >= 1 ? $"{gb:F1} GB" : $"{AdapterRamBytes / 1024.0 / 1024.0:F0} MB";
        }
    }

    public string DetailLine => string.IsNullOrWhiteSpace(VideoMode)
        ? $"{VramFormatted}  -  driver {DriverVersion}"
        : $"{VramFormatted}  -  {VideoMode}  -  driver {DriverVersion}";
}

public sealed class DisplayInfo
{
    public string DeviceName { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshHz { get; set; }
    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public bool IsPrimary { get; set; }

    public string Header => IsPrimary ? $"{FriendlyName}  (primary)" : FriendlyName;
    public string Resolution => $"{Width} x {Height} @ {RefreshHz} Hz";
    public string Position => $"Origin: ({PositionX}, {PositionY})  -  Device: {DeviceName}";
}
