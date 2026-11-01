# CLAUDE.md

這份文件給 Claude Code（以及未來的自己）快速了解本專案用。**回答與註解一律使用繁體中文。**

## 專案簡介

台灣股票報酬可視化工具（個人專案）。從證交所 (TWSE) 公開 API 抓取個股日線資料存入本機 SQLite，再以 ECharts 繪製 K 線圖與多檔股票滾動報酬率比較圖。

- 技術棧：.NET 8 / Blazor Web App（**Interactive Server** 模式，所有 C# 都跑在伺服器端，透過 SignalR 更新畫面）
- 資料庫：SQLite + Dapper（無 EF Core）
- 圖表：ECharts 5.4.3（CDN 全域載入，於 `Components/App.razor`）

## 啟動方式

```bash
# 1. 準備設定檔（appsettings.json 被 .gitignore 排除，不會進版控）
cp appsettings.Example.json appsettings.json
# 2. 修改 DefaultConnection 指向你的 stock.db
dotnet run --launch-profile http   # 監聽 http://localhost:8888
```

- 啟動後會自動以預設瀏覽器開啟 `http://localhost:8888/stock-query`（可用 `StartupUrl` 設定覆寫；無桌面環境時只記錄警告不會崩潰）。
- `*.db` 也被 `.gitignore` 排除。啟動時 `DatabaseInitializer` 會以 `CREATE TABLE IF NOT EXISTS` 自動建立缺少的資料表（不會動到既有資料），並在 log 印出實際連到的資料庫檔案路徑。
- 驗證修改：`dotnet build` 必須 0 error（目前也是 0 warning，請維持）。

## 目錄結構與職責

```
Program.cs                  DI 註冊、HTTP 管線、啟動後自動開瀏覽器
Components/
  App.razor                 HTML 外殼；載入 bootstrap、app.css、scoped CSS 打包檔、ECharts CDN
  Routes.razor              路由；預設 Layout = MainLayout
  Layout/
    MainLayout.razor        整體版面（側邊欄 + 主內容區）
    NavMenu.razor           可收合側邊欄（4 個頁面連結）
  Pages/                    每頁 = .razor (標記) + .razor.cs (程式) + .razor.css (樣式) + .razor.js (ECharts 繪圖，可選)
    StockQuery              /stock-query   查詢單檔股票在 DB 中最新一筆日線 + 股票名稱
    StockChart              /stock-chart   K 線圖（最近 220 個交易日）
    StockUpdate             /stock-update  從 TWSE API 逐月同步日線 / 手動新增單筆 / 同步股票清單
    StockCompare            /stock-compare 多檔股票 N 日滾動報酬率比較
    Error                   /Error         非開發環境的未處理例外頁面
  Shared/
    StockIdDataList         股票代號自動完成清單 (<datalist>)，搭配 <input list="..."> 使用
Services/                   商業邏輯 / 外部 API
  StockPerformanceService   計算多檔滾動報酬率 → CompareChartResult
  TwseApiClient             TWSE API：STOCK_DAY (個股月日線，清洗民國年/千分號) 與 OpenAPI STOCK_DAY_ALL (上市股票清單)
  StockPriceService         ⚠️ 未完成：還原股價（除權息調整），尚未註冊到 DI
Repositories/
  Interfaces/ + Implements/ StockPriceRepository (日線)、StockListRepository (股票清單) 可用；
                            StockDividend / StockCorporateAction 只有介面與空類別（規劃中）
  DatabaseInitializer       啟動時建立缺少的資料表
Models/
  Entities/                 對應資料表的實體（StockDailyPrice、StockList 完整；其餘為空殼）
  StockDataPoint            K 線圖資料點
  StockSeries / CompareChartResult  比較圖的輸出 DTO
  TwseStockDayResponse / TwseStockDayAllItem  TWSE API 回應格式
  StockCombinationDto       ⚠️ 目前沒有被使用（規劃中的「股票組合」功能）
```

## 分層原則（重要）

```
Razor 頁面 (.razor.cs)  →  Service  →  Repository  →  SQLite
                         ↘  TwseApiClient → 證交所 API
```

- **頁面不可以直接寫 SQL 或 new SqliteConnection**，一律透過 `IStockPriceRepository`。
- **連線字串只有一個來源**：`appsettings.json` 的 `ConnectionStrings:DefaultConnection`（由 Repository 讀取）。不要再把路徑寫死在程式裡。
- 新增 Service / Repository 時記得到 `Program.cs` 註冊（目前都是 `AddScoped`；HttpClient 用 `AddHttpClient<介面, 實作>()`）。
- 頁面用 `[Inject]` 屬性注入（寫在 .razor.cs），不在 .razor 用 `@inject`。

## 資料流

1. **資料同步（StockUpdate）**
   使用者選擇月份數 → 由本月往回逐月呼叫 `TwseApiClient.GetMonthlyPricesAsync`（每次間隔 2.5 秒防封鎖；連續 3 個月無資料即中斷）→ 全部收集完 → `StockPriceRepository.UpsertPricesAsync`（`INSERT OR REPLACE`，包在單一交易中）。
2. **K 線圖（StockChart）**
   `GetLatestCandlesAsync(stockId, 220)` → `List<StockDataPoint>` → JS interop 呼叫 `StockChart.razor.js` 的 `renderStockChart`（C# 屬性會自動轉成 camelCase）。
3. **多檔比較（StockCompare）**
   `StockPerformanceService.GetCompareChartDataAsync` → Repository 撈 660 天內所有指定股票 → 建立所有股票「聯集」的日期時間軸 → 每檔計算 `(今日收盤 − N 個交易日前收盤) / N 日前收盤 × 100`，缺資料的點為 `null` → `CompareChartResult` → `StockCompare.razor.js` 畫多線圖。
4. **股價查詢（StockQuery）**
   `GetLatestPriceAsync(stockId)` → 顯示最新收盤價、成交股數、最新交易日；名稱來自 `StockListRepository`。
5. **股票清單（StockUpdate 第三張卡片）**
   `TwseApiClient.GetListedStocksAsync`（證交所 OpenAPI STOCK_DAY_ALL，含 ETF）→ `StockListRepository.UpsertStocksAsync`（`ON CONFLICT DO UPDATE`）。
   使用處：查詢頁的商品名稱、K 線圖標題「代號 名稱」、多檔比較的圖例、`StockIdDataList` 自動完成。清單中沒有的股票一律退回只顯示代號。

## 資料庫 Schema（定義於 `Repositories/DatabaseInitializer.cs`）

```sql
CREATE TABLE StockDailyPrice (
    StockId     TEXT NOT NULL,
    TradeDate   TEXT NOT NULL,      -- 'yyyy-MM-dd'，以字串比較大小，格式必須一致
    Volume      INTEGER,            -- 成交股數
    Amount      INTEGER,            -- 成交金額
    OpenPrice   REAL,
    HighPrice   REAL,
    LowPrice    REAL,
    ClosePrice  REAL,
    PriceChange REAL,               -- 漲跌價差
    TradeCount  INTEGER,            -- 成交筆數
    Note        TEXT,
    PRIMARY KEY (StockId, TradeDate)
);

CREATE TABLE StockList (
    StockId   TEXT NOT NULL PRIMARY KEY,
    StockName TEXT NOT NULL,
    Market    TEXT NOT NULL DEFAULT 'TWSE',  -- TWSE = 上市；預留 TPEx = 上櫃
    UpdatedAt TEXT NOT NULL                  -- yyyy-MM-dd HH:mm:ss
);
```

新增資料表時，請同步加到 `DatabaseInitializer` 的 `CREATE TABLE IF NOT EXISTS`。

## 樣式（CSS）

- 使用者要求：**版面樣式暫不更動**（顏色、字型、padding、margin、border 等）。重構 CSS 時必須確認畫面完全一致。
- 樣式檔分工：
  | 檔案 | 作用範圍 | 內容 |
  |---|---|---|
  | `wwwroot/app.css` | 全域 | `:root` 顏色變數、`body`、版面與側邊欄、共用面板 `.panel-standard` |
  | `Components/Layout/NavMenu.razor.css` | NavMenu (scoped) | 側邊欄連結的高度、圓角、hover / active 底色。權重高於 app.css 的 `.nav-link`，**實際生效的是這裡** |
  | `Components/Pages/*.razor.css` | 各頁 (scoped) | 每頁自己的面板、輸入框、按鈕、表格樣式 |
- Scoped CSS 會自動在選擇器後加上 `[b-xxxx]` 屬性，只作用於該元件自己的標記；要選到子元件（例如 `NavLink` 產生的 `<a>`）必須用 `::deep`。
- Scoped CSS **不能用 `:root`**（會變成 `:root[b-xxxx]` 而失效）。頁面專屬的 CSS 變數請定義在該頁最外層元素上（參考 `StockCompare.razor.css` 的 `.dashboard-panel`）。
- **共用面板樣式**：StockQuery / StockChart / StockUpdate 最外層是 `<div class="dashboard-panel panel-standard">`，共用 `app.css` 中 `.panel-standard ...` 開頭的規則（面板、標題、搜尋列、`.dark-input`、`.action-btn`、`.alert-box`）。新頁面若要同樣外觀，加上 `panel-standard` 即可。
- StockCompare **不使用** `panel-standard`，它的 `.dashboard-panel`、`.dark-input` 等是另一套設計，全部寫在 `StockCompare.razor.css`。
- 頁面若需微調共用樣式，寫在該頁 `.razor.css`（scoped 權重較高會覆蓋），例如 StockUpdate 的輸入框不需要放大鏡的左側 padding。
- Scoped CSS 會打包成 `StockDashboard.styles.css`（`App.razor` 已引用）。`Program.cs` 有呼叫 `UseStaticWebAssets()`，所以 `dotnet run`、Visual Studio、直接執行 `bin/` 下的 exe、`dotnet publish` 都能正確載入。
- `App.razor` 引用 `app.css` 與 `StockDashboard.styles.css` 時會加上 `?v=<組件版本>`，每次編譯都會變，避免瀏覽器沿用快取中的舊 CSS（.NET 8 沒有內建靜態檔指紋）。新增全域 CSS 檔時請比照辦理。

### 修改樣式後的驗證方式
用 Playwright 擷取各頁各狀態（初始、查詢結果、錯誤訊息、focus、hover、側邊欄收合）所有元素的 `getComputedStyle` 與截圖，與修改前比對。

## 已知問題 / 未完成

- `StockPriceService`（還原股價）`ApplyDividendAdjustment` 尚未實作，依賴的 `IStockDividendRepository` 也沒有實作類別。
- `StockDividend` / `StockCorporateAction` 的 Entity 與 Repository 是空殼。
- 股票清單只有上市 (TWSE)，尚未包含上櫃 (TPEx)；已下市的股票不會自動從清單移除。
- 滾動報酬率以「所有股票聯集後的時間軸」位移 N 格計算，若比較的股票交易日不同（例如停牌），N 日的意義會略有偏差。
- 沒有自動化測試專案。
- ECharts 走 CDN，離線時圖表無法顯示。
