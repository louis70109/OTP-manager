# ADR 0001：Windows 本機 OTP 管理器架構

- 狀態：Accepted
- 日期：2026-10-02
- 範圍：記錄 v1 現有架構與安全邊界，不宣稱新增尚未實作的保護。

## 背景

應用程式需要在 Windows 桌面產生 TOTP、匯入既有驗證器帳戶、掃描 QR Code，並在本機保存密鑰。v1 不提供雲端服務、帳戶同步或跨平台介面；盡量使用 .NET 與 Windows 內建能力，降低部署相依性。

## 決策

1. 使用 .NET 8、WPF 與 C# 實作桌面介面，透過 Windows Forms `NotifyIcon` 提供通知區開啟、隱藏與結束功能。現有 UI、TOTP 與儲存協調程式維持於 `MainWindow.xaml.cs`，本 ADR 不引入新的分層框架。
2. 使用 Windows 內建 `winsqlite3.dll`，透過 `NativeWindowsStorage.cs` 的 P/Invoke 儲存帳戶。資料庫優先放在 `%LOCALAPPDATA%\WindowsOtpManager\accounts.db`；無法初始化時，現有實作會改用專案根目錄或執行檔旁的 `Data` 目錄。
3. 以目前 Windows 使用者範圍的 DPAPI 加密 `Secret` 欄位。Issuer、帳戶名稱、演算法、位數與週期仍是明文；這不是整個資料庫加密。程式必須在執行時解密密鑰，部分暫存 byte buffer 會清零，但 managed string 不具備可靠清零保證。
4. 支援 `otpauth://totp`，以及 Google Authenticator `otpauth-migration://offline` 中支援的 TOTP 帳戶。TOTP 支援 SHA1、SHA256、SHA512 與 6／8 位數；一般 URI 的週期限制為 5–300 秒。匯入是複製，並不刪除來源驗證器中的帳戶；不支援 HOTP。
5. 掃碼使用 `CameraQrScanner.cs` 啟動暫時性的 loopback HTTP listener，以隨機路徑提供 `camera-scan.html`，優先開啟 Edge，否則使用預設瀏覽器。攝影機權限與選擇由瀏覽器處理；影像在本機解碼，辨識出的 URI 回傳本機應用程式，確認預覽後才儲存。
6. 優先使用瀏覽器 `BarcodeDetector`；不支援時從 jsDelivr 載入固定版本 `jsQR@1.4.0`。因此掃碼不能保證完全離線，且此遠端程式碼屬於掃碼流程的信任邊界。產生已儲存帳戶的 TOTP 與貼上 URI 匯入不需要這個 CDN。
7. 點擊驗證碼後複製至剪貼簿，30 秒後僅在剪貼簿文字仍等於當次驗證碼時清除，避免覆蓋使用者後續複製的其他文字。剪貼簿歷程、同步或其他程式的讀取不在這項清除保證內。

## 替代方案

- **跨平台 UI 或 Web 服務**：增加平台、部署與密鑰同步的複雜度，不符合 v1 的 Windows 本機範圍。
- **NuGet SQLite wrapper／跨平台密鑰庫**：可改善 API 與可攜性，但目前選擇 Windows 原生能力，不另引入套件。
- **內嵌 WebView 或原生攝影機 SDK**：可以減少外部瀏覽器切換，但增加執行階段相依性、相機整合與維護成本。
- **打包 QR 解碼器**：能移除 CDN 執行期需求；v1 沿用現有瀏覽器與 CDN fallback，並明確揭露此限制。

## 結果與風險

- 不需要伺服器；資料保留於本機 Windows 使用者環境。
- DPAPI 不抵禦同一使用者權限的惡意程式，也不等於應用程式鎖定。Windows Hello、閒置鎖定、復原／備份流程尚未提供。
- ZIP 僅表示免安裝的程式封裝，不表示帳戶資料可攜；單獨複製資料庫到另一個使用者或電腦不能視為可用備份。
- Windows API、桌面 UI、相機權限與瀏覽器行為必須在 Windows 驗證；其他作業系統的靜態檢查不能替代實際啟動。
- 目前沒有完整自動化功能／安全測試；發佈流程的啟動 smoke test 不等於 TOTP 正確性、相機匯入或 DPAPI 端到端測試。
- 發佈封裝與驗證規則另見 [ADR 0002](0002-tagged-windows-releases.md)。
