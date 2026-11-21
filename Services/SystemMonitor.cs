using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pulse.Services;

public sealed record HardwareInfo(string Windows, string Cpu, string Gpu, string InstalledRam);
public sealed record SystemSample(double? CpuPercent, double? RamPercent, string RamUsage, string Uptime);

public sealed class SystemMonitor
{
    private (ulong Idle, ulong Total)? previous;

    public static HardwareInfo ReadHardware()
    {
        var (windows, edition) = WindowsInfo();
        using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        var processor = (cpu?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Unavailable";
        var installed = GetPhysicallyInstalledSystemMemory(out var memory) ? Capacity(memory * 1024) : "Unavailable";
        return new($"{windows} {edition}".Trim(), processor, ReadGraphics(), installed);
    }

    public static (string Version, string Edition) WindowsInfo()
    {
        var build = Environment.OSVersion.Version.Build;
        var name = build >= 22000 ? "Windows 11" : "Windows 10";
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var release = key?.GetValue("DisplayVersion") as string ?? key?.GetValue("ReleaseId") as string;
        var revision = key?.GetValue("UBR") is int value ? $".{value}" : "";
        var edition = key?.GetValue("EditionID") as string ?? "";
        return ($"{name} {release} · {build}{revision}", edition switch
        {
            "Core" => "Home", "CoreN" => "Home N", "Professional" => "Pro", "ProfessionalN" => "Pro N",
            _ => edition
        });
    }

    public SystemSample Sample(bool liveMonitoring)
    {
        var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var uptime = elapsed.Days > 0 ? $"{elapsed.Days}d {elapsed.Hours}h {elapsed.Minutes}m" : $"{elapsed.Hours}h {elapsed.Minutes}m";
        if (!liveMonitoring)
            return new(null, null, "Monitoring paused", uptime);

        double? cpu = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel + user;
            if (previous is { } last && total > last.Total && idle >= last.Idle)
                cpu = Math.Clamp(100d * (1d - (double)(idle - last.Idle) / (total - last.Total)), 0, 100);
            previous = (idle, total);
        }
        else
        {
            previous = null;
        }

        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        var hasMemory = GlobalMemoryStatusEx(ref memory);
        return new(cpu, hasMemory ? memory.Load : null,
            hasMemory ? $"{Capacity(memory.TotalPhysical - memory.AvailablePhysical)} / {Capacity(memory.TotalPhysical)}" : "Unavailable",
            uptime);
    }

    public void ResetCpuBaseline() => previous = null;

    private static string Capacity(ulong bytes) => $"{bytes / (1024d * 1024 * 1024):0.#} GB";

    private static string ReadGraphics()
    {
        var descriptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (uint index = 0; index < 64; index++)
        {
            var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref device, 0))
                break;
            // Exclude mirroring and remote display adapters.
            if ((device.StateFlags & 0x00000008) == 0 && (device.StateFlags & 0x04000000) == 0 &&
                !string.IsNullOrWhiteSpace(device.Description))
                descriptions.Add(device.Description);
        }
        return descriptions.Count == 0 ? "Unavailable" : string.Join("  /  ", descriptions);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, AvailableExtended;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus value);
    [DllImport("kernel32.dll")] private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? name, uint index, ref DisplayDevice device, uint flags);
}

internal static class WindowsUserParameters
{
    private const uint GetClientAreaAnimation = 0x1042;
    private const uint SetClientAreaAnimation = 0x1043;
    private const uint UpdateAndBroadcast = 0x03;
    private const uint ShowExtensionsMask = 0x02;
    private const uint ShowExtensionsBit = 0x02;

    public static bool ReadAnimations()
    {
        if (!GetParameter(GetClientAreaAnimation, 0, out var value, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return value != 0;
    }

    public static void SetAnimations(bool enabled)
    {
        if (!SetParameter(SetClientAreaAnimation, 0, enabled ? new IntPtr(1) : IntPtr.Zero, UpdateAndBroadcast))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static bool ReadFileExtensions()
    {
        SHGetSettings(out var flags, ShowExtensionsMask);
        return (flags & ShowExtensionsBit) != 0;
    }

    public static void SetFileExtensions(bool enabled)
    {
        var settings = new ShellState { Flags = enabled ? ShowExtensionsBit : 0 };
        SHGetSetSettings(ref settings, ShowExtensionsMask, true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShellState
    {
        public uint Flags, HiddenDword, HiddenValue;
        public int SortParameter, SortDirection;
        public uint Version, NotUsed, OtherFlags;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool GetParameter(uint action, uint parameter, out int value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SetParameter(uint action, uint parameter, IntPtr value, uint flags);
    [DllImport("shell32.dll")] private static extern void SHGetSettings(out uint state, uint mask);
    [DllImport("shell32.dll")] private static extern void SHGetSetSettings(ref ShellState state, uint mask, bool set);
}
