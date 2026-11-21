using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Pulse.Services;

namespace Pulse.ViewModels;

public sealed class AppInstallerViewModel : Bindable
{
    private readonly WingetService winget = new();
    private string search = "";
    private string? category;
    private string status = "Checking WinGet package availability…";
    private bool busy = true;
    private int checkedCount;

    public AppInstallerViewModel()
    {
        Apps = new(AppCatalog.Create());
        Categories = new(["All categories", .. Enum.GetValues<AppCategory>().Select(Label)]);
        InstallCommand = new Command(async _ => await InstallSelectedAsync(), _ => !Busy && SelectedCount > 0);
        foreach (var app in Apps)
            app.PropertyChanged += (_, _) => { Notify(nameof(SelectedCount)); Notify(nameof(VisibleApps)); CommandManager.InvalidateRequerySuggested(); };
    }

    public ObservableCollection<AppEntry> Apps { get; }
    public ObservableCollection<string> Categories { get; }
    public ICommand InstallCommand { get; }
    public int SelectedCount => Apps.Count(app => app.Selected && app.Status == "Available");
    public int CheckedCount { get => checkedCount; private set { if (Set(ref checkedCount, value)) Notify(nameof(Progress)); } }
    public int TotalCount => Apps.Count;
    public string Progress => $"{CheckedCount} of {TotalCount} checked";
    public bool Busy { get => busy; private set { if (Set(ref busy, value)) CommandManager.InvalidateRequerySuggested(); } }
    public string Status { get => status; private set => Set(ref status, value); }
    public string Search { get => search; set { if (Set(ref search, value)) Notify(nameof(VisibleApps)); } }
    public string? Category { get => category; set { if (Set(ref category, value)) Notify(nameof(VisibleApps)); } }

    public IEnumerable<AppEntry> VisibleApps => Apps.Where(app =>
        (Category is null or "All categories" || Label(app.Category) == Category) &&
        (string.IsNullOrWhiteSpace(Search) || app.Name.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase) ||
         app.PackageId?.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase) == true));

    public async Task InitializeAsync()
    {
        try
        {
            var progress = new Progress<string>(_ =>
            {
                CheckedCount++;
                if (CheckedCount % 8 == 0 || CheckedCount == TotalCount)
                    Status = $"Checking package sources… {Progress}";
            });
            await winget.RefreshAsync(Apps, progress, CancellationToken.None);
            Status = winget.IsAvailable ? "Package availability checked against WinGet." : "WinGet is unavailable. Install App Installer to check packages.";
        }
        catch (Exception ex) { Status = $"WinGet check failed: {ex.Message}"; }
        finally
        {
            Busy = false;
            foreach (var app in Apps) app.RefreshBindings();
            Notify(nameof(VisibleApps));
        }
    }

    private async Task InstallSelectedAsync()
    {
        var selected = Apps.Where(app => app.Selected && app.Status == "Available" && app.Available).ToArray();
        if (selected.Length == 0) return;
        var names = string.Join(Environment.NewLine, selected.Select(app => $"• {app.Name}"));
        if (MessageBox.Show($"Install these applications using WinGet?{Environment.NewLine}{Environment.NewLine}{names}",
                "Confirm installation", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            return;

        Busy = true;
        try
        {
            foreach (var app in selected)
            {
                app.Status = "Installing";
                app.RefreshBindings();
                Status = $"Installing {app.Name}…";
                try
                {
                    var output = new Progress<string>(line => Status = $"{app.Name}: {line}");
                    await winget.InstallAsync(app, false, output, CancellationToken.None);
                    app.Status = "Installed";
                    app.Selected = false;
                }
                catch (Exception ex) { app.Status = "Error"; app.Note = ex.Message; }
                app.RefreshBindings();
            }
            Status = selected.Any(app => app.Status == "Error")
                ? "Some installations failed. Select an error row to review its details."
                : "Selected applications installed.";
        }
        finally { Busy = false; Notify(nameof(VisibleApps)); }
    }

    public static string Label(AppCategory category) => category switch
    {
        AppCategory.VPNPrivacy => "VPN & Privacy",
        _ => category.ToString()
    };
}
