# CLAUDE.md

台股報酬可視化工具：.NET 8 Blazor Web App（**Interactive Server**，C# 全在伺服器端）＋ SQLite（Dapper，無 EF Core）＋ ECharts（CDN）。個人專案，使用者非資訊本科。**聊天回覆與程式註解用繁體中文；commit 訊息用英文。** 細節以程式碼與註解為準。

## 文件（需要時才讀，不要整份預先讀）

| 文件 | 何時讀 |
|---|---|
| `docs/TODO.md` | **開始新工作前必讀**：待辦（有編號）、已知問題／限制、想法 |
| `docs/FILE-MAP.md` | 不確定要改哪個檔時（用 Grep 找該檔那一列即可，不必整份讀） |
| `docs/CORE-LOGIC.md` | 改報酬率、型態掃描、附圖／均線／指標、技術圖表版面、三大法人、原油、費半之前（多為使用者指定的行為，**修改前先問使用者**） |
| `docs/DECISIONS.md` | 改同步流程、資料庫寫入、靜態檔載入、日期對齊、頁面狀態之前（列出不要改回去的做法與原因） |

## 目錄

- `Program.cs`：DI 註冊、啟動流程。`Components/Pages/X.razor(.cs/.css/.js)`：四個頁面 `StockUpdate`（`/stock-update` 資料同步，每個來源一個元件在 `Components/UpdateSources/`）、`StockChart`（`/stock-chart` 技術圖表）、`PatternScan`（`/pattern-scan` 型態掃描）、`StockCompare`（`/stock-compare` 多檔報酬比較）。`Components/Shared/ChartDisplayOptions.razor`：附圖／均線／指標設定（兩個圖表頁共用）。
- `Services/`：`*ApiClient`（外部 API：證交所、FinMind、Yahoo、oilpriceapi）、`*SyncService`（同步寫入）、`PatternScanService`／`StockChartService`／`StockPerformanceService`（計算）、`Patterns/`（K 線型態與技術指標）、`PriceAdjuster`（還原股價）、`*State`（Scoped 畫面狀態）。
- `Repositories/`：所有 SQL 都在這裡（介面 `Interfaces/`、實作 `Implements/`）；Schema 在 `DatabaseInitializer.cs`。
- `wwwroot/js/candle-chart.js`：共用繪圖（附圖定義 `SUB_CHARTS`、`buildStackedOption`）；`wwwroot/app.css`：全域樣式。
- `tests/StockDashboard.Tests/`（xUnit；`Fixtures/` 是證交所實際回應）、`tools/`（`ui-snapshot.js`、`fake-twse.py`、`make-test-db.py`）。
- 主專案 `StockDashboard.csproj` 在 repo 根目錄，**已排除 `tests/**`、`tools/**`**；新增不屬於網站的資料夾也要排除。

## 修改後的驗證

```bash
dotnet test    # 每次修改後必跑（含建置）；約 10 秒
dotnet test --filter "FullyQualifiedName~TwseApiClientTests"   # 只跑部分測試
```
- **必須 0 error、0 warning、測試全過才 commit；不可為了通過而跳過或刪除測試。**
- 改 `TwseApiClient`、`StockPerformanceService`、Repository、技術指標時補對應測試。
- 動到 UI／CSS 時（每次擷取約 1 分鐘）：
  ```bash
  python3 tools/make-test-db.py /tmp/ui.db   # appsettings.json 的 DefaultConnection 指向它，啟動網站
  node tools/ui-snapshot.js capture /tmp/before --echarts <echarts.min.js>   # 修改前
  node tools/ui-snapshot.js capture /tmp/after  --echarts <echarts.min.js>   # 修改、重建、重啟後
  node tools/ui-snapshot.js diff /tmp/before /tmp/after   # 外觀不可改時必須 0 差異
  ```
- 本機啟動：`cp appsettings.Example.json appsettings.json`，改 `ConnectionStrings:DefaultConnection`，再 `dotnet run --launch-profile http`（http://localhost:8888）。

## 常見任務

- **新增頁面**：`Components/Pages/X.razor`（`@page`）＋ `X.razor.cs`（`[Inject]` 屬性注入）＋ `X.razor.css`；最外層 `<div class="dashboard-panel panel-standard">` 即套用共用樣式；`NavMenu.razor` 加連結（含 SVG 圖示）；`tools/ui-snapshot.js` 的 `STATES` 加一項。
- **新增查詢**：寫在 Repository（介面＋實作），頁面只呼叫 Repository／Service。
- **新增資料表**：加到 `DatabaseInitializer` 的 `CREATE TABLE IF NOT EXISTS`；**先確認使用者的 stock.db 沒有同名表**（同名會直接略過，曾與使用者的 `StockList`、`StockCorporateAction` 衝突）。
- **新增 Service／Repository**：`Program.cs` 註冊 `AddScoped`；呼叫外部 API 用 `AddHttpClient<介面, 實作>()`。
- **新增設定鍵**：同步加到 `appsettings.Example.json`。
- **新增 K 線型態**：`Services/Patterns/XxxPattern.cs` 實作 `ICandlePattern`（共用條件用 `PatternFilters`；鏡像型態參考 `ThreeCandlesPattern`）→ `Program.cs` 註冊 `AddSingleton<ICandlePattern, XxxPattern>()` → 加到測試的 `PatternExampleTests.AllPatterns` 並寫判斷測試。頁面不用改。`BuildExample` 必須產生符合自己判斷的 K 線。
- **新增附圖**：`candle-chart.js` 的 `SUB_CHARTS` ＋ `ChartDisplaySettings.SubChartOptions`；指標參數加到 `ChartDisplayOptions.razor` 的 `IndicatorGroups`。
- **外部 API 改版**：先改 `appsettings.json` 的網址（`Twse:*Url`／`FinMind:DataUrl`）；格式變了才改對應 `*ApiClient`（並更新 Fixtures）。

## 硬性限制

- **頁面不可寫 SQL 或 `new SqliteConnection`**；一律經 Repository。
- **連線字串只來自 `ConnectionStrings:DefaultConnection`**，不可寫死路徑。
- **`StockList` 是使用者既有的表（中文欄位）：只讀，不可寫入、建立、修改、刪除。**
- **股票名稱只是附加顯示**：一律用 `GetStockNameOrNullAsync`／`GetStockNamesOrEmptyAsync`，查名稱失敗不可讓主要功能失敗。
- **版面樣式不更動**（顏色、字型、padding、margin、border）；外觀變更需使用者同意。
- **先問使用者才能改**：報酬率公式、型態判斷條件與預設參數、`docs/CORE-LOGIC.md` 裡的行為。
- `TradeDate` 存 `yyyy-MM-dd` 字串，靠字串比較排序，**格式不可改**。台股配色：漲紅 `#ef5350`、跌綠 `#26a69a`。
- CSS／頁面 JS 網址都要帶 `?v=`（`AssetVersion`）；頁面樣式用 scoped `.razor.css`（不用 `<style>`、不用 `:root`）；`StockCompare` 不加 `panel-standard`。
- 外部 API 被封鎖或出錯：立即停止、不重試，不可當成「沒資料」；請求間隔不可調短。

## 環境陷阱（雲端沙盒）

- 沒有預裝 .NET：`apt-get install -y dotnet-sdk-8.0`（約 1 分鐘）。
- 連不到證交所、FinMind、Yahoo、ECharts CDN：
  - 測 TWSE／FinMind：`python3 tools/fake-twse.py`，`Twse:*`、`FinMind:DataUrl` 指向它（用法見檔案開頭）；啟動網站要加 `NO_PROXY=127.0.0.1,localhost`。
  - 測圖表：`curl -sSL https://registry.npmjs.org/echarts/-/echarts-5.4.3.tgz | tar xz -C /tmp package/dist/echarts.min.js`，再傳 `--echarts /tmp/package/dist/echarts.min.js`。
- **不要用 `pkill -f`／`pgrep -f` 關伺服器**（會中斷 session）。改用 `ps -eo pid,args | awk '/bin\/Debug\/net8.0\/StockDashboard/ && !/awk/ {print $1}' | xargs -r kill`。
- 測試用的 `appsettings.json` 用完要刪。
- 使用者說「合併到 main」指的是 `master`。

## Git 與文件維護

- 每完成一個獨立的修改就 commit；訊息用英文，第一行簡述，需要時空一行補原因；只描述本次修改。過去的修改用 `git log` 查。
- 每次 commit 一併更新 `docs/TODO.md`：只記現況（做完的刪掉、不寫完成紀錄），每項以一行為主；使用者說「做待辦 N」以該檔編號為準。
- 行為、檔案職責、架構決策有變時，更新對應的 `docs/CORE-LOGIC.md`／`FILE-MAP.md`／`DECISIONS.md`（改寫原本那一項，不要只往後加）；**本檔只放每次都用得到的規則，保持精簡**。

## 完成任務後的回覆

簡短列出（內容要和 `docs/TODO.md` 一致，沒有就省略）：
1. **本次修改**
2. **尚待改善／已知問題**
3. **建議改善方向或功能**（含可提高 Claude Code 效率的建議）
