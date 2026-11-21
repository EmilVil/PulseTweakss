using System.Windows;
using Microsoft.Win32;
using Pulse.ViewModels;

namespace Pulse.Views;

public partial class ReviewWindow : Window
{
    public ReviewWindow(string title, string description, string confirm, IReadOnlyList<PreviewRow> rows)
    {
        InitializeComponent();
        ReviewTitle.Text = title;
        ReviewDescription.Text = description;
        ConfirmButton.Content = confirm;
        ReviewItems.ItemsSource = rows;
        ReviewScroll.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void ConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}

internal sealed class DesktopDialogs(Window owner) : IDialogs
{
    public bool Confirm(string title, string description, string confirm, IReadOnlyList<PreviewRow> rows) =>
        new ReviewWindow(title, description, confirm, rows) { Owner = owner }.ShowDialog() == true;

    public string? PickExportPath()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export PULSE settings",
            FileName = "pulse-settings",
            DefaultExt = ".json",
            Filter = "JSON files (*.json)|*.json",
            AddExtension = true
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
}
