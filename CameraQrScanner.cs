using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsOtpManager;
static class CameraQrScanner
{
    public static async Task<string> ScanAsync(CancellationToken cancellationToken)
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        string route = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + "/";
        string prefix = $"http://127.0.0.1:{port}/{route}";
        using var listener = new HttpListener(); listener.Prefixes.Add(prefix); listener.Start();
        string page = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "camera-scan.html")).Replace("__SCAN_PATH__", route + "scan", StringComparison.Ordinal);
        LaunchScannerPage(prefix);
        using var registration = cancellationToken.Register(() => { try { listener.Stop(); } catch { } });
        while (true)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().WaitAsync(cancellationToken); }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            if (context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == "/" + route)
            {
                byte[] html = Encoding.UTF8.GetBytes(page); context.Response.ContentType = "text/html; charset=utf-8"; context.Response.ContentLength64 = html.Length; context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.OutputStream.WriteAsync(html, cancellationToken); context.Response.Close(); continue;
            }
            if (context.Request.HttpMethod == "POST" && context.Request.Url?.AbsolutePath == "/" + route + "scan")
            {
                if (context.Request.ContentLength64 < 1 || context.Request.ContentLength64 > 16_384)
                {
                    byte[] tooLarge = Encoding.UTF8.GetBytes("QR payload is empty or too large."); context.Response.StatusCode = 413; context.Response.ContentType = "text/plain; charset=utf-8"; context.Response.ContentLength64 = tooLarge.Length;
                    await context.Response.OutputStream.WriteAsync(tooLarge, cancellationToken); context.Response.Close(); continue;
                }
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8); string uri = await reader.ReadToEndAsync(cancellationToken);
                try { _ = OtpUri.ParseMany(uri); }
                catch (FormatException)
                {
                    byte[] invalid = Encoding.UTF8.GetBytes("This QR is not a supported TOTP authenticator export."); context.Response.StatusCode = 422; context.Response.ContentType = "text/plain; charset=utf-8"; context.Response.ContentLength64 = invalid.Length;
                    await context.Response.OutputStream.WriteAsync(invalid, cancellationToken); context.Response.Close(); continue;
                }
                byte[] ok = Encoding.UTF8.GetBytes("Scanned. Return to Windows OTP Manager."); context.Response.ContentType = "text/plain; charset=utf-8"; context.Response.ContentLength64 = ok.Length;
                await context.Response.OutputStream.WriteAsync(ok, cancellationToken); context.Response.Close(); return uri;
            }
            context.Response.StatusCode = 404; context.Response.Close();
        }
    }

    private static void LaunchScannerPage(string url)
    {
        // BarcodeDetector is not available in every browser. Prefer Edge when
        // installed so the built-in QR decoder works with the camera scanner.
        string? edge = FindEdge();
        if (edge is not null)
        {
            Process.Start(new ProcessStartInfo(edge, url) { UseShellExecute = false });
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static string? FindEdge()
    {
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        foreach (string candidate in candidates)
            if (File.Exists(candidate)) return candidate;

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet\Microsoft Edge\shell\open\command");
            string? command = key?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(command))
            {
                string executable = command.Trim().Trim('"');
                if (File.Exists(executable)) return executable;
            }
        }
        catch (UnauthorizedAccessException) { }
        return null;
    }
}
