using System.Windows;
using NavMeCat.Services;

namespace NavMeCat;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Apply theme BEFORE base.OnStartup so StaticResource bindings resolve correctly
        ThemeManager.Apply(SettingsStore.Current.Theme);
        base.OnStartup(e);
        LocalizationManager.Instance.Language = SettingsStore.Current.Language;
    }
}
