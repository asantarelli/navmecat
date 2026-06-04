using NavMeCat.Models;

namespace NavMeCat.Views;

/// <summary>Central helpers for styled modal popups.</summary>
public static class Dialogs
{
    public static void ShowMessage(string title, string message)
        => ModalDialog.Show(title, message, DialogKind.Info, "OK", null);

    public static void ShowSuccess(string title, string message)
        => ModalDialog.Show(title, message, DialogKind.Success, "OK", null);

    public static void ShowError(string title, string message)
        => ModalDialog.Show(title, message, DialogKind.Error, "OK", null);

    public static bool Confirm(string title, string message)
        => ModalDialog.Show(title, message, DialogKind.Question, "Yes", "Cancel");

    /// <summary>Opens the connection editor. Returns true if the user saved.</summary>
    public static bool EditConnection(ConnectionProfile profile)
        => new ConnectionDialog(profile).ShowDialog() == true;
}
