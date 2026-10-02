# ADR 0002：以 Git tag 發佈 Windows 自含式 ZIP

- 狀態：Accepted
- 日期：2026-10-02

## 背景

原始專案只有開發者使用 `dotnet build`／`dotnet run` 的路徑。v1 需要讓一般 Windows 使用者下載後直接執行，也需要能追溯到原始碼版本的 GitHub Release。

## 決策

1. 使用 `.github/workflows/release.yml`。推送 `main` 或對 `main` 提出 pull request 時執行建置與封裝驗證；只有推送 `vMAJOR.MINOR.PATCH` tag 才建立公開 Release。第一版使用 `v1.0.0`，不建立會移動的 `v1` 別名。
2. 在 `windows-2022` runner 使用 .NET 8 SDK，以 Release 組態發佈 `win-x64` 自含式目錄。`global.json` 以 `latestFeature` 選取已安裝的最新穩定 8.0 SDK，不讓 runner 預裝的其他主版本影響 SDK 選擇。版本由 tag 取得；分支驗證採 CI 版本。使用者不必另行安裝 .NET Desktop Runtime。
3. 保留一般多檔案輸出，不啟用 trimming 或 single-file。WPF／Windows Forms 不適合任意裁剪，而且 `camera-scan.html` 必須存在於執行檔旁。ZIP 必須包含完整 publish 目錄，不能只散發 EXE。
4. 原專案的 `NuGet.Config` 刻意清空套件來源。發佈指令明確指定 `https://api.nuget.org/v3/index.json`，供 SDK 還原自含式執行階段套件；不修改應用程式的全域套件來源政策。
5. 封裝為 `WindowsOtpManager-v1.0.0-win-x64.zip` 這類版本化檔案，並產生 `SHA256SUMS`。先解壓 ZIP，檢查執行檔、runtime 與掃碼頁，再啟動實際 WPF 程式，等待主視窗建立且程序保持執行，才允許上傳。
6. 建置工作只有 `contents: read`。獨立的 Ubuntu 發佈工作取得 `contents: write`，下載同一 workflow run 的產物、驗證 SHA-256，先建立附有資產的 draft，最後轉為正式 Release。以內建 `GITHUB_TOKEN` 與 GitHub CLI 發佈，不保存個人 PAT。
7. GitHub Actions 使用完整 commit SHA 固定版本，checkout 不保留憑證。commit／tag 由維護者身份推送；Release 物件由 `github-actions[bot]` 建立，工作流程保留推送者與 commit 的稽核資訊。
8. tag 與已發佈的資產視為不可變。修正已公開版本時使用下一個 patch tag，不移動 tag 或覆寫正式資產。若在公開前失敗，先檢查 run；如已留下 draft，維護者確認並刪除該未公開 draft 後再重跑原 tag 的 workflow。不要刪除正式 Release 來重用版本。

## 替代方案

- **Framework-dependent ZIP**：下載較小，但要求使用者另裝正確的 .NET Desktop Runtime。
- **MSIX／MSI 與程式碼簽章**：提供安裝、更新與更好的發行者辨識，但需要簽章憑證、安裝生命週期與相應維護；不納入這次 v1。
- **本機手動建置並上傳**：難以一致驗證 Windows 封裝，也缺乏可追溯的 CI 記錄。
- **x86／ARM64 多架構矩陣**：需要各架構的實機執行驗證；v1 僅提供已在 x64 Windows runner 啟動的 x64 產物。

## 結果與限制

- 下載並解壓完整 ZIP 後即可啟動；不是安裝程式，也沒有自動更新。
- 產物未簽章，Windows SmartScreen 可能提示未知發行者。SHA-256 可驗證下載完整性，但不是程式碼簽章，也不能替代信任來源的判斷。
- 以仍受 Microsoft 支援的 Windows x64 版本為使用前提；CI 啟動驗證環境為 Windows Server 2022，不宣稱測過所有桌面版本或實體攝影機。
- 自含式 ZIP 會包含執行階段，安全更新需要重新建置與發佈；SDK 採 .NET 8 當前 patch，因此不同時間重建不保證位元組一致。
- 依據 [Microsoft 支援政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)，.NET 8 將於 2026-11-10 結束支援，後續版本必須在此之前評估升級受支援的 LTS；v1 不混入目標框架遷移。
- smoke test 僅證明封裝內容及 WPF 主視窗啟動；不宣稱已驗證相機、剪貼簿、TOTP 向量或完整匯入流程。
