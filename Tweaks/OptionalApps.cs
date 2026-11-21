using System.Text.RegularExpressions;
using Pulse.Models;
using Pulse.Services;

namespace Pulse.Tweaks;

public sealed partial class OptionalApps
{
    private const string GameBar = "Microsoft.XboxGamingOverlay";
    private static readonly (string Identity, string Name, string Description)[] Allowlist =
    [
        ("Microsoft.Copilot", "Microsoft Copilot", "Microsoft’s stand-alone AI assistant."),
        ("Clipchamp.Clipchamp", "Clipchamp", "Microsoft’s video editor."),
        ("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection", "Microsoft’s collection of casual card games."),
        ("Microsoft.BingNews", "Microsoft News", "The optional Microsoft news app."),
        ("Microsoft.BingWeather", "Microsoft Weather", "The optional Microsoft weather app."),
        ("Microsoft.Getstarted", "Microsoft Tips", "The optional Windows tips app.")
    ];

    public async Task<(IReadOnlyList<(ITweak Tweak, Detection State)> Apps, bool GameBarInstalled)> ScanAsync()
    {
        var names = string.Join(",", Allowlist.Select(app => Quote(app.Identity)).Append(Quote(GameBar)));
        var output = await WindowsProcess.PowerShellAsync(
            $"Get-AppxPackage -PackageTypeFilter Main | Where-Object {{ $_.Name -in @({names}) -and (-not $_.NonRemovable -or $_.Name -eq {Quote(GameBar)}) }} " +
            "| ForEach-Object { $_.PackageFullName }", 25);
        var packages = Parse(output);
        var result = new List<(ITweak, Detection)>();
        foreach (var app in Allowlist)
        {
            var matches = ForIdentity(packages, app.Identity);
            if (matches.Length > 0)
                result.Add((new PackageTweak(app), PackageTweak.Describe(matches)));
        }
        return (result, ForIdentity(packages, GameBar).Length > 0);
    }

    private static string[] ForIdentity(IEnumerable<string> packages, string identity) =>
        packages.Where(full => full.StartsWith(identity + "_", StringComparison.OrdinalIgnoreCase)).ToArray();

    private static string[] Parse(string output) => output.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Where(full => PackageName().IsMatch(full)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    [GeneratedRegex(@"^[A-Za-z0-9.-]+_[0-9.]+_(?:x86|x64|arm|arm64|neutral)_[A-Za-z0-9.-]*_[A-Za-z0-9]+$", RegexOptions.IgnoreCase)]
    private static partial Regex PackageName();

    private sealed class PackageTweak((string Identity, string Name, string Description) app) : ITweak
    {
        private static readonly TweakOption Removed = new(null, "Uninstalled");

        public TweakInfo Info { get; } = new("appx." + app.Identity, Page.Debloat, app.Name, app.Description,
            Risk.Moderate, "Windows 10 / 11 · current user",
            "Local app data may be deleted. Reinstall from Microsoft Store if the app is offered there.", Reversible: false);

        public async Task<Detection> DetectAsync() => Describe(await ReadPackagesAsync());

        public async Task WriteAsync(TweakOption target, TweakOption expected)
        {
            if (target.Value is not null)
                throw new InvalidOperationException("Package installation is outside this operation.");
            if (expected.Value is not { } package || !PackageName().IsMatch(package) ||
                !package.StartsWith(app.Identity + "_", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The reviewed package identity is invalid.");
            await WindowsProcess.PowerShellAsync(
                $"$p = Get-AppxPackage -Name {Quote(app.Identity)} -PackageTypeFilter Main | Where-Object {{ $_.PackageFullName -eq {Quote(package)} -and -not $_.NonRemovable }}; " +
                "if (@($p).Count -ne 1) { throw 'The package changed before removal.' }; Remove-AppxPackage -Package $p.PackageFullName -Confirm:$false", 90);
        }

        private async Task<string[]> ReadPackagesAsync() => ForIdentity(Parse(await WindowsProcess.PowerShellAsync(
            $"Get-AppxPackage -Name {Quote(app.Identity)} -PackageTypeFilter Main | ForEach-Object {{ $_.PackageFullName }}", 25)), app.Identity);

        internal static Detection Describe(string[] matches) => matches.Length switch
        {
            0 => Detection.Available(Removed),
            1 => Detection.Available(new(matches[0], "Installed"), Removed),
            _ => Detection.Unavailable("Multiple versions were detected. Use Windows Installed apps to review this package.")
        };
    }
}
