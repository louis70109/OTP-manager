# Windows OTP Manager

Windows WPF app implementing TOTP (SHA1/SHA256/SHA512; 6/8 digits), camera-selected QR scanning (including Google Authenticator transfer QR codes), otpauth URI import, SQLite with per-user DPAPI encrypted secrets, search, click-the-code-to-copy with 30-second clipboard clearing, delete, and basic tray open/hide/exit.

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows, then run `dotnet restore` and `dotnet build`. After a successful build, launch with `dotnet run` from this folder.

Camera scanning opens a local scanner page in Microsoft Edge when installed. Select a camera and grant camera access there. The scanner uses native browser QR support or falls back to jsQR from jsDelivr. The video is processed locally and only the scanned TOTP URI is handed back to the app. Windows Hello, inactivity lock, global shortcut, quick OTP tray panel, preferences, installer, and automated tests are not included yet. Import duplicates the account and does not remove it from the source authenticator.
