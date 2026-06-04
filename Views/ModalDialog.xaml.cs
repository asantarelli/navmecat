using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NavMeCat.Views;

public enum DialogKind { Info, Success, Error, Question }

public partial class ModalDialog : Window
{
    public ModalDialog()
    {
        InitializeComponent();
    }

    public static bool Show(string title, string message, DialogKind kind,
        string primaryText, string? secondaryText)
    {
        var dlg = new ModalDialog
        {
            Owner = Application.Current?.MainWindow is { IsLoaded: true } w ? w : null
        };
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.PrimaryButton.Content = primaryText;

        if (secondaryText is not null)
        {
            dlg.SecondaryButton.Content = secondaryText;
            dlg.SecondaryButton.Visibility = Visibility.Visible;
        }

        var (color, glyph) = kind switch
        {
            DialogKind.Success => ("#FF2E9E5B", "✓"),
            DialogKind.Error => ("#FFD64545", "!"),
            DialogKind.Question => ("#FF2D7FE0", "?"),
            _ => ("#FF2D7FE0", "i"),
        };
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        dlg.IconBadge.Background = brush;
        dlg.IconGlyph.Text = glyph;

        return dlg.ShowDialog() == true;
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
