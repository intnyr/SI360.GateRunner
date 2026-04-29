using System.Windows;
using Application = System.Windows.Application;

namespace SI360.GateRunner.Services;

public enum AppTheme { Dark, Light }

public sealed class ThemeManager
{
    public AppTheme Current { get; private set; } = AppTheme.Dark;

    public event Action<AppTheme>? ThemeChanged;

    public void Apply(AppTheme theme)
    {
        var uri = theme == AppTheme.Dark
            ? new Uri("pack://application:,,,/SI360.GateRunner;component/Styles/DarkTheme.xaml")
            : new Uri("pack://application:,,,/SI360.GateRunner;component/Styles/LightTheme.xaml");

        var next = new ResourceDictionary { Source = uri };
        var dicts = Application.Current.Resources.MergedDictionaries;
        if (dicts.Count > 0) dicts[0] = next;
        else dicts.Add(next);

        Current = theme;
        ThemeChanged?.Invoke(theme);
    }

    public void Toggle() => Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
}
