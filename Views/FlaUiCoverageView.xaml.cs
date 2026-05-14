using System.Windows;
using System.Windows.Controls;

namespace SI360.GateRunner.Views;

public partial class FlaUiCoverageView : System.Windows.Controls.UserControl
{
    public FlaUiCoverageView()
    {
        InitializeComponent();
    }

    private void CoverageRoot_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 900;

        if (compact)
        {
            Grid.SetRow(CoverageActionsPanel, 1);
            Grid.SetColumn(CoverageActionsPanel, 0);
            Grid.SetColumnSpan(CoverageActionsPanel, 2);
            CoverageActionsPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            CoverageActionsPanel.Margin = new Thickness(0, 10, 0, 0);
            HeaderTextPanel.Margin = new Thickness(0);
            HeaderTextPanel.MaxHeight = e.NewSize.Width < 620 ? 128 : 112;

            Grid.SetColumnSpan(SearchBox, 2);
            Grid.SetRow(FilterChipsPanel, 1);
            Grid.SetColumn(FilterChipsPanel, 0);
            Grid.SetColumnSpan(FilterChipsPanel, 2);
            FilterChipsPanel.Margin = new Thickness(0, 8, 0, 0);

            DetailRow.Height = new GridLength(e.NewSize.Width < 620 ? 360 : 320);
            ListColumn.Width = new GridLength(1, GridUnitType.Star);
            DetailColumn.MinWidth = 0;
            DetailColumn.Width = new GridLength(0);
            Grid.SetRow(DetailPanel, 1);
            Grid.SetColumn(DetailPanel, 0);
            DetailPanel.Margin = new Thickness(0, 12, 0, 0);
        }
        else
        {
            Grid.SetRow(CoverageActionsPanel, 0);
            Grid.SetColumn(CoverageActionsPanel, 1);
            Grid.SetColumnSpan(CoverageActionsPanel, 1);
            CoverageActionsPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            CoverageActionsPanel.Margin = new Thickness(0);
            HeaderTextPanel.Margin = new Thickness(0, 0, 16, 0);
            HeaderTextPanel.MaxHeight = 112;

            Grid.SetColumnSpan(SearchBox, 1);
            Grid.SetRow(FilterChipsPanel, 0);
            Grid.SetColumn(FilterChipsPanel, 1);
            Grid.SetColumnSpan(FilterChipsPanel, 1);
            FilterChipsPanel.Margin = new Thickness(10, 0, 0, 0);

            DetailRow.Height = new GridLength(0);
            ListColumn.Width = new GridLength(3, GridUnitType.Star);
            DetailColumn.MinWidth = 300;
            DetailColumn.Width = new GridLength(2, GridUnitType.Star);
            Grid.SetRow(DetailPanel, 0);
            Grid.SetColumn(DetailPanel, 1);
            DetailPanel.Margin = new Thickness(12, 0, 0, 0);
        }
    }
}
