using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace MagicMusicWall.App;

internal sealed class QrCodeWindow : Window
{
    public QrCodeWindow(string link, bool listening = false)
    {
        Ui.Theme(this);
        Title = "Connect your phone · Magic Music Wall";
        Width = 560; Height = 690; MinWidth = 480; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(Ui.Text(listening ? "Scan to listen or watch here" : "Scan to control your music", 26));
        panel.Children.Add(Ui.Text("Open your phone's camera and scan this code. Your phone and this PC must be on the same home network.", 14, true));
        var bytes = PngByteQRCodeHelper.GetQRCode(link, QRCodeGenerator.ECCLevel.M, 8);
        var bitmap = new BitmapImage();
        using (var stream = new MemoryStream(bytes))
        {
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        }
        var image = new Image { Source = bitmap, Width = 360, Height = 360, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        System.Windows.Automation.AutomationProperties.SetName(image, "QR code for the web remote");
        panel.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = image, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(Ui.Text("This code includes your access link. Anyone who scans it on your network can control playback and stream media when enabled. Replace the access link in Setup to revoke old codes.", 12, true));
        panel.Children.Add(Ui.Button("Close", (_, _) => Close()));
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
