using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace WindowsOtpManager;
public partial class MainWindow : Window
{
    readonly Store store = new();
    readonly ObservableCollection<Row> rows = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly System.Windows.Forms.NotifyIcon tray;
    public MainWindow()
    {
        InitializeComponent(); AccountsList.ItemsSource = rows;
        CollectionViewSource.GetDefaultView(rows).Filter = o => o is Row r && (string.IsNullOrWhiteSpace(SearchBox.Text) || (r.Issuer + " " + r.Account).Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase));
        foreach (var account in store.Read()) rows.Add(new Row(account));
        RefreshEmptyState();
        timer.Tick += (_, _) => { foreach (var row in rows) row.Tick(); }; timer.Start();
        tray = new() { Text = "Windows OTP Manager", Icon = System.Drawing.SystemIcons.Shield, Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Dispatcher.Invoke(() => { Show(); Activate(); }));
        menu.Items.Add("Hide", null, (_, _) => Dispatcher.Invoke(Hide));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); Activate(); });
        Closing += (_, _) => { tray.Visible = false; tray.Dispose(); };
    }
    void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ImportWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        try
        {
            foreach (var account in dialog.Result)
            {
                var duplicate = rows.FirstOrDefault(x => x.Issuer.Equals(string.IsNullOrWhiteSpace(account.Issuer) ? account.AccountName : account.Issuer, StringComparison.OrdinalIgnoreCase) && x.Account.Equals(account.AccountName, StringComparison.OrdinalIgnoreCase));
                if (duplicate != null)
                {
                    var choice = MessageBox.Show($"{account.Issuer} · {account.AccountName} already exists.\n\nYes: replace it\nNo: keep both\nCancel: stop importing", "Account already exists", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    if (choice == MessageBoxResult.Cancel) break;
                    if (choice == MessageBoxResult.Yes) { store.Delete(duplicate.Id); rows.Remove(duplicate); }
                }
                store.Add(account); rows.Add(new Row(account));
            }
            RefreshEmptyState();
        }
        catch { MessageBox.Show("Could not save this account. The secret was not logged.", "Import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    void RefreshEmptyState()
    {
        bool empty = rows.Count == 0;
        EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        AccountsList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        AccountCount.Text = rows.Count == 1 ? "1 account" : $"{rows.Count} accounts";
    }
    void Search_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => CollectionViewSource.GetDefaultView(rows)?.Refresh();
    void Copy_Code_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Guid id && rows.FirstOrDefault(x => x.Id == id) is { } row)
        {
            string code = row.Code;
            try { Clipboard.SetText(code); } catch { MessageBox.Show("Could not copy the code. Please try again."); return; }
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => Dispatcher.Invoke(() => { try { if (Clipboard.ContainsText() && Clipboard.GetText() == code) Clipboard.Clear(); } catch { } }));
            AccountCount.Text = "Code copied · clipboard clears in 30 seconds";
        }
    }
    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Guid id && MessageBox.Show("Delete this account from this device?", "Delete account", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) { store.Delete(id); var row = rows.FirstOrDefault(x => x.Id == id); if (row != null) rows.Remove(row); RefreshEmptyState(); }
    }
}
record Account(Guid Id, string Issuer, string AccountName, string Algorithm, int Digits, int Period, string Secret);
sealed class Row : INotifyPropertyChanged
{
    readonly Account a; public Row(Account x) { a = x; Tick(); } public Guid Id => a.Id; public string Issuer => string.IsNullOrWhiteSpace(a.Issuer) ? a.AccountName : a.Issuer; public string Account => a.AccountName;
    public string Initial => string.IsNullOrWhiteSpace(Issuer) ? "?" : Issuer[..1].ToUpperInvariant();
    public string Code { get; private set; } = ""; public string DisplayCode => Code.Length == 6 ? Code[..3] + " " + Code[3..] : Code.Length == 8 ? Code[..4] + " " + Code[4..] : Code; public int Remaining { get; private set; } public int Period => a.Period;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Tick() { long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); Remaining = (int)(a.Period - t % a.Period); Code = Totp.Make(a.Secret, a.Algorithm, a.Digits, t / a.Period); PropertyChanged?.Invoke(this, new(null)); }
}
static class OtpUri
{
    public static List<Account> ParseMany(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var u)) throw new FormatException();
        if (u.Scheme == "otpauth-migration" && u.Host == "offline") return ParseMigration((System.Web.HttpUtility.ParseQueryString(u.Query)["data"] ?? throw new FormatException()).Replace(' ', '+'));
        if (u.Scheme != "otpauth" || u.Host != "totp") throw new FormatException();
        var q = System.Web.HttpUtility.ParseQueryString(u.Query); string secret = (q["secret"] ?? "").Replace(" ", "").ToUpperInvariant();
        if (secret.Length < 16 || secret.Any(c => !"ABCDEFGHIJKLMNOPQRSTUVWXYZ234567=".Contains(c))) throw new FormatException();
        var label = Uri.UnescapeDataString(u.AbsolutePath.TrimStart('/')).Split(':', 2); string issuer = q["issuer"] ?? (label.Length == 2 ? label[0] : "");
        string algorithm = (q["algorithm"] ?? "SHA1").ToUpperInvariant(); int digits = int.TryParse(q["digits"], out int d) ? d : 6, period = int.TryParse(q["period"], out int p) ? p : 30;
        if (algorithm is not ("SHA1" or "SHA256" or "SHA512") || digits is not (6 or 8) || period < 5 || period > 300) throw new FormatException();
        return new() { new(Guid.NewGuid(), issuer, label[^1], algorithm, digits, period, secret.TrimEnd('=')) };
    }
    static List<Account> ParseMigration(string encoded)
    {
        if (encoded.Length > 100_000) throw new FormatException();
        string base64 = encoded.Replace('-', '+').Replace('_', '/'); base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
        byte[] payload = Convert.FromBase64String(base64); int position = 0; var result = new List<Account>();
        while (position < payload.Length)
        {
            ulong tag = ReadVarint(payload, ref position); int field = (int)(tag >> 3), wire = (int)(tag & 7); if (field == 1 && wire == 2) { var account = ParseOtpParameters(ReadBytes(payload, ref position)); if (account != null) result.Add(account); } else Skip(payload, ref position, wire);
        }
        if (result.Count == 0) throw new FormatException(); return result;
    }
    static Account? ParseOtpParameters(byte[] bytes)
    {
        int position = 0, algorithm = 0, digitsEnum = 0, type = 0; string name = "", issuer = ""; byte[] secret = Array.Empty<byte>();
        while (position < bytes.Length)
        {
            ulong tag = ReadVarint(bytes, ref position); int field = (int)(tag >> 3), wire = (int)(tag & 7);
            if (wire == 2 && field == 1) secret = ReadBytes(bytes, ref position);
            else if (wire == 2 && field == 2) name = System.Text.Encoding.UTF8.GetString(ReadBytes(bytes, ref position));
            else if (wire == 2 && field == 3) issuer = System.Text.Encoding.UTF8.GetString(ReadBytes(bytes, ref position));
            else if (wire == 0 && field is 4 or 5 or 6) { int value = checked((int)ReadVarint(bytes, ref position)); if (field == 4) algorithm = value; else if (field == 5) digitsEnum = value; else type = value; }
            else Skip(bytes, ref position, wire);
        }
        if (type != 2 || secret.Length == 0 || algorithm == 4 || algorithm > 4 || digitsEnum > 2) return null;
        string alg = algorithm switch { 2 => "SHA256", 3 => "SHA512", _ => "SHA1" }; int digits = digitsEnum == 2 ? 8 : 6;
        string account = name; if (!string.IsNullOrWhiteSpace(issuer) && name.StartsWith(issuer + ":", StringComparison.OrdinalIgnoreCase)) account = name[(issuer.Length + 1)..];
        else if (string.IsNullOrWhiteSpace(issuer) && name.Contains(':')) { var pair = name.Split(':', 2); issuer = pair[0]; account = pair[1]; }
        if (string.IsNullOrWhiteSpace(account)) account = issuer;
        return new(Guid.NewGuid(), issuer, account, alg, digits, 30, EncodeBase32(secret));
    }
    static byte[] ReadBytes(byte[] bytes, ref int p) { ulong size = ReadVarint(bytes, ref p); if (size > (ulong)(bytes.Length - p)) throw new FormatException(); var value = bytes.AsSpan(p, (int)size).ToArray(); p += (int)size; return value; }
    static ulong ReadVarint(byte[] bytes, ref int p) { ulong value = 0; for (int shift = 0; shift < 70; shift += 7) { if (p >= bytes.Length) throw new FormatException(); byte b = bytes[p++]; value |= (ulong)(b & 127) << shift; if ((b & 128) == 0) return value; } throw new FormatException(); }
    static void Skip(byte[] bytes, ref int p, int wire) { switch (wire) { case 0: _ = ReadVarint(bytes, ref p); break; case 1: p = checked(p + 8); break; case 2: ulong n = ReadVarint(bytes, ref p); if (n > (ulong)(bytes.Length - p)) throw new FormatException(); p += (int)n; break; case 5: p = checked(p + 4); break; default: throw new FormatException(); } if (p > bytes.Length) throw new FormatException(); }
    static string EncodeBase32(byte[] bytes) { const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"; var result = new System.Text.StringBuilder(); int buffer = 0, bits = 0; foreach (byte b in bytes) { buffer = (buffer << 8) | b; bits += 8; while (bits >= 5) { bits -= 5; result.Append(chars[(buffer >> bits) & 31]); } } if (bits > 0) result.Append(chars[(buffer << (5 - bits)) & 31]); return result.ToString(); }
}
static class Totp
{
    public static string Make(string secret, string alg, int digits, long count)
    {
        const string alpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"; var bytes = new List<byte>(); int buf = 0, bits = 0;
        foreach (char c in secret) { int v = alpha.IndexOf(c); if (v < 0) throw new FormatException(); buf = (buf << 5) | v; bits += 5; if (bits >= 8) { bits -= 8; bytes.Add((byte)(buf >> bits)); } }
        Span<byte> msg = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(msg, count); byte[] key = bytes.ToArray();
        byte[] h = alg switch { "SHA256" => HMACSHA256.HashData(key, msg), "SHA512" => HMACSHA512.HashData(key, msg), _ => HMACSHA1.HashData(key, msg) };
        int o = h[^1] & 15, n = ((h[o] & 127) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
        return (n % (int)Math.Pow(10, digits)).ToString(new string('0', digits), System.Globalization.CultureInfo.InvariantCulture);
    }
}
sealed class Store
{
    readonly string path; string Conn => "Data Source=" + path;
    public Store()
    {
        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsOtpManager");
        try { Directory.CreateDirectory(local); path = Path.Combine(local, "accounts.db"); InitializeStorage(path); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            string appData = Path.Combine(AppContext.BaseDirectory, "Data");
            DirectoryInfo? root = new(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "WindowsOtpManager.csproj"))) root = root.Parent;
            local = Path.Combine(root?.FullName ?? AppContext.BaseDirectory, "Data"); Directory.CreateDirectory(local);
            path = Path.Combine(local, "accounts.db"); string prior = Path.Combine(appData, "accounts.db");
            if (!File.Exists(path) && File.Exists(prior) && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(prior), StringComparison.OrdinalIgnoreCase)) File.Copy(prior, path);
            InitializeStorage(path);
        }
    }
    static void InitializeStorage(string database) { using var c = new NativeDatabase(database); c.Exec("CREATE TABLE IF NOT EXISTS Accounts(Id TEXT PRIMARY KEY,Issuer TEXT NOT NULL,Account TEXT NOT NULL,Algorithm TEXT NOT NULL,Digits INTEGER NOT NULL,Period INTEGER NOT NULL,Secret BLOB NOT NULL)"); }
    public List<Account> Read() { using var c = new NativeDatabase(path); using var q = c.Prepare("SELECT Id,Issuer,Account,Algorithm,Digits,Period,Secret FROM Accounts"); var list = new List<Account>(); while (q.StepRow()) { byte[] raw = WindowsDpapi.Unprotect(q.Blob(6)); try { list.Add(new(Guid.Parse(q.Text(0)), q.Text(1), q.Text(2), q.Text(3), q.Int(4), q.Int(5), System.Text.Encoding.UTF8.GetString(raw))); } finally { CryptographicOperations.ZeroMemory(raw); } } return list; }
    public void Add(Account a) { using var c = new NativeDatabase(path); using var q = c.Prepare("INSERT INTO Accounts VALUES(?1,?2,?3,?4,?5,?6,?7)"); byte[] secret = System.Text.Encoding.UTF8.GetBytes(a.Secret); byte[] encrypted; try { encrypted = WindowsDpapi.Protect(secret); } finally { CryptographicOperations.ZeroMemory(secret); } q.BindText(1,a.Id.ToString()); q.BindText(2,a.Issuer); q.BindText(3,a.AccountName); q.BindText(4,a.Algorithm); q.BindInt(5,a.Digits); q.BindInt(6,a.Period); q.BindBlob(7,encrypted); q.StepDone(); CryptographicOperations.ZeroMemory(encrypted); }
    public void Delete(Guid id) { using var c = new NativeDatabase(path); using var q = c.Prepare("DELETE FROM Accounts WHERE Id=?1"); q.BindText(1,id.ToString()); q.StepDone(); }
}
