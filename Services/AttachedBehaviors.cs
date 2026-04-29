using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;

namespace SI360.GateRunner.Services;

public static class TextBoxBehavior
{
    public static readonly DependencyProperty AutoScrollToEndProperty =
        DependencyProperty.RegisterAttached(
            "AutoScrollToEnd",
            typeof(bool),
            typeof(TextBoxBehavior),
            new PropertyMetadata(false, OnAutoScrollChanged));

    public static bool GetAutoScrollToEnd(DependencyObject o) => (bool)o.GetValue(AutoScrollToEndProperty);
    public static void SetAutoScrollToEnd(DependencyObject o, bool v) => o.SetValue(AutoScrollToEndProperty, v);

    private static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        if ((bool)e.NewValue) tb.TextChanged += OnTextChanged;
        else tb.TextChanged -= OnTextChanged;
    }

    private static void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.ScrollToEnd();
    }
}

public static class ListBoxBehavior
{
    public static readonly DependencyProperty DoubleClickCommandProperty =
        DependencyProperty.RegisterAttached(
            "DoubleClickCommand",
            typeof(ICommand),
            typeof(ListBoxBehavior),
            new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetDoubleClickCommand(DependencyObject o) => (ICommand?)o.GetValue(DoubleClickCommandProperty);
    public static void SetDoubleClickCommand(DependencyObject o, ICommand? v) => o.SetValue(DoubleClickCommandProperty, v);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox lb) return;
        lb.MouseDoubleClick -= OnDoubleClick;
        if (e.NewValue is not null) lb.MouseDoubleClick += OnDoubleClick;
    }

    private static void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox lb) return;
        var cmd = GetDoubleClickCommand(lb);
        var item = lb.SelectedItem;
        if (cmd is not null && item is not null && cmd.CanExecute(item))
            cmd.Execute(item);
    }
}
