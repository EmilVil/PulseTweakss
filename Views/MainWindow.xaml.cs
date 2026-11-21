using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pulse.Services;
using Pulse.ViewModels;

namespace Pulse.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly DispatcherTimer timer;

    public MainWindow(JsonStore store)
    {
        InitializeComponent();
        viewModel = new(store, new DesktopDialogs(this));
        DataContext = viewModel;
        InputBindings.Add(new KeyBinding(new Command(_ => { SearchBox.Focus(); SearchBox.SelectAll(); }), Key.F, ModifierKeys.Control));
        LoadLogo();
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            if (viewModel.IsOverview && WindowState != WindowState.Minimized)
                viewModel.SampleNow();
        };
        viewModel.PropertyChanged += ViewModelChanged;
        Loaded += async (_, _) =>
        {
            timer.Start();
            await viewModel.InitializeAsync();
        };
        Closed += (_, _) => timer.Stop();
        StateChanged += (_, _) =>
        {
            var maximized = WindowState == WindowState.Maximized;
            MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
            MaximizeButton.ToolTip = maximized ? "Restore window" : "Maximize";
            System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, (string)MaximizeButton.ToolTip);
        };
    }

    private void LoadLogo()
    {
        try
        {
            const string name = "pack://application:,,,/Pulse;component/Assets/PulseMark.png";
            var bitmap = new BitmapImage(new Uri(name, UriKind.Absolute));
            Brand.Source = bitmap;
            Icon = bitmap;
        }
        catch (Exception)
        {
            BrandFallback.Visibility = Visibility.Visible;
        }
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.PageTitle))
            return;
        PageScroll.ScrollToTop();
        if (viewModel.IsOverview)
            viewModel.SampleNow();
        if (viewModel.AnimatePages && SystemParameters.ClientAreaAnimation)
            PageBody.BeginAnimation(OpacityProperty, new DoubleAnimation(.7, 1, TimeSpan.FromMilliseconds(135)) { FillBehavior = FillBehavior.Stop });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (viewModel.Busy && viewModel.BusyMessage != "Reading Windows settings…")
            e.Cancel = true;
        base.OnClosing(e);
    }

    private void MinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void MaximizeClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}

public sealed class InverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type type, object parameter, System.Globalization.CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type type, object parameter, System.Globalization.CultureInfo culture) =>
        value is Visibility.Collapsed;
}
