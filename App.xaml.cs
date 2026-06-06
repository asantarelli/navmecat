using System.Windows;
using NavMeCat.Services;

namespace NavMeCat;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LocalizationManager.Instance.Language = SettingsStore.Current.Language;
    }
}
