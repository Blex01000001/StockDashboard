# CLAUDE.md

台股報酬可視化工具：.NET 8 Blazor Web App（**Interactive Server**，C# 全在伺服器端）＋ SQLite（Dapper，無 EF Core）＋ ECharts（CDN）。個人專案，使用者非資訊本科。**聊天回覆與程式註解用繁體中文；commit 訊息用英文。** 細節以程式碼與註解為準。

## 檔案地圖

| 檔案 | 負責 | 常見修改 |
|---|---|---|
| `Program.cs` | DI 註冊、HttpClient 設定、啟動流程 | 新增 Service / Repository 註冊 |
| `Components/App.razor` | HTML 外殼、CSS/JS 載入 | 新增全域 CSS（要加 `?v=@AssetVersion`） |
| `Components/Layout/NavMenu.razor` | 側邊欄連結 | 新增頁面時加連結 |
| `Components/Pages/StockQuery.*` | `/stock-query` 單檔最新日線＋名稱 | |
| `Components/Pages/StockChart.*` | `/stock-chart` K 線圖（`.razor.js` 畫 ECharts） | 圖表設定改 `.razor.js` |
| `Components/Pages/StockCompare.*` | `/stock-compare` 多檔 N 日滾動報酬率 | 圖表設定改 `.razor.js` |
| `Components/Pages/PatternScan.*` | `/pattern-scan` 型態掃描：控制列（設定／參數／範例預覽）＋分頁小卡；參數輸入框依型態定義自動產生，勾選參數與其 `EnabledBy` 附屬數值自動框成一組（`PatternParameterLayout`） | 小卡與預覽圖改 `.razor.js` |
| `Components/Pages/StockUpdate.*` | `/stock-update` TWSE 逐月同步、手動單筆、股票清單同步 | 同步流程、錯誤訊息 |
| `Components/Shared/StockIdDataList.razor` | 股票代號自動完成 `<datalist>` | |
| `Services/TwseApiClient.cs` | 證交所 API 呼叫與資料清洗（民國年、千分號） | 證交所格式改變時 |
| `Services/StockPerformanceService.cs` | 滾動報酬率計算 | 報酬率公式（**先問使用者**） |
| `Services/Patterns/ICandlePattern.cs` | 型態介面：參數定義、判斷、範例 K 線 | |
| `Services/Patterns/*Pattern.cs` | 各型態（看漲吞沒、晨星、紅三兵／黑三兵）的方向、參數、預設值、判斷、範例 | 判斷條件與預設值（**先問使用者**） |
| `Services/Patterns/PatternFilters.cs` | 共用條件（先下跌／先上漲、量增）與範例 K 線輔助 | |
| `Services/Patterns/TechnicalIndicators.cs` | 技術指標（SMA、MACD）純計算 | 新增指標時（補測試） |
| `Services/PatternScanService.cs` | 掃描：讀完整日線 → 判斷 → 切小卡範圍、均線、之後報酬、勝率 | |
| `Services/PatternScanState.cs` | 型態掃描頁的畫面狀態（Scoped） | 新增要保留的輸入時 |
| `Repositories/Implements/StockPriceRepository.cs` | `StockDailyPrice` 唯一讀寫入口 | 新查詢 |
| `Repositories/Implements/StockNameRepository.cs` | 股票名稱（合併 `StockList` ＋ `StockNameList`） | |
| `Repositories/StockNameRepositoryExtensions.cs` | 顯示用名稱查詢，失敗回傳 null／空字典 | |
| `Repositories/DatabaseInitializer.cs` | 啟動時 `CREATE TABLE IF NOT EXISTS`（Schema 在此） | 新增資料表 |
| `wwwroot/app.css` | 全域：色彩變數、版面、側邊欄、共用面板 `.panel-standard` | |
| `Components/Layout/NavMenu.razor.css` | 側邊欄連結實際生效的樣式（權重高於 `app.css`） | |
| `Components/Pages/*.razor.css` | 各頁 scoped CSS | |
| `appsettings.Example.json` | 設定範本（`appsettings.json` 被 gitignore） | 新增設定鍵時同步更新 |
| `tests/StockDashboard.Tests/` | xUnit：`TwseApiClient` 解析、報酬率計算、Repository（暫存 SQLite） | 改這些類別時補測試 |
| `tests/.../Fixtures/` | 證交所實際回應範例（測試與 `fake-twse.py` 共用） | 證交所格式改變時更新 |
| `tools/ui-snapshot.js` | UI 快照：擷取／比對 computed style 與截圖 | 新增頁面或互動狀態時加到 `STATES` |
| `tools/fake-twse.py`、`tools/make-test-db.py` | 假證交所伺服器、確定性測試資料庫 | |

未完成的空殼：`StockPriceService`（還原股價，未註冊 DI）、`StockDividend*`、`StockCorporateAction*`、`StockCombinationDto`。
主專案 `StockDashboard.csproj` 在 repo 根目錄，**已排除 `tests/**`、`tools/**`**（否則會把測試碼編進網站）；新增資料夾若不屬於網站也要排除。

## 修改後的驗證

```bash
dotnet test    # 每次修改後必跑（含建置網站與測試專案）；約 10 秒，首次需下載 NuGet 套件會更久
dotnet test --filter "FullyQualifiedName~TwseApiClientTests"   # 只跑部分測試
```
- **必須 0 error、0 warning、測試全過才 commit；不可為了通過而跳過或刪除測試。**
- 改 `TwseApiClient`、`StockPerformanceService`、Repository 時補對應測試。
- 動到 UI／CSS 時（每次擷取約 1 分鐘）：
  ```bash
  python3 tools/make-test-db.py /tmp/ui.db   # appsettings.json 的 DefaultConnection 指向它，啟動網站
  node tools/ui-snapshot.js capture /tmp/before --echarts <echarts.min.js>   # 修改前
  node tools/ui-snapshot.js capture /tmp/after  --echarts <echarts.min.js>   # 修改、重建、重啟後
  node tools/ui-snapshot.js diff /tmp/before /tmp/after   # 外觀不可改時必須 0 差異
  ```
- 本機啟動：`cp appsettings.Example.json appsettings.json`，改 `ConnectionStrings:DefaultConnection`，再 `dotnet run --launch-profile http`（http://localhost:8888）。

## 常見任務

- **新增頁面**：`Components/Pages/X.razor`（`@page`）＋ `X.razor.cs`（`[Inject]` 屬性注入）＋ `X.razor.css`；最外層 `<div class="dashboard-panel panel-standard">` 即套用共用樣式；`NavMenu.razor` 加連結。
- **新增查詢**：寫在 Repository（介面＋實作），頁面只呼叫 Repository／Service。
- **新增資料表**：加到 `DatabaseInitializer` 的 `CREATE TABLE IF NOT EXISTS`；**先確認使用者的 stock.db 沒有同名表**（該語法遇到同名表會直接略過，曾與使用者的 `StockList` 衝突）。
- **新增 Service／Repository**：`Program.cs` 註冊 `AddScoped`；呼叫外部 API 用 `AddHttpClient<介面, 實作>()`。
- **新增設定鍵**：同步加到 `appsettings.Example.json`。
- **新增 K 線型態**：`Services/Patterns/XxxPattern.cs` 實作 `ICandlePattern`（設定 `Direction`；共用條件用 `PatternFilters`，趨勢量到哪一根見 `PassesTrend` 註解；鏡像型態可共用基底類別，參考 `ThreeCandlesPattern`）→ `Program.cs` 註冊 `AddSingleton<ICandlePattern, XxxPattern>()` → 加到測試的 `PatternExampleTests.AllPatterns` 並寫判斷測試。頁面不用改。`BuildExample` 必須產生符合自己判斷的 K 線（測試會用每個參數的最小／最大值檢查）。
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
| 型態掃描頁的狀態放在 Scoped 的 `PatternScanState` | 使用者要求切換頁面後保留結果；Blazor 元件離開頁面即銷毀，Scoped 服務在同一連線內持續存在（F5 重新整理才重置） |

## 核心邏輯（修改前須先與使用者確認）

- 滾動報酬率：`(今日收盤 − N 個交易日前收盤) / N 日前收盤 × 100`，N 以所有股票「聯集」的時間軸計算；缺資料為 `null`（圖上斷線）。
- 台股配色：漲紅 `#ef5350`、跌綠 `#26a69a`。
- 型態掃描：小卡為型態成立日前 10 天、後 max(20, 最長觀察天數) 天；均線 MA5／10／20／60／120 用完整歷史計算，預設顯示 5／10／20／60（使用者指定）；之後報酬以型態成立日收盤為基準；結果新的日期在前；每頁筆數可選（預設 10）。
- 小卡附圖可選「成交量」（預設，使用者指定；含量 MA5／MA10）或「MACD」：MACD(12,26,9)，DIF − 訊號線 = OSC 柱狀（正紅負綠），EMA 以第一筆收盤為起始值。
- 勝率：看多型態之後第 N 日報酬 > 0 算勝、看空型態 < 0 算勝，報酬 0 算輸；之後天數不足的不計入分母。觀察天數固定 5／10／20，加上使用者輸入的「額外觀察天數」（可多組，如 `60,120`，1～250 天；以完整歷史計算，可超過小卡的後 20 天）。
- 勝率醒目提示：50% 起每 5 個百分點一級（共 5 級），用黃色系而非紅綠（紅綠在台股代表漲跌）。
- 看漲吞沒的預設參數由使用者確認過；晨星、紅三兵、黑三兵的預設參數是 Claude 依常見定義訂的，尚未經使用者確認。
- `TradeDate` 存 `yyyy-MM-dd` 字串，靠字串比較排序與篩選，**格式不可改**。

## 環境陷阱（雲端沙盒）

- 沒有預裝 .NET：`apt-get install -y dotnet-sdk-8.0`（約 1 分鐘）。
- 連不到 `twse.com.tw`、`openapi.twse.com.tw`、ECharts CDN：
  - 測 TWSE：`python3 tools/fake-twse.py`，`Twse:*` 設定指向它（用法見檔案開頭）；啟動網站要加 `NO_PROXY=127.0.0.1,localhost`，否則會走沙盒 proxy。
  - 測圖表：`curl -sSL https://registry.npmjs.org/echarts/-/echarts-5.4.3.tgz | tar xz -C /tmp package/dist/echarts.min.js`，再傳 `--echarts /tmp/package/dist/echarts.min.js`。
- **不要用 `pkill -f`／`pgrep -f` 關伺服器**：會比對到自己的 shell 指令而中斷 session。改用 `ps -eo pid,args | awk '/StockDashboard.dll/ && !/awk/ {print $1}' | xargs -r kill`。
- 測試用的 `appsettings.json` 用完要刪（雖被 gitignore，但會影響之後的啟動）。
- 使用者說「合併到 main」指的是 `master`（repo 沒有 `main` 分支）。

## Git 規則
- 每完成一個獨立的修改就 commit
- commit 訊息第一行簡述改了什麼，需要時空一行補充原因
- commit 訊息只描述本次修改，不要附上待辦清單
- 需要了解過去的修改時，用 git log 查詢
- commit 訊息使用英文

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
