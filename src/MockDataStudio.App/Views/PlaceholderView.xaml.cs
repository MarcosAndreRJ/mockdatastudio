using System.Windows.Controls;

namespace MockDataStudio.App.Views;

public partial class PlaceholderView : UserControl
{
    public PlaceholderView(string title)
    {
        InitializeComponent();
        TitleText.Text = title;
    }
}
