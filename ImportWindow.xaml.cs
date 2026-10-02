using System;
using System.Threading;
using System.Windows;

namespace WindowsOtpManager;
public partial class ImportWindow : Window
{
    CancellationTokenSource? scanCancellation;
    IReadOnlyList<Account>? pending;
    internal IReadOnlyList<Account>? Result { get; private set; }
    public ImportWindow() { InitializeComponent(); Closed += (_, _) => scanCancellation?.Cancel(); }

    async void Scan_Click(object sender, RoutedEventArgs e)
    {
        scanCancellation?.Cancel(); scanCancellation = new CancellationTokenSource();
        StatusText.Text = "Scanner opened. After a successful scan, this window will return to the front with the account preview.";
        try
        {
            string uri = await CameraQrScanner.ScanAsync(scanCancellation.Token);
            SetPending(OtpUri.ParseMany(uri));
            UriBox.Clear();
            StatusText.Text = "QR scanned. Check the account name, then save it.";
            WindowState = WindowState.Normal;
            Topmost = true;
            Topmost = false;
            Activate();
            Focus();
        }
        catch (OperationCanceledException) { StatusText.Text = "Camera scan cancelled."; }
        catch (Exception)
        {
            StatusText.Text = "Could not scan. Try a supported browser, allow camera access, or paste the otpauth URI below.";
            MessageBox.Show("Camera scanning could not start or decode a TOTP QR code. Your camera feed stays on this device.", "Camera scan unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        try { if (!string.IsNullOrWhiteSpace(UriBox.Text)) SetPending(OtpUri.ParseMany(UriBox.Text)); if (pending is null) throw new FormatException(); Result = pending; DialogResult = true; }
        catch { StatusText.Text = "Enter or scan a valid otpauth://totp account first."; StatusText.Foreground = System.Windows.Media.Brushes.Firebrick; }
    }

    void SetPending(IReadOnlyList<Account> accounts)
    {
        if (accounts.Count == 0) throw new FormatException();
        pending = accounts;
        PreviewName.Text = accounts.Count == 1
            ? (string.IsNullOrWhiteSpace(accounts[0].Issuer) ? "Authenticator" : accounts[0].Issuer) + "  ·  " + accounts[0].AccountName
            : $"{accounts.Count} accounts  ·  {string.Join(", ", accounts.Take(2).Select(x => x.Issuer))}";
        PreviewCard.Visibility = Visibility.Visible;
    }
}
