using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Win32;
using Pulse.Models;
using Pulse.Services;
using Pulse.Tweaks;

namespace Pulse.ViewModels;

public sealed class MainViewModel : Bindable
{
    private readonly JsonStore store;
    private readonly IDialogs dialogs;
    private readonly TweakEngine engine;
    private readonly SystemMonitor monitor = new();
    private readonly IReadOnlyList<ITweak> catalog = TweakCatalog.Create();
    private readonly OptionalApps optionalApps = new();
    private List<TweakRow> rows = [];
    private List<ChangeRecord> history = [];
    private Page page;
    private Preferences preferences = new();
    private string search = "";
    private string status = "";
    private string busyMessage = "";
    private string? packageError;
    private bool gameBar;
    private bool recall;
    private bool batchingSelection;
    private HardwareInfo hardware = new("Detecting…", "Detecting…", "Detecting…", "Detecting…");
    private SystemSample sample = new(null, null, "Detecting…", "Detecting…");

    public MainViewModel(JsonStore store, IDialogs dialogs)
    {
        this.store = store;
        this.dialogs = dialogs;
        engine = new(store);
        try { preferences = store.ReadPreferences(); }
        catch (Exception ex) { Status = $"Saved preferences could not be loaded. {ex.Message}"; }

        Navigation =
        [
            new(Page.Overview, "\uE80F") { Active = true }, new(Page.Optimization, "\uE945"),
            new(Page.Debloat, "\uE74D"), new(Page.AppInstaller, "\uE7A8"), new(Page.Gaming, "\uE7FC"), new(Page.Privacy, "\uE72E"),
            new(Page.Customization, "\uE771"), new(Page.Advanced, "\uE9E9"), new(Page.Restore, "\uE777"),
            new(Page.Settings, "\uE713")
        ];
        NavigateCommand = new Command(value => Navigate((Page)value!), value => value is Page);
        RefreshCommand = new Command(async _ => await RefreshAsync(), _ => !Busy);
        ApplyCommand = new Command(async _ => await ApplyAsync(), _ => !Busy && SelectedCount > 0);
        RevertCommand = new Command(async value => await RevertAsync((HistoryRow)value!), value => !Busy && value is HistoryRow { CanRevert: true });
        ProfileCommand = new Command(value => SelectProfile((Profile)value!), value => !Busy && value is Profile);
        ClearCommand = new Command(_ => ClearSelection(), _ => !Busy && SelectedCount > 0);
        ClearSearchCommand = new Command(_ => Search = "", _ => SearchActive);
        ExportCommand = new Command(_ => Export(), _ => !Busy);
        OpenLinkCommand = new Command(value => OpenLink((SettingLink)value!), value => value is SettingLink);
    }

    public IReadOnlyList<NavRow> Navigation { get; }
    public IReadOnlyList<ProfileRow> Profiles { get; } =
    [
        new(Profile.Safe, "Safe", "Show file extensions."),
        new(Profile.Balanced, "Balanced", "File extensions and Game Mode."),
        new(Profile.Gaming, "Gaming", "Game Mode and fewer animations."),
        new(Profile.Custom, "Custom", "Choose your own changes.")
    ];

    public ICommand NavigateCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand RevertCommand { get; }
    public ICommand ProfileCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand OpenLinkCommand { get; }

    public Page CurrentPage => page;
    public bool SearchActive => !string.IsNullOrWhiteSpace(search);
    public bool IsOverview => !SearchActive && page == Page.Overview;
    public bool IsRestore => !SearchActive && page == Page.Restore;
    public bool IsSettings => !SearchActive && page == Page.Settings;
    public bool IsAppInstaller => !SearchActive && page == Page.AppInstaller;
    public bool IsCatalog => !IsOverview && !IsRestore && !IsSettings && !IsAppInstaller;
    public bool IsAdvanced => !SearchActive && page == Page.Advanced;
    public string PageTitle => SearchActive ? "Search results" : page.ToString();
    public string PageDescription => SearchActive ? "Find settings and optional apps across PULSE." : page switch
    {
        Page.Overview => "Live system activity and hardware at a glance.",
        Page.Optimization => "Adjust how Windows and your applications respond.",
        Page.Debloat => "Review optional apps installed for your Windows account.",
        Page.AppInstaller => "Find and install applications through verified WinGet package IDs.",
        Page.Gaming => "Manage Windows gaming, capture and startup preferences.",
        Page.Privacy => "Manage recommendations, personalization and optional features.",
        Page.Customization => "Adjust the details you see while using Windows.",
        Page.Advanced => "Review device-wide changes and their trade-offs.",
        Page.Restore => "Review changes made by PULSE and restore previous settings.",
        Page.Settings => "Manage this app’s preferences and export your data.",
        _ => ""
    };

    public string Search
    {
        get => search;
        set
        {
            if (Set(ref search, value))
                UpdatePage();
        }
    }

    public bool Busy => BusyMessage.Length != 0;
    public bool Idle => !Busy;
    public string BusyMessage
    {
        get => busyMessage;
        private set
        {
            if (!Set(ref busyMessage, value))
                return;
            Notify(nameof(Busy));
            Notify(nameof(Idle));
            Notify(nameof(ShowEmpty));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string Status
    {
        get => status;
        private set
        {
            if (Set(ref status, value))
                Notify(nameof(HasStatus));
        }
    }
    public bool HasStatus => Status.Length > 0;
    public int SelectedCount => rows.Count(row => row.Staged);
    public string SelectionLabel => SelectedCount == 1 ? "1 change selected" : $"{SelectedCount} changes selected";
    public string SelectionHint => SelectedCount == 0 ? "Select a tweak or load a profile." : "Review the selected changes before applying.";
    public string ProfileName => preferences.Profile.ToString();
    public bool LiveMonitoring
    {
        get => preferences.LiveMonitoring;
        set
        {
            if (value == preferences.LiveMonitoring)
                return;
            UpdatePreferences(preferences with { LiveMonitoring = value });
            monitor.ResetCpuBaseline();
            SampleNow();
            Notify();
            Notify(nameof(MonitorLabel));
        }
    }

    public bool AnimatePages
    {
        get => preferences.AnimatePages;
        set
        {
            if (value == preferences.AnimatePages)
                return;
            UpdatePreferences(preferences with { AnimatePages = value });
            Notify();
        }
    }

    public string MonitorLabel => LiveMonitoring ? "Live · 2 s refresh" : "Monitoring paused";
    public string Windows => hardware.Windows;
    public string Cpu => hardware.Cpu;
    public string Gpu => hardware.Gpu;
    public string InstalledRam => hardware.InstalledRam;
    public double CpuPercent => sample.CpuPercent ?? 0;
    public double RamPercent => sample.RamPercent ?? 0;
    public string CpuLabel => FormatPercent(sample.CpuPercent);
    public string RamLabel => FormatPercent(sample.RamPercent);
    public string RamUsage => sample.RamUsage;
    public string Uptime => sample.Uptime;

    public IEnumerable<TweakRow> VisibleRows => SearchActive
        ? rows.Where(row => Matches(row.Info.Name, row.Info.Description, row.Info.Page.ToString()))
        : rows.Where(row => row.Info.Page == page || (page == Page.Gaming && row.Info.Id is TweakCatalog.Animations or TweakCatalog.PowerPlan));
    public IEnumerable<SettingLink> VisibleLinks => SearchActive
        ? Enum.GetValues<Page>().SelectMany(PageLinks).Distinct().Where(link => Matches(link.Name, link.Description))
        : PageLinks(page);
    public bool HasLinks => VisibleLinks.Any();
    public bool ShowEmpty => !Busy && !VisibleRows.Any() && !HasLinks;
    public string EmptyLabel => SearchActive ? "No matching settings or apps." :
        packageError is null ? "No optional apps from the PULSE list were detected." : "Select Refresh to retry app detection.";
    public bool ShowPackageError => packageError is not null && page == Page.Debloat && !SearchActive;
    public string PackageError => packageError ?? "";
    public string CatalogLabel => SearchActive ? "Matching changes" : page == Page.Debloat ? "Optional installed apps" : "Available changes";
    public IReadOnlyList<HistoryRow> History => history.AsEnumerable().Reverse()
        .Select(entry => new HistoryRow(entry, TweakEngine.CanRevert(history, entry) && catalog.Any(tweak => tweak.Info.Id == entry.Info.Id))).ToArray();
    public bool HistoryEmpty => history.Count == 0;

    public Task InitializeAsync() => RefreshAsync(keepStatus: true);

    private async Task RefreshHardwareAsync()
    {
        try { hardware = await Task.Run(SystemMonitor.ReadHardware); }
        catch (Exception ex)
        {
            hardware = new("Unavailable", "Unavailable", "Unavailable", "Unavailable");
            Status = $"Hardware information could not be read. {ex.Message}";
        }
        Notify(nameof(Windows));
        Notify(nameof(Cpu));
        Notify(nameof(Gpu));
        Notify(nameof(InstalledRam));
        SampleNow();
    }

    public void SampleNow()
    {
        sample = monitor.Sample(LiveMonitoring);
        Notify(nameof(CpuPercent));
        Notify(nameof(RamPercent));
        Notify(nameof(CpuLabel));
        Notify(nameof(RamLabel));
        Notify(nameof(RamUsage));
        Notify(nameof(Uptime));
    }

    private static string FormatPercent(double? value) => value is { } percent ? $"{percent:0}%" : "—";

    public void Navigate(Page destination)
    {
        if (page == destination && !SearchActive)
            return;
        page = destination;
        search = "";
        Notify(nameof(Search));
        UpdatePage();
    }

    private void UpdatePage()
    {
        foreach (var item in Navigation)
            item.Active = !SearchActive && item.Page == page;
        Notify(nameof(CurrentPage));
        Notify(nameof(SearchActive));
        Notify(nameof(IsOverview));
        Notify(nameof(IsRestore));
        Notify(nameof(IsSettings));
        Notify(nameof(IsAppInstaller));
        Notify(nameof(IsCatalog));
        Notify(nameof(IsAdvanced));
        Notify(nameof(PageTitle));
        Notify(nameof(PageDescription));
        UpdateCatalog();
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateCatalog()
    {
        Notify(nameof(VisibleRows));
        Notify(nameof(VisibleLinks));
        Notify(nameof(HasLinks));
        Notify(nameof(ShowEmpty));
        Notify(nameof(EmptyLabel));
        Notify(nameof(ShowPackageError));
        Notify(nameof(PackageError));
        Notify(nameof(CatalogLabel));
    }

    private bool Matches(params string[] text) => search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .All(word => text.Any(value => value.Contains(word, StringComparison.OrdinalIgnoreCase)));

    private async Task RefreshAsync(bool keepStatus = false)
    {
        if (Busy)
            return;
        if (!keepStatus)
            Status = "";
        await RunBusyAsync("Reading Windows settings…", async () =>
        {
            var previous = rows.Where(row => row.Staged).Select(row => row.CreatePlan())
                .ToDictionary(plan => plan.Tweak.Info.Id);
            await Task.WhenAll(LoadRowsAsync(previous), RefreshHardwareAsync());
            LoadHistory();
        });
    }

    private async Task LoadRowsAsync(Dictionary<string, PlannedChange> previous)
    {
        // Run AppX inventory alongside native setting checks.
        var appScan = optionalApps.ScanAsync();
        var recallScan = DetectRecallAsync();
        var detected = await Task.WhenAll(catalog.Select(async tweak =>
        {
            try { return new TweakRow(tweak, await tweak.DetectAsync()); }
            catch (Exception ex) { return new TweakRow(tweak, Detection.Unavailable(ex.Message)); }
        }));

        var combined = detected.ToList();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            combined.RemoveAll(row => row.Info.Id == TweakCatalog.GameMode);
        packageError = null;
        gameBar = false;
        try
        {
            var scan = await appScan;
            combined.AddRange(scan.Apps.Select(item => new TweakRow(item.Tweak, item.State)));
            gameBar = scan.GameBarInstalled;
        }
        catch (Exception ex) { packageError = $"Installed apps could not be read. {ex.Message}"; }
        recall = await recallScan;

        rows = combined;
        var restored = 0;
        foreach (var row in rows)
        {
            if (previous.TryGetValue(row.Info.Id, out var plan) && row.State.Current?.Value == plan.Before.Value && row.StageValue(plan.After.Value))
                restored++;
            row.Changed += OnRowChanged;
        }
        if (restored != previous.Count && !HasStatus)
            Status = "Windows changed since some items were selected. Review the current settings before applying.";
        UpdateCatalog();
        OnSelectionChanged();
    }

    private static async Task<bool> DetectRecallAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
            return false;
        try
        {
            return (await WindowsProcess.PowerShellAsync(
                "(Get-CimInstance -ClassName Win32_OptionalFeature -Filter \"Name='Recall' AND InstallState=1\").Name", 12)) == "Recall";
        }
        catch (Exception) { return false; }
    }

    private void LoadHistory()
    {
        history = store.ReadHistory();
        Notify(nameof(History));
        Notify(nameof(HistoryEmpty));
    }

    private void SelectProfile(Profile profile)
    {
        if (profile == Profile.Custom)
        {
            SetProfile(Profile.Custom);
            Navigate(Page.Optimization);
            return;
        }
        if (SelectedCount > 0 && !dialogs.Confirm($"Load {profile}?", "This replaces the changes currently selected for review.", "Load profile", []))
            return;

        batchingSelection = true;
        foreach (var row in rows)
        {
            row.Staged = false;
            if (row.Info.Id == TweakCatalog.Extensions && profile is Profile.Safe or Profile.Balanced)
                row.StageValue("1");
            if (row.Info.Id == TweakCatalog.GameMode && profile is Profile.Balanced or Profile.Gaming)
                row.StageValue("1");
            if (row.Info.Id == TweakCatalog.Animations && profile == Profile.Gaming)
                row.StageValue("0");
        }
        batchingSelection = false;
        Status = SelectedCount == 0 ? $"{profile} has no pending changes on this device." : $"{profile} is ready to review.";
        SetProfile(profile);
        OnSelectionChanged();
    }

    private void SetProfile(Profile profile)
    {
        if (preferences.Profile == profile)
            return;
        UpdatePreferences(preferences with { Profile = profile });
        Notify(nameof(ProfileName));
    }

    private void UpdatePreferences(Preferences value)
    {
        preferences = value;
        try { store.SavePreferences(value); }
        catch (Exception ex) { Status = $"Preferences could not be saved. {ex.Message}"; }
    }

    private void OnRowChanged(TweakRow row)
    {
        if (batchingSelection)
            return;
        SetProfile(Profile.Custom);
        OnSelectionChanged();
    }

    private void ClearSelection()
    {
        batchingSelection = true;
        foreach (var row in rows)
            row.Staged = false;
        batchingSelection = false;
        OnSelectionChanged();
        SetProfile(Profile.Custom);
    }

    private void OnSelectionChanged()
    {
        Notify(nameof(SelectedCount));
        Notify(nameof(SelectionLabel));
        Notify(nameof(SelectionHint));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task ApplyAsync()
    {
        var plans = rows.Where(row => row.Staged).Select(row => row.CreatePlan()).ToArray();
        var preview = plans.Select(plan => Preview(plan.Tweak.Info, plan.Before, plan.After)).ToArray();
        if (!dialogs.Confirm("Apply selected changes?", "Review the values below. PULSE saves original settings locally before writing to Windows.", "Apply changes", preview))
            return;

        await RunBusyAsync("Applying changes…", async () =>
        {
            var remaining = plans.ToDictionary(plan => plan.Tweak.Info.Id);
            var issues = new List<string>();
            var succeeded = 0;
            foreach (var plan in plans)
            {
                try
                {
                    var entry = await engine.ApplyAsync(plan);
                    if (entry.Status != ChangeStatus.Applied)
                    {
                        issues.Add($"{entry.Info.Name}: {entry.Detail ?? "Check Restore for the recorded result."}");
                        continue;
                    }
                    succeeded++;
                    remaining.Remove(plan.Tweak.Info.Id);
                }
                catch (Exception ex) { issues.Add($"{plan.Tweak.Info.Name}: {ex.Message}"); }
            }

            Status = $"Applied {succeeded} of {plans.Length} selected changes.";
            if (issues.Count > 0)
                Status += $" {issues[0]}" + (issues.Count > 1 ? $" {issues.Count - 1} more issue(s). Review Restore and the current settings." : "");
            LoadHistory();
            await LoadRowsAsync(remaining);
        });
    }

    private async Task RevertAsync(HistoryRow item)
    {
        var entry = item.Change;
        if (!dialogs.Confirm("Restore the previous setting?", "PULSE verifies the current value before restoring the saved original.", "Restore original",
            [Preview(entry.Info, entry.After, entry.Before)]))
            return;
        await RunBusyAsync("Restoring the original setting…", async () =>
        {
            var tweak = catalog.Single(tweak => tweak.Info.Id == entry.Info.Id);
            var result = await engine.RevertAsync(entry.Id, tweak);
            Status = result.Status == ChangeStatus.Reverted ? $"Restored {entry.Info.Name}." :
                $"{entry.Info.Name} could not be restored. {result.Detail}";
            LoadHistory();
            await LoadRowsAsync(rows.Where(row => row.Staged).Select(row => row.CreatePlan()).ToDictionary(plan => plan.Tweak.Info.Id));
        });
    }

    private static PreviewRow Preview(TweakInfo info, TweakOption from, TweakOption to) =>
        new(info.Name, $"{from.Label}  →  {to.Label}", info.Impact,
            !info.Reversible ? "Moderate risk · PULSE undo unavailable" : info.Risk == Risk.Moderate ? "Moderate risk" : "Low risk");

    private async Task RunBusyAsync(string message, Func<Task> action)
    {
        BusyMessage = message;
        try { await action(); }
        catch (Exception ex) { Status = ex.Message; }
        finally { BusyMessage = ""; }
    }

    private void Export()
    {
        try
        {
            var path = dialogs.PickExportPath();
            if (path is null)
                return;
            if (store.IsInternalPath(path))
                throw new InvalidOperationException("Choose an export path outside PULSE’s internal data folder.");
            JsonStore.Export(path, preferences, rows.Select(row => new
            {
                row.Info.Id, row.Info.Name, row.Info.Page, Current = row.State.Current, Planned = row.Staged ? row.Target : null
            }).ToArray(), store.ReadHistory());
            Status = $"Exported settings to {Path.GetFileName(path)}.";
        }
        catch (Exception ex) { Status = $"Export failed. {ex.Message}"; }
    }

    private void OpenLink(SettingLink link)
    {
        try { Process.Start(new ProcessStartInfo(link.Uri) { UseShellExecute = true }); }
        catch (Exception ex) { Status = $"Windows Settings could not be opened. {ex.Message}"; }
    }

    private IEnumerable<SettingLink> PageLinks(Page current)
    {
        var build = Environment.OSVersion.Version.Build;
        if (current == Page.Gaming)
        {
            if (build < 22000)
                yield return new("Game Mode", "Set Game Mode in Windows 10.", "ms-settings:gaming-gamemode");
            if (gameBar)
                yield return new("Background game recording", "Adjust Record what happened and other recording preferences in Captures.", "ms-settings:gaming-gamedvr", "Open Captures");
            if (build >= 17134)
                yield return new("Startup applications", "Review and change which apps launch at sign-in.", "ms-settings:startupapps", "Open Startup");
            if (build >= 22000)
                yield return new("Visual effects", "Adjust accessibility preferences for animation, transparency and scrollbars.", "ms-settings:easeofaccess-visualeffects");
        }
        else if (current == Page.Privacy)
        {
            if (build >= 10240)
                yield return new("Advertising ID and suggested content", "Review advertising preferences, recommendations and promotional content for this account.", "ms-settings:privacy-general");
            if (build >= 15063)
                yield return new("Personalized offers", "Review tailored experiences or personalized offers in Diagnostics & feedback.", "ms-settings:privacy-feedback");
            if (rows.Any(row => row.Info.Id == "appx.Microsoft.Copilot"))
                yield return new("Microsoft Copilot", "Copilot is installed for this account. Review it in Windows Installed apps.", "ms-settings:appsfeatures", "Open Installed apps");
            if (recall)
                yield return new("Recall & snapshots", "Recall is installed. Open Privacy & security and select Recall & snapshots to review capture and retention.", "ms-settings:privacy");
        }
    }
}
