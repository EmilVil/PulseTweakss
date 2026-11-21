using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Pulse.Models;

namespace Pulse.Tweaks;

internal sealed class PowerPlanTweak : ITweak
{
    private const uint AccessScheme = 16;
    private const uint NoMoreItems = 259;
    private const uint MoreData = 234;

    public TweakInfo Info { get; } = new(TweakCatalog.PowerPlan, Page.Advanced, "Active power plan",
        "Choose from the power plans already installed on this device.", Risk.Moderate, "Windows 10 / 11",
        "Applies device-wide. Higher-performance plans may increase heat and battery use. Restoring requires the original plan to still exist.");

    public Task<Detection> DetectAsync()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var activePointer));
        Guid active;
        try { active = Marshal.PtrToStructure<Guid>(activePointer); }
        finally { LocalFree(activePointer); }

        var options = new List<TweakOption>();
        for (uint index = 0; ; index++)
        {
            uint size = 16;
            var status = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, out var plan, ref size);
            if (status == NoMoreItems)
                break;
            Check(status);
            options.Add(ReadPlan(plan));
        }
        var current = options.FirstOrDefault(option => option.Value == active.ToString("D")) ?? ReadPlan(active);
        return Task.FromResult(new Detection(current, options, null));
    }

    public Task WriteAsync(TweakOption target, TweakOption expected)
    {
        if (!Guid.TryParseExact(target.Value, "D", out var plan))
            throw new ArgumentException("Invalid power plan identifier.");
        Check(PowerSetActiveScheme(IntPtr.Zero, in plan));
        return Task.CompletedTask;
    }

    private static TweakOption ReadPlan(Guid plan)
    {
        var id = plan.ToString("D");
        uint size = 0;
        var status = PowerReadFriendlyName(IntPtr.Zero, in plan, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (status is not (0 or MoreData) || size is < 2 or > 65_536)
            return new(id, id);

        var bytes = new byte[size];
        status = PowerReadFriendlyName(IntPtr.Zero, in plan, IntPtr.Zero, IntPtr.Zero, bytes, ref size);
        if (status != 0)
            return new(id, id);
        var name = Encoding.Unicode.GetString(bytes, 0, (int)size).TrimEnd('\0');
        if (name.StartsWith(@"@%SystemRoot%\system32\powrprof.dll,-", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = new StringBuilder(1024);
            if (SHLoadIndirectString(name, resolved, (uint)resolved.Capacity, IntPtr.Zero) == 0)
                name = resolved.ToString();
        }
        return new(id, string.IsNullOrWhiteSpace(name) ? id : name);
    }

    private static void Check(uint status)
    {
        if (status != 0)
            throw new Win32Exception((int)status);
    }

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr userRoot, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr userRoot, in Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup,
        uint access, uint index, out Guid value, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, in Guid scheme, IntPtr subgroup,
        IntPtr setting, [Out] byte[]? name, ref uint size);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(string source, StringBuilder output, uint length, IntPtr reserved);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
