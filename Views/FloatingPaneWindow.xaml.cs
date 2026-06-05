using System.Windows;

namespace NavMeCat.Views;

public partial class FloatingPaneWindow : Window
{
    public FloatingPaneWindow()
    {
        InitializeComponent();
    }

    public void SetBody(FrameworkElement content) => Body.Content = content;
}
