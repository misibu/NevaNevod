using System.Windows;

namespace NevaVision;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
    }

    public void UpdateContent(string fishText, string statusText)
    {
        FishText.Text = fishText;
        HintText.Text = statusText;
        HeaderText.Text = "NEVA VISION  •  read-only overlay";
    }
}
