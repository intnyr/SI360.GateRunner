using System.Windows;
using SI360.GateRunner.ViewModels;

namespace SI360.GateRunner.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm) oldVm.TabSwitchRequested -= SwitchTab;
        if (e.NewValue is MainViewModel newVm) newVm.TabSwitchRequested += SwitchTab;
    }

    private void SwitchTab(int index)
    {
        if (index < 0 || index >= MainTabs.Items.Count) return;
        MainTabs.SelectedIndex = index;
    }
}
