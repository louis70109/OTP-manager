# Windows OTP Manager

Windows WPF app implementing TOTP (SHA1/SHA256/SHA512; 6/8 digits), camera-selected QR scanning (including Google Authenticator transfer QR codes), otpauth URI import, SQLite with per-user DPAPI encrypted secrets, search, click-the-code-to-copy with 30-second clipboard clearing, delete, and basic tray open/hide/exit.

## Download and run

Download the Windows x64 ZIP and `SHA256SUMS` from [GitHub Releases](https://github.com/louis70109/OTP-manager/releases/latest). Extract the **entire ZIP** and run `WindowsOtpManager.exe`; the .NET runtime is included. Use a Windows x64 version still supported by Microsoft. This is an unsigned application, not an installer; Windows SmartScreen may warn about an unknown publisher. There is no automatic updater.

Verify a download in PowerShell with `Get-FileHash .\WindowsOtpManager-v1.0.0-win-x64.zip -Algorithm SHA256` and compare the result with `SHA256SUMS`. Checksums verify integrity, not publisher identity.

Accounts are normally stored in `%LOCALAPPDATA%\WindowsOtpManager\accounts.db`, with a `Data` directory fallback when that location cannot be initialized. Only secrets are DPAPI-encrypted; account names and other metadata remain readable. Encryption is bound to the Windows user environment: copying the database to another machine or user is not a supported backup or migration method.

## Build from source

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows, then run `dotnet restore` and `dotnet build`. After a successful build, launch with `dotnet run` from this folder.

## Camera and security boundaries

Camera scanning opens a local scanner page in Microsoft Edge when installed. Select a camera and grant camera access there. The scanner uses native browser QR support or falls back to jsQR from jsDelivr. The video is processed locally and only the scanned TOTP URI is handed back to the app. The fallback requires network access and trusts downloaded decoder code. Windows Hello, inactivity lock, global shortcut, quick OTP tray panel, preferences, installer, and comprehensive automated functional tests are not included yet. Import duplicates the account and does not remove it from the source authenticator. DPAPI does not protect against malicious software running as the same Windows user.

## Releases

[Windows release](https://github.com/louis70109/OTP-manager/actions/workflows/release.yml) builds on `main` pushes and pull requests, and publishes stable releases on `vMAJOR.MINOR.PATCH` tag pushes. It publishes a self-contained `win-x64` directory, ZIPs it, extracts it, and verifies that the packaged WPF application creates its main window and stays running before publishing the ZIP and SHA-256 checksum. This is a startup smoke test, not camera or full OTP workflow coverage.

To reproduce the release build on Windows:

```powershell
dotnet publish WindowsOtpManager.csproj --configuration Release --runtime win-x64 --self-contained true --source https://api.nuget.org/v3/index.json --output .\bin\publish\win-x64 -p:Version=1.0.0 -p:ContinuousIntegrationBuild=true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
```

The explicit NuGet source permits runtime-pack restore despite the repository's cleared package sources. Keep `camera-scan.html` beside the EXE and distribute the entire publish directory.

After the `main` workflow succeeds, a maintainer publishes the next version by pushing an annotated tag:

```sh
git tag -a v1.0.0 -m "Windows OTP Manager v1.0.0"
git push origin v1.0.0
```

The workflow creates the Release as `github-actions[bot]`; the tag push retains the maintainer's identity. Never move a published tag or replace published assets. Use a new patch version for corrections. For a failure before publication, inspect the run and remove only its unpublished draft if one exists before rerunning; do not delete a public Release to reuse a version.

## Architecture decisions

- [ADR 0001: Windows local OTP architecture](docs/adr/0001-windows-local-otp-architecture.md)
- [ADR 0002: Tagged self-contained Windows releases](docs/adr/0002-tagged-windows-releases.md)
