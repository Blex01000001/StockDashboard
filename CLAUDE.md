# CLAUDE.md

台股報酬可視化工具：.NET 8 Blazor Web App（**Interactive Server**，C# 全在伺服器端）＋ SQLite（Dapper，無 EF Core）＋ ECharts（CDN）。個人專案，使用者非資訊本科。**回答與註解一律使用繁體中文。** 細節以程式碼與註解為準。

## 檔案地圖

| 檔案 | 負責 | 常見修改 |
|---|---|---|
| `Program.cs` | DI 註冊、HttpClient 設定、啟動流程 | 新增 Service / Repository 註冊 |
| `Components/App.razor` | HTML 外殼、CSS/JS 載入 | 新增全域 CSS（要加 `?v=@AssetVersion`） |
| `Components/Layout/NavMenu.razor` | 側邊欄連結 | 新增頁面時加連結 |
| `Components/Pages/StockQuery.*` | `/stock-query` 單檔最新日線＋名稱 | |
| `Components/Pages/StockChart.*` | `/stock-chart` K 線圖（`.razor.js` 畫 ECharts） | 圖表設定改 `.razor.js` |
| `Components/Pages/StockCompare.*` | `/stock-compare` 多檔 N 日滾動報酬率 | 圖表設定改 `.razor.js` |
| `Components/Pages/StockUpdate.*` | `/stock-update` TWSE 逐月同步、手動單筆、股票清單同步 | 同步流程、錯誤訊息 |
| `Components/Shared/StockIdDataList.razor` | 股票代號自動完成 `<datalist>` | |
| `Services/TwseApiClient.cs` | 證交所 API 呼叫與資料清洗（民國年、千分號） | 證交所格式改變時 |
| `Services/StockPerformanceService.cs` | 滾動報酬率計算 | 報酬率公式（**先問使用者**） |
| `Repositories/Implements/StockPriceRepository.cs` | `StockDailyPrice` 唯一讀寫入口 | 新查詢 |
| `Repositories/Implements/StockNameRepository.cs` | 股票名稱（合併 `StockList` ＋ `StockNameList`） | |
| `Repositories/StockNameRepositoryExtensions.cs` | 顯示用名稱查詢，失敗回傳 null／空字典 | |
| `Repositories/DatabaseInitializer.cs` | 啟動時 `CREATE TABLE IF NOT EXISTS`（Schema 在此） | 新增資料表 |
| `wwwroot/app.css` | 全域：色彩變數、版面、側邊欄、共用面板 `.panel-standard` | |
| `Components/Layout/NavMenu.razor.css` | 側邊欄連結實際生效的樣式（權重高於 `app.css`） | |
| `Components/Pages/*.razor.css` | 各頁 scoped CSS | |
| `appsettings.Example.json` | 設定範本（`appsettings.json` 被 gitignore） | 新增設定鍵時同步更新 |

未完成的空殼：`StockPriceService`（還原股價，未註冊 DI）、`StockDividend*`、`StockCorporateAction*`、`StockCombinationDto`。沒有測試專案。

## 修改後的驗證

```bash
dotnet build   # 每次修改後必跑；增量約 3 秒，乾淨建置約 30 秒
```
- **必須 0 error、0 warning**（目前是 0 warning，請維持）。
- 動到 UI 時：啟動後用 Playwright 實際操作頁面確認（見「環境陷阱」）。
- 動到 CSS 時：比對修改前後所有元素的 `getComputedStyle` 與截圖，必須一致（除非使用者同意改外觀）。
- 本機啟動：`cp appsettings.Example.json appsettings.json`，改 `ConnectionStrings:DefaultConnection`，再 `dotnet run --launch-profile http`（http://localhost:8888）。

## 常見任務

- **新增頁面**：`Components/Pages/X.razor`（`@page`）＋ `X.razor.cs`（`[Inject]` 屬性注入）＋ `X.razor.css`；最外層 `<div class="dashboard-panel panel-standard">` 即套用共用樣式；`NavMenu.razor` 加連結。
- **新增查詢**：寫在 Repository（介面＋實作），頁面只呼叫 Repository／Service。
- **新增資料表**：加到 `DatabaseInitializer` 的 `CREATE TABLE IF NOT EXISTS`；**先確認使用者的 stock.db 沒有同名表**（該語法遇到同名表會直接略過，曾與使用者的 `StockList` 衝突）。
- **新增 Service／Repository**：`Program.cs` 註冊 `AddScoped`；呼叫外部 API 用 `AddHttpClient<介面, 實作>()`。
- **新增設定鍵**：同步加到 `appsettings.Example.json`。
- **證交所 API 改版**：先改 `appsettings.json` 的 `Twse:StockDayUrl`／`Twse:StockDayAllUrl`；格式變了才改 `TwseApiClient`。

## 硬性限制

- **頁面不可寫 SQL 或 `new SqliteConnection`**；一律經 Repository。
- **連線字串只來自 `ConnectionStrings:DefaultConnection`**，不可寫死路徑（舊版曾寫死 `E:\...`）。
- **`StockList` 是使用者既有的「上市公司基本資料」表（中文欄位、資料有在用）：只讀，不可寫入、建立、修改、刪除。**
- **股票名稱只是附加顯示**：一律用 `GetStockNameOrNullAsync`／`GetStockNamesOrEmptyAsync`，查名稱失敗不可讓主要功能失敗。
- **版面樣式暫不更動**（顏色、字型、padding、margin、border）。外觀變更需使用者同意。

## 架構決策（不要改回去）

| 做法 | 原因 |
|---|---|
| `App.razor` 的 CSS 網址加 `?v=<組件 MVID>` | .NET 8 無靜態檔指紋，瀏覽器會沿用舊 CSS 造成版面全亂（使用者實際遇過） |
| `Program.cs` 呼叫 `UseStaticWebAssets()` | 使用者可能直接執行 `bin/` 下的 exe（Production 環境），沒有它 CSS 全部 404 |
| 頁面樣式用 scoped `.razor.css`，不用 `<style>` | `<style>` 是全域且只在當頁存在，曾導致 StockUpdate 完全沒樣式 |
| scoped CSS 不用 `:root`，頁面變數定義在最外層元素 | scoped 會變成 `:root[b-xxx]` 而失效 |
| StockCompare 不加 `panel-standard` | 它是另一套設計；加了會混入共用樣式 |
| 名稱分兩表：讀 `StockList`，同步寫 `StockNameList` | `StockList` 不含 ETF 且不可寫；同代號以 `StockList` 簡稱為準 |
| `StockList` 欄位用 `pragma_table_info` 動態比對（忽略空白） | 使用者表的欄位名稱可能含空白 |
| `StockDailyPrice` 用 `INSERT OR REPLACE`；`StockNameList` 用 `ON CONFLICT DO UPDATE` | 後者避免日後加欄位被整列覆寫成 NULL |
| 批次寫入包在單一交易 | SQLite 逐筆交易極慢；失敗整批還原 |
| TWSE 逐月請求間隔 2.5 秒、連續 3 個月無資料中斷（本月不計） | 證交所會封鎖頻繁請求；月初本月本來就沒資料 |
| TWSE HttpClient 帶瀏覽器 User-Agent、自行解析回應 | 被封鎖時常回 HTML／403，要顯示看得懂的原因 |
| 數字解析用 `InvariantCulture` | 避免電腦地區設定影響 |
| 自動開瀏覽器包 try/catch | 無桌面環境會丟例外導致程式崩潰 |

## 核心邏輯（修改前須先與使用者確認）

- 滾動報酬率：`(今日收盤 − N 個交易日前收盤) / N 日前收盤 × 100`，N 以所有股票「聯集」的時間軸計算；缺資料為 `null`（圖上斷線）。
- 台股配色：漲紅 `#ef5350`、跌綠 `#26a69a`。
- `TradeDate` 存 `yyyy-MM-dd` 字串，靠字串比較排序與篩選，**格式不可改**。

## 環境陷阱（雲端沙盒）

- 沒有預裝 .NET：`apt-get install -y dotnet-sdk-8.0`（約 1 分鐘）。
- 連不到 `twse.com.tw`、`openapi.twse.com.tw`、ECharts CDN：
  - 測 TWSE：本機跑假伺服器，`appsettings.json` 的 `Twse:StockDayUrl` 指向它。
  - 測圖表：Playwright 用 `page.route` 把 `echarts.min.js` 換成 npm 下載的 `echarts@5.4.3`。
- **不要用 `pkill -f`／`pgrep -f` 關伺服器**：會比對到自己的 shell 指令而中斷 session。改用 `ps -eo pid,args | awk '/StockDashboard.dll/ && !/awk/ {print $1}' | xargs -r kill`。
- 測試用的 `appsettings.json` 用完要刪（雖被 gitignore，但會影響之後的啟動）。
- 使用者說「合併到 main」指的是 `master`（repo 沒有 `main` 分支）。

## Git 規則
- 每完成一個獨立的修改就 commit
- commit 訊息第一行簡述改了什麼，需要時空一行補充原因
- commit 訊息只描述本次修改，不要附上待辦清單
- 需要了解過去的修改時，用 git log 查詢

## PROGRESS.md
- 記錄已知限制與待辦，需要時再讀，不必每次 commit 都讀
- 只在以下情況更新：完成待辦項目、發現新的已知限制或問題
- 完成的待辦直接刪除，不要搬到「已完成」
- 發現限制或問題解除時直接刪除

## 完成任務後的回覆
簡短列出：
1. **本次修改**：做了什麼
2. *尚待改善項目／問題、已知問題**：還沒做完的、發現但未處理的問題、已知限制（沒有就省略）
3. **建議改善方向或功能**：之後可以考慮的改進或新功能，或是可以提高CLAUDE CODE效率的建議（沒有就省略）
