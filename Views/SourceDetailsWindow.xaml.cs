using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Pulse.Services;

namespace Pulse.Views;

public partial class SourceDetailsWindow : Window
{
    public SourceDetailsWindow(AppEntry app)
    {
        InitializeComponent();
        AppTitle.Text = app.Name;
        SourceStatus.Text = app.HasVerifiedSource
            ? "WinGet provides an HTTPS download and SHA-256 checksum. This helps verify file integrity; it does not certify the publisher."
            : app.SourceMetadataNote ?? "WinGet did not provide enough installer details to show a source check.";
        PackageIdBox.Text = app.PackageId ?? "Unavailable";
        PublisherBox.Text = string.IsNullOrWhiteSpace(app.Publisher) ? "Not provided by WinGet" : app.Publisher;
        PublisherLink.NavigateUri = Uri.TryCreate(app.PublisherUrl, UriKind.Absolute, out var publisherUri) ? publisherUri : null;
        PublisherLink.Inlines.Clear();
        PublisherLink.Inlines.Add(app.PublisherUrl ?? "Not provided by WinGet");
        InstallerLink.NavigateUri = Uri.TryCreate(app.InstallerUrl, UriKind.Absolute, out var installerUri) ? installerUri : null;
        InstallerLink.Inlines.Clear();
        InstallerLink.Inlines.Add(app.InstallerUrl ?? "No HTTPS installer URL reported");
        HashBox.Text = app.InstallerSha256 ?? "No valid SHA-256 reported";
    }

    private void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri.Scheme != Uri.UriSchemeHttps)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open this link: {ex.Message}", "Open source link", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
