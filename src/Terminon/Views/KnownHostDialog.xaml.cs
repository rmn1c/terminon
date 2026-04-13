using System.Windows;
using System.Windows.Media;

namespace Terminon.Views;

public partial class KnownHostDialog : Window
{
    public KnownHostDialog(string fingerprint, string keyType, string reason)
    {
        InitializeComponent();

        KeyTypeBlock.Text = keyType;
        FingerprintBlock.Text = fingerprint;

        if (reason == "new")
        {
            TitleBlock.Text = "Unknown Host";
            SubtitleBlock.Text = "This host has not been seen before.";
            IconBlock.Foreground = (Brush)FindResource("WarningBrush");
            WarningBlock.Text = "Do you want to connect and trust this host's key? " +
                                "Verify the fingerprint with the server administrator before accepting.";
        }
        else if (reason == "changed")
        {
            TitleBlock.Text = "⚠ Host Key Changed!";
            SubtitleBlock.Text = "The server's key has changed since your last connection.";
            IconBlock.Text = "🚨";
            IconBlock.Foreground = (Brush)FindResource("DangerBrush");
            WarningBlock.Text = "WARNING: This could be a man-in-the-middle attack! " +
                                "The host key no longer matches the stored key. " +
                                "Only continue if you are certain the server's key was intentionally changed.";
            WarningBlock.Foreground = (Brush)FindResource("DangerBrush");
            AcceptButton.Content = "Accept Changed Key";
        }
    }

    private void Accept_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Reject_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
