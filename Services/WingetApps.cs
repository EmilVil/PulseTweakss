using System.Diagnostics;
using System.Text;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Pulse.Models;

namespace Pulse.Services;

public enum AppCategory
{
    Browsers, VPNPrivacy, Communication, Gaming, Utilities, Development, Multimedia, Productivity
}

public sealed class AppEntry : INotifyPropertyChanged
{
    private string? note;
    private string status = "Checking";
    private bool selected;
    public required string Name { get; init; }
    public required AppCategory Category { get; init; }
    public string CategoryLabel => Category switch
    {
        AppCategory.VPNPrivacy => "VPN & Privacy",
        _ => Category.ToString()
    };
    public string IconGlyph => Category switch
    {
        AppCategory.Browsers => "\uE774",
        AppCategory.VPNPrivacy => "\uE72E",
        AppCategory.Communication => "\uE8BD",
        AppCategory.Gaming => "\uE7FC",
        AppCategory.Utilities => "\uE90F",
        AppCategory.Development => "\uE943",
        AppCategory.Multimedia => "\uE714",
        AppCategory.Productivity => "\uE8A5",
        _ => "\uE71D"
    };
    public string Monogram => Name switch
    {
        "7-Zip" => "7z",
        "VS Code" => "VS",
        "EA App" => "EA",
        "GOG Galaxy" => "GOG",
        "Tor Browser" => "Tor",
        "Cloudflare WARP" => "W",
        "Telegram Desktop" => "TG",
        "MSI Afterburner" => "MSI",
        "GitHub Desktop" => "GH",
        "CrystalDiskInfo" => "CD",
        "Epic Games" => "E",
        _ => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])))
    };
    public string Description => Name switch
    {
        "Firefox" => "Open-source web browser from Mozilla.",
        "Brave" => "Privacy-focused browser with built-in tracker blocking.",
        "Proton VPN" => "VPN service from the Proton privacy ecosystem.",
        "Telegram Desktop" => "Desktop messaging client for Telegram.",
        "Discord" => "Voice, video and text chat for communities.",
        "Steam" => "PC game library, store and community client.",
        "7-Zip" => "File archiver supporting 7z, ZIP and more.",
        "VS Code" => "Extensible code editor from Microsoft.",
        "Git" => "Distributed version control system.",
        "VLC" => "Media player for a wide range of formats.",
        "OBS Studio" => "Recording and live-streaming software.",
        _ => Category switch
        {
            AppCategory.Browsers => "Web browser.",
            AppCategory.VPNPrivacy => "Privacy, security or password utility.",
            AppCategory.Communication => "Messaging and communication app.",
            AppCategory.Gaming => "PC gaming client or utility.",
            AppCategory.Utilities => "Windows desktop utility.",
            AppCategory.Development => "Development tool.",
            AppCategory.Multimedia => "Audio, video or image application.",
            AppCategory.Productivity => "Productivity application.",
            _ => "Windows application."
        }
    };
    public string? PackageId { get; init; }
    public string? Note { get => note; set => Set(ref note, value); }
    public string? ResolvedId { get; set; }
    public string Status { get => status; set { if (Set(ref status, value)) Notify(nameof(IsInstalled)); } }
    public string? Version { get; set; }
    public bool Selected { get => selected; set => Set(ref selected, value); }
    public bool Available => ResolvedId is not null;
    public bool IsInstalled => Status == "Installed";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void RefreshBindings() { Notify(nameof(Available)); Notify(nameof(Note)); Notify(nameof(Status)); Notify(nameof(IsInstalled)); }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed record WingetResult(string Output, int ExitCode);

public static class AppCatalog
{
    private static readonly (AppCategory Category, string Name, string? Id)[] Entries =
    [
        (AppCategory.Browsers,"Firefox","Mozilla.Firefox"),(AppCategory.Browsers,"Brave","Brave.Brave"),
        (AppCategory.Browsers,"Tor Browser","TorProject.TorBrowser"),
        (AppCategory.VPNPrivacy,"Proton VPN","ProtonTechnologies.ProtonVPN"),(AppCategory.VPNPrivacy,"Mullvad VPN","MullvadVPN.MullvadVPN"),
        (AppCategory.VPNPrivacy,"Tailscale","Tailscale.Tailscale"),
        (AppCategory.VPNPrivacy,"WireGuard","WireGuard.WireGuard"),(AppCategory.VPNPrivacy,"Bitwarden","Bitwarden.Bitwarden"),
        (AppCategory.VPNPrivacy,"KeePassXC","KeePassXCTeam.KeePassXC"),
        (AppCategory.Communication,"Telegram Desktop","Telegram.TelegramDesktop"),(AppCategory.Communication,"Discord"," Discord.Discord".Trim()),
        (AppCategory.Communication,"Signal","OpenWhisperSystems.Signal"),
        (AppCategory.Communication,"WhatsApp","WhatsApp.WhatsApp"),
        (AppCategory.Communication,"Thunderbird","Mozilla.Thunderbird"),
        (AppCategory.Gaming,"Steam","Valve.Steam"),(AppCategory.Gaming,"Epic Games","EpicGames.EpicGamesLauncher"),
        (AppCategory.Gaming,"GOG Galaxy","GOG.Galaxy"),(AppCategory.Gaming,"Playnite","Playnite.Playnite"),
        (AppCategory.Gaming,"Heroic Launcher","HeroicGamesLauncher.HeroicGamesLauncher"),
        (AppCategory.Utilities,"7-Zip","7zip.7zip"),(AppCategory.Utilities,"Everything","voidtools.Everything"),
        (AppCategory.Utilities,"PowerToys","Microsoft.PowerToys"),(AppCategory.Utilities,"WizTree","AntibodySoftware.WizTree"),
        (AppCategory.Utilities,"CrystalDiskInfo","CrystalDewWorld.CrystalDiskInfo"),(AppCategory.Utilities,"Rufus","Rufus.Rufus"),
        (AppCategory.Utilities,"Ventoy","Ventoy.Ventoy"),(AppCategory.Utilities,"LocalSend","LocalSend.LocalSend"),
        (AppCategory.Utilities,"ShareX","ShareX.ShareX"),
        (AppCategory.Development,"VS Code","Microsoft.VisualStudioCode"),(AppCategory.Development,"Git","Git.Git"),
        (AppCategory.Development,"GitHub Desktop","GitHub.GitHubDesktop"),(AppCategory.Development,"Python","Python.Python.3.13"),
        (AppCategory.Development,"Node.js","OpenJS.NodeJS.LTS"),(AppCategory.Development,"Docker Desktop","Docker.DockerDesktop"),
        (AppCategory.Development,"Notepad++","Notepad++.Notepad++"),
        (AppCategory.Multimedia,"VLC","VideoLAN.VLC"),(AppCategory.Multimedia,"OBS Studio","OBSProject.OBSStudio"),
        (AppCategory.Multimedia,"Audacity","Audacity.Audacity"),(AppCategory.Multimedia,"Blender","BlenderFoundation.Blender"),
        (AppCategory.Multimedia,"GIMP","GIMP.GIMP"),(AppCategory.Multimedia,"Spotify","Spotify.Spotify"),
        (AppCategory.Productivity,"LibreOffice","TheDocumentFoundation.LibreOffice"),
        (AppCategory.Productivity,"Obsidian","Obsidian.Obsidian"),(AppCategory.Productivity,"Notion","Notion.Notion"),
        (AppCategory.Productivity,"SumatraPDF","SumatraPDF.SumatraPDF"),
        (AppCategory.Productivity,"Joplin","Joplin.Joplin")
    ];

    public static IReadOnlyList<AppEntry> Create()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Entries.Select(entry => (entry.Category, entry.Name, entry.Id))
            .Where(entry => seen.Add(entry.Name))
            .Select(entry => new AppEntry { Name = entry.Name, Category = entry.Category, PackageId = entry.Id,
                Note = entry.Id is null ? "Package ID not verified. Unavailable until verified." : null })
            .ToArray();
    }
}

public sealed class WingetService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);
    private static readonly string[] VersionNames = ["Version", "Available"];

    public bool IsAvailable => ResolveWinget() is not null;

    public async Task RefreshAsync(IReadOnlyList<AppEntry> apps, IProgress<string> progress, CancellationToken token)
    {
        await Parallel.ForEachAsync(apps, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
            async (app, cancellationToken) =>
        {
            if (app.PackageId is null)
            {
                app.Status = "Unavailable";
                progress.Report(app.Name);
                return;
            }
            var search = await RunAsync(["search", "--id", app.PackageId, "--exact", "--source", "winget", "--accept-source-agreements", "--disable-interactivity"], cancellationToken);
            if (search.ExitCode != 0 || !ContainsId(search.Output, app.PackageId))
            {
                app.ResolvedId = null;
                app.Status = "Unavailable";
                app.Note = search.ExitCode == 0 ? "Exact package ID was not found in the WinGet community source." : search.Output.Trim();
                progress.Report(app.Name);
                return;
            }
            app.ResolvedId = app.PackageId;
            var installed = await RunAsync(["list", "--id", app.PackageId, "--exact", "--disable-interactivity"], cancellationToken);
            app.Status = installed.ExitCode == 0 && ContainsId(installed.Output, app.PackageId) ? "Installed" : "Available";
            app.Version = ReadVersion(installed.Output);
            app.Note = null;
            progress.Report(app.Name);
        });
    }

    public async Task InstallAsync(AppEntry app, bool update, IProgress<string> output, CancellationToken token)
    {
        if (app.ResolvedId is not { } id || !StringComparer.Ordinal.Equals(id, app.PackageId))
            throw new InvalidOperationException("The exact package ID has not been verified for this session.");
        var args = new List<string> { update ? "upgrade" : "install", "--id", id, "--exact", "--source", "winget",
            "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity" };
        await RunStreamingAsync(args, output, token);
    }

    private static bool ContainsId(string output, string id) => output.Split(['\r','\n'], StringSplitOptions.TrimEntries)
        .Any(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(token => token.Equals(id, StringComparison.OrdinalIgnoreCase)));

    private static string? ReadVersion(string output) => output.Split(['\r','\n'], StringSplitOptions.TrimEntries)
        .FirstOrDefault(line => VersionNames.Any(name => line.Contains(name, StringComparison.OrdinalIgnoreCase)));

    private static string? ResolveWinget()
    {
        var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .Select(folder => Path.Combine(folder, "winget.exe")).FirstOrDefault(File.Exists);
        if (path is not null)
            return path;
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        return File.Exists(alias) ? alias : null;
    }

    private async Task<WingetResult> RunAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var exe = ResolveWinget() ?? throw new FileNotFoundException("WinGet is unavailable. Install or update App Installer from Microsoft Store.");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("WinGet could not be started.");
        var stdout = DrainAsync(process.StandardOutput, output => { });
        var stderr = DrainAsync(process.StandardError, output => { });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        return new((await stdout) + Environment.NewLine + (await stderr), process.ExitCode);
    }

    private async Task RunStreamingAsync(IReadOnlyList<string> args, IProgress<string> output, CancellationToken token)
    {
        var exe = ResolveWinget() ?? throw new FileNotFoundException("WinGet is unavailable.");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("WinGet could not be started.");
        var stdout = DrainAsync(process.StandardOutput, output.Report);
        var stderr = DrainAsync(process.StandardError, output.Report);
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(Timeout);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); token.ThrowIfCancellationRequested(); throw new TimeoutException("WinGet exceeded the 30 minute time limit."); }
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0) throw new InvalidOperationException($"WinGet exited with code {process.ExitCode}.");
    }

    private static async Task<string> DrainAsync(StreamReader reader, Action<string> report)
    {
        var output = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            if (output.Length < 64_000) output.AppendLine(line);
            report(line);
        }
        return output.ToString();
    }
}
