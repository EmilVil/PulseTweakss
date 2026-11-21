using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Pulse.ViewModels;

namespace Pulse.Views;

public partial class AppInstallerView : UserControl
{
    private readonly AppInstallerViewModel viewModel = new();
    private bool initialized;

    public AppInstallerView()
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            if (initialized) return;
            initialized = true;
            await viewModel.InitializeAsync();
        };
    }
}

public sealed class NonEmptyVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text && !string.IsNullOrWhiteSpace(text) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class InverseBoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}
