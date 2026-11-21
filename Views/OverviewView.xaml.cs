using System.Windows;
using System.Windows.Controls;

namespace Pulse.Views;

public partial class OverviewView : UserControl
{
    private bool compact;

    public OverviewView() => InitializeComponent();

    private void Reflow(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 650;
        if (compact == narrow)
            return;
        compact = narrow;
        ProfileColumn.Width = new GridLength(narrow ? 0 : 278);
        Grid.SetColumn(ProfileCard, narrow ? 0 : 1);
        Grid.SetRow(ProfileCard, narrow ? 1 : 0);
        ActivityCard.Margin = narrow ? new Thickness(0, 0, 0, 14) : new Thickness(0, 0, 14, 0);
    }
}
