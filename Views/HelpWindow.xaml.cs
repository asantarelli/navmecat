using System.Windows;
using NavMeCat.Behaviors;
using NavMeCat.Services;

namespace NavMeCat.Views;

/// <summary>In-app user guide: renders the bundled HTML documentation in the current language.</summary>
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        var lang = LocalizationManager.Instance.Language;
        Title = LocalizationManager.Instance["Help_Title"];
        // The attached property handles WebView2 initialization and renders the HTML once loaded.
        WebViewHtml.SetHtml(Web, HelpDocs.GetHtml(lang));
    }
}
