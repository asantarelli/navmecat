using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NavMeCat.Models;
using NavMeCat.Services;

namespace NavMeCat.Views;

public partial class ConnectionDialog : Window
{
    private readonly ConnectionProfile _profile;

    public ConnectionDialog(ConnectionProfile profile)
    {
        InitializeComponent();
        _profile = profile;
        Owner = Application.Current?.MainWindow is { IsLoaded: true } w ? w : null;
        LoadFromProfile();
    }

    private void LoadFromProfile()
    {
        NameBox.Text = _profile.Name;
        ServerBox.Text = _profile.Server;
        DatabaseBox.Text = _profile.Database ?? "";
        UserBox.Text = _profile.Username ?? "";
        PassBox.Password = _profile.Password ?? "";
        EncryptCheck.IsChecked = _profile.Encrypt;
        TrustCertCheck.IsChecked = _profile.TrustServerCertificate;

        WinAuthRadio.IsChecked = _profile.IntegratedSecurity;
        SqlAuthRadio.IsChecked = !_profile.IntegratedSecurity;

        RawModeCheck.IsChecked = _profile.UseRawConnectionString;
        RawBox.Text = _profile.RawConnectionString ?? "";

        ApplyAuthState();
        ApplyRawState();
    }

    /// <summary>Writes the current form values into a profile (the edited copy or a temp).</summary>
    private void WriteToProfile(ConnectionProfile p)
    {
        p.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Unnamed Connection" : NameBox.Text.Trim();
        p.Server = ServerBox.Text.Trim();
        p.Database = string.IsNullOrWhiteSpace(DatabaseBox.Text) ? null : DatabaseBox.Text.Trim();
        p.IntegratedSecurity = WinAuthRadio.IsChecked == true;
        p.Username = string.IsNullOrWhiteSpace(UserBox.Text) ? null : UserBox.Text.Trim();
        p.Password = string.IsNullOrEmpty(PassBox.Password) ? null : PassBox.Password;
        p.Encrypt = EncryptCheck.IsChecked == true;
        p.TrustServerCertificate = TrustCertCheck.IsChecked == true;
        p.UseRawConnectionString = RawModeCheck.IsChecked == true;
        p.RawConnectionString = string.IsNullOrWhiteSpace(RawBox.Text) ? null : RawBox.Text.Trim();
    }

    // ---- UI state --------------------------------------------------------

    private void Auth_Changed(object sender, RoutedEventArgs e) => ApplyAuthState();

    private void ApplyAuthState()
    {
        if (CredentialsGrid is null) return;
        CredentialsGrid.IsEnabled = SqlAuthRadio.IsChecked == true;
    }

    private void RawMode_Changed(object sender, RoutedEventArgs e) => ApplyRawState();

    private void ApplyRawState()
    {
        if (RawPanel is null || FieldPanel is null) return;
        var raw = RawModeCheck.IsChecked == true;
        RawPanel.Visibility = raw ? Visibility.Visible : Visibility.Collapsed;
        FieldPanel.Visibility = raw ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    // ---- actions ---------------------------------------------------------

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var temp = new ConnectionProfile();
        WriteToProfile(temp);
        TestStatus.Text = "Testing…";
        TestStatus.Foreground = (Brush)FindResource("B.TextMuted");
        try
        {
            await SqlServerService.TestConnectionAsync(temp.BuildConnectionString());
            TestStatus.Text = "Connection succeeded.";
            TestStatus.Foreground = (Brush)FindResource("B.Success");
        }
        catch (Exception ex)
        {
            TestStatus.Text = "Failed.";
            TestStatus.Foreground = (Brush)FindResource("B.Danger");
            Dialogs.ShowError("Connection test failed", ex.Message);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (RawModeCheck.IsChecked != true && string.IsNullOrWhiteSpace(ServerBox.Text))
        {
            Dialogs.ShowError("Missing server", "Please enter a server name.");
            return;
        }
        WriteToProfile(_profile);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
