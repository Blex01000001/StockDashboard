# CLAUDE.md

台股報酬可視化工具：.NET 8 Blazor Web App（**Interactive Server**，C# 全在伺服器端）＋ SQLite（Dapper，無 EF Core）＋ ECharts（CDN）。個人專案，使用者非資訊本科。**聊天回覆與程式註解用繁體中文；commit 訊息用英文。** 細節以程式碼與註解為準。

**開始新工作前先讀 `docs/TODO.md`**（待辦、已知問題、想法）。

## 檔案地圖

| 檔案 | 負責 | 常見修改 |
|---|---|---|
| `Program.cs` | DI 註冊、HttpClient 設定、啟動流程 | 新增 Service / Repository 註冊 |
| `Components/App.razor` | HTML 外殼、CSS/JS 載入 | 新增全域 CSS（要加 `?v=@AssetVersion.Value`） |
| `wwwroot/favicon.svg`、`favicon.png` | 瀏覽器分頁圖示 📈（SVG 為 emoji 文字，PNG 是同圖的 64×64 備援；`App.razor` 的 `<link rel="icon">` 都加 `?v=`） | |
| `Components/AssetVersion.cs` | 靜態檔版本號（MVID）；頁面 JS 模組用 `AssetVersion.Module(path)` 載入 | |
| `Components/Layout/NavMenu.razor` | 側邊欄連結（順序：API 資料更新、K線技術圖表、K線型態掃描、多檔績效比較） | 新增頁面時加連結 |
| `Components/Pages/StockChart.*` | `/stock-chart` 技術圖表：K 線＋均線＋附圖（同型態掃描），寬度隨容器（`ResizeObserver`），顯示筆數可選，可縮放；`?id=&date=` 開啟並標示型態日（型態掃描小卡「技術圖表」連結）；狀態在 Scoped `StockChartState` | 圖表設定改 `.razor.js` |
| `Components/Pages/StockCompare.*` | `/stock-compare` 多檔 N 日滾動報酬率 | 圖表設定改 `.razor.js` |
| `Components/Pages/PatternScan.*` | `/pattern-scan` 型態掃描：控制列（設定／參數／範例預覽）＋分頁小卡；參數輸入框依型態定義自動產生，勾選參數與其 `EnabledBy` 附屬數值自動框成一組（`PatternParameterLayout`） | 小卡與預覽圖改 `.razor.js` |
| `Components/Pages/StockUpdate.*` | `/stock-update` 依來源分框（`.source-group`）：證交所（日線逐月、三大法人逐日、股票清單）、FinMind（個股多年三大法人）；卡片標題下列 API 簡短網址 | 同步流程、錯誤訊息 |
| `Components/Shared/StockIdDataList.razor` | 股票代號自動完成 `<datalist>` | |
| `Components/Shared/ChartDisplayOptions.razor` | 附圖勾選、指標參數（`IndicatorGroups`）、均線勾選；型態掃描與技術圖表共用，改動後 `OnChanged(指標是否改變)`；`Framed`（技術圖表頁）= 單一設定面板（附圖／均線各一列；MACD、KD、RSI、布林通道的參數直接放在各自的勾選框旁，框成一組、勾選才亮起，輸入框窄、按鈕只寫「預設」，沒有獨立的指標參數區），否則（型態掃描頁）指標參數收折；勾選標記由 `SubChartToggles`／`MovingAverageToggles` 兩種版面共用 | 新增附圖／指標參數 |
| `wwwroot/js/candle-chart.js` | 共用繪圖：`SUB_CHARTS`（附圖定義）、`MA_LINES`、`buildStackedOption`（K 線＋附圖堆疊）；頁面 `.razor.js` 以 `await import(... + import.meta.url 的 ?v=)` 載入 | 附圖外觀、新增附圖 |
| `Services/TwseApiClient.cs` | 證交所 API 呼叫與資料清洗（民國年、千分號）；T86／MI_QFIIS 依欄位名稱取值 | 證交所格式改變時 |
| `Services/PriceSyncService.cs` | 個股日線逐月同步（由本月往前、連續 3 個月無資料中斷、請求間隔 `Twse:RequestDelayMs`、可取消、進度回報），最後一次寫入 `StockDailyPrice` | |
| `Services/InstitutionalSyncService.cs` | 三大法人逐日同步（T86 買賣超＋MI_QFIIS 外資持股），寫入 `InstitutionalDaily`、記錄 `InstitutionalSyncLog` | |
| `Services/FinMindApiClient.cs` | FinMind API（匿名、無 Token）：個股三大法人買賣超（每日多列合併）、外資持股；`ip banned`／402 丟 `FinMindBlockedException` | FinMind 格式改變時 |
| `Services/FinMindSyncService.cs` | FinMind 逐檔同步，寫入 `InstitutionalDaily`、記錄 `FinMindSyncLog` | |
| `Services/OilPriceApiClient.cs` | oilpriceapi.com：Brent `past_year`（每天一筆當日均價 `daily_average`）；API Key 來自 `OilPrice:ApiKey`（錯誤訊息不可含 Key） | oilpriceapi 格式改變時 |
| `Services/OilPriceSyncService.cs` | 原油同步：一次請求寫入 `OilPriceDaily`（同日覆蓋，舊資料不刪） | |
| `Repositories/Implements/OilPriceRepository.cs` | `OilPriceDaily` 讀寫；`OilPriceRepositoryExtensions.GetAllOrEmptyAsync` 失敗回傳空清單 | |
| `Services/MarketIndexApiClient.cs` | Yahoo Finance chart API（非官方、免金鑰）：美股指數日線 OHLC（目前只有費半 `^SOX`）；必須用 `period1/period2`（`range=max` 會降成月線）；日期取時間戳 UTC 日期，價格四捨五入到小數 2 位 | Yahoo 格式改變時 |
| `Services/MarketIndexSyncService.cs` | 費半同步：一次請求最近 365 天寫入 `MarketIndexDaily`（同日覆蓋，舊資料不刪） | |
| `Repositories/Implements/MarketIndexRepository.cs` | `MarketIndexDaily` 讀寫；`MarketIndexRepositoryExtensions.GetAllOrEmptyAsync` 失敗回傳空清單 | |
| `Services/LocalPathOpener.cs` | 在執行網站的電腦上開啟 `appsettings.json`（Windows 用記事本、Mac `open -t`、Linux `xdg-open`）或其資料夾；只開固定路徑，不接受使用者輸入（原油卡片的按鈕用） | |
| `Services/StockPerformanceService.cs` | 滾動報酬率計算 | 報酬率公式（**先問使用者**） |
| `Services/Patterns/ICandlePattern.cs` | 型態介面：參數定義、判斷、範例 K 線 | |
| `Services/Patterns/*Pattern.cs` | 各型態（看漲吞沒、晨星、紅三兵／黑三兵）的方向、參數、預設值、判斷、範例 | 判斷條件與預設值（**先問使用者**） |
| `Services/Patterns/PatternFilters.cs` | 共用條件（先下跌／先上漲、量增）與範例 K 線輔助 | |
| `Services/Patterns/TechnicalIndicators.cs` | 技術指標（SMA、MACD、KD、RSI）純計算 | 新增指標時（補測試） |
| `Services/PatternScanService.cs` | 掃描：讀完整日線 → 判斷 → 切小卡範圍、均線、三大法人（`ApplyInstitutional`）、之後報酬、勝率 | |
| `Services/PatternScanState.cs` | 型態掃描頁的畫面狀態（Scoped）；附圖／均線／指標設定在 `Display`（`ChartDisplaySettings`） | 新增要保留的輸入時 |
| `Services/ChartDisplaySettings.cs` | 附圖選項、顯示順序（`SubChartOrder`，預設 = `SubChartOptions` 順序，技術圖表頁 `MoveSubChart` 拖曳重排）、均線選項與預設、指標參數 | 新增附圖時加到 `SubChartOptions` |
| `Services/StockChartService.cs` | 技術圖表資料：完整日線算指標，取最近 N 根（`SetWindow`，0 = 全部），填三大法人；`CountToInclude` 確保標示日在範圍內 | |
| `Repositories/Implements/StockPriceRepository.cs` | `StockDailyPrice` 唯一讀寫入口 | 新查詢 |
| `Repositories/Implements/StockNameRepository.cs` | 股票名稱（合併 `StockList` ＋ `StockNameList`） | |
| `Repositories/Implements/InstitutionalRepository.cs` | `InstitutionalDaily`／`InstitutionalSyncLog`／`FinMindSyncLog` 讀寫 | |
| `Repositories/StockNameRepositoryExtensions.cs` | 顯示用名稱查詢，失敗回傳 null／空字典 | |
| `Repositories/DatabaseInitializer.cs` | 啟動時 `CREATE TABLE IF NOT EXISTS`（Schema 在此） | 新增資料表 |
| `wwwroot/app.css` | 全域：色彩變數、版面、側邊欄、共用面板 `.panel-standard` | |
| `Components/Layout/NavMenu.razor.css` | 側邊欄連結實際生效的樣式（權重高於 `app.css`） | |
| `Components/Pages/*.razor.css` | 各頁 scoped CSS | |
| `docs/TODO.md` | 目前狀態：待辦（有編號與標記）、已知問題／限制、想法；**每次 commit 時同步更新** | 每次 commit |
| `appsettings.Example.json` | 設定範本（`appsettings.json` 被 gitignore） | 新增設定鍵時同步更新 |
| `tests/StockDashboard.Tests/` | xUnit：`TwseApiClient` 解析、報酬率計算、Repository（暫存 SQLite） | 改這些類別時補測試 |
| `tests/.../Fixtures/` | 證交所實際回應範例（測試與 `fake-twse.py` 共用） | 證交所格式改變時更新 |
| `tools/ui-snapshot.js` | UI 快照：擷取／比對 computed style 與截圖 | 新增頁面或互動狀態時加到 `STATES` |
| `tools/fake-twse.py`、`tools/make-test-db.py` | 假證交所（含 FinMind 端點）伺服器、確定性測試資料庫 | |

還原股價（型態掃描、技術圖表有「還原股價」開關，預設原始價；`PatternScanService.ScanAsync`／`StockChartService.LoadAsync` 的 `adjusted` 參數用 `PriceAdjuster.ApplyToCandles` 還原完整日線後才判斷／算指標；小卡連結帶 `&adj=1`；`/stock-compare` 尚無）：`Services/PriceAdjuster.cs`（比例還原純計算：係數 = 參考價 / 前收盤，除權息日之前的 OHLC 與漲跌價差連乘）、`StockPriceService`（`isAdjusted` 才套用）、`StockExRightEvent` 表（**不是 `StockCorporateAction`：使用者的 stock.db 已有同名但欄位不同的表**）與 `StockCorporateActionRepository`、`StockCorporateActionSyncService`（逐檔、封鎖即停）＋`FinMindCorporateActionApiClient`（FinMind `TaiwanStockDividendResult`：`before_price` → PreClose、`reference_price`（無則 `after_price`）→ RefPrice，2003 年起整段一次取回；`/stock-update` 有同步卡片）。
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
- **證交所／FinMind API 改版**：先改 `appsettings.json` 的 `Twse:*Url`／`FinMind:DataUrl`；格式變了才改 `TwseApiClient`／`FinMindApiClient`（並更新 Fixtures）。

## 硬性限制

- **頁面不可寫 SQL 或 `new SqliteConnection`**；一律經 Repository。
- **連線字串只來自 `ConnectionStrings:DefaultConnection`**，不可寫死路徑（舊版曾寫死 `E:\...`）。
- **`StockList` 是使用者既有的「上市公司基本資料」表（中文欄位、資料有在用）：只讀，不可寫入、建立、修改、刪除。**
- **股票名稱只是附加顯示**：一律用 `GetStockNameOrNullAsync`／`GetStockNamesOrEmptyAsync`，查名稱失敗不可讓主要功能失敗。
- **版面樣式暫不更動**（顏色、字型、padding、margin、border）。外觀變更需使用者同意。

## 架構決策（不要改回去）

| 做法 | 原因 |
|---|---|
| `App.razor` 的 CSS 網址、頁面 JS 模組都加 `?v=<組件 MVID>`（`AssetVersion`）；共用 JS 由模組沿用自己的 `?v=` 動態 import | .NET 8 無靜態檔指紋，瀏覽器會沿用舊 CSS／JS 造成版面全亂（使用者實際遇過） |
| `Program.cs` 呼叫 `UseStaticWebAssets()` | 使用者可能直接執行 `bin/` 下的 exe（Production 環境），沒有它 CSS 全部 404 |
| 頁面樣式用 scoped `.razor.css`，不用 `<style>` | `<style>` 是全域且只在當頁存在，曾導致 StockUpdate 完全沒樣式 |
| scoped CSS 不用 `:root`，頁面變數定義在最外層元素 | scoped 會變成 `:root[b-xxx]` 而失效 |
| StockCompare 不加 `panel-standard` | 它是另一套設計；加了會混入共用樣式 |
| 名稱分兩表：讀 `StockList`，同步寫 `StockNameList` | `StockList` 不含 ETF 且不可寫；同代號以 `StockList` 簡稱為準 |
| `StockList` 欄位用 `pragma_table_info` 動態比對（忽略空白） | 使用者表的欄位名稱可能含空白 |
| `StockDailyPrice` 用 `INSERT OR REPLACE`；`StockNameList` 用 `ON CONFLICT DO UPDATE` | 後者避免日後加欄位被整列覆寫成 NULL |
| 批次寫入包在單一交易 | SQLite 逐筆交易極慢；失敗整批還原 |
| TWSE 請求間隔 `Twse:RequestDelayMs`（預設 2.5 秒）；逐月同步連續 3 個月無資料中斷（本月不計） | 證交所會封鎖頻繁請求；月初本月本來就沒資料 |
| 三大法人 T86 與 MI_QFIIS 寫入同一列，各自 `ON CONFLICT` 只更新自己的欄位 | 兩個來源分開請求，先後順序不定；互不覆蓋 |
| 三大法人同步記錄每天每來源查過與否（`InstitutionalSyncLog`）；無資料的日期若是「之後」才查的就不再查 | 重跑時只補缺的；當天查無資料可能只是尚未公布 |
| 三大法人分工：證交所只補最近（一週／一個月，逐日全市場）；多年資料用 FinMind（逐檔，一次整段）。兩者寫同一張 `InstitutionalDaily` | 證交所一天一請求太慢；FinMind 定義相同，附圖不必分來源 |
| FinMind 第一次從 2012-05-02 抓，之後從 `FinMindSyncLog` 的上次同步日往前 7 天補；可勾「重新完整下載」 | 請求次數相同但資料量差很多；重疊幾天補上當時未公布或修正的資料 |
| FinMind 封鎖（`ip banned`／403）或達上限（402）立即停止、不重試 | FinMind 封鎖政策嚴格（使用者要求） |
| TWSE HttpClient 帶瀏覽器 User-Agent、自行解析回應 | 被封鎖時常回 HTML／403，要顯示看得懂的原因 |
| 數字解析用 `InvariantCulture` | 避免電腦地區設定影響 |
| 自動開瀏覽器包 try/catch | 無桌面環境會丟例外導致程式崩潰 |
| 原油附圖：每個交易日取「當天之前」最近一筆價格（`PatternScanService.ApplyOil`，嚴格早於當天、休市沿用前值） | 來源是 UTC 整天均價，涵蓋台股收盤之後，用當天值等於偷看未來 |
| 原油來源用 oilpriceapi.com `past_year`（只有一年、是均價），不用 FRED／FinMind | FinMind 原油要收費；FRED 是現貨，與 TradingView UKOIL（期貨）差最多近 20 美元；oilpriceapi 實測與 UKOIL 收盤平均差約 1.8 美元 |
| 費半日期對齊（`PatternScanService.ApplyMarketIndex`）：美股交易日 D 歸屬到「該股完整日線中第一個日期 > D」的台股日；同一個台股日有多個美股交易日（台股連假）合併成一根（開取最早、收取最後、高取最大、低取最小）；美股休市日留空、不重複顯示 | 美股在台灣時間隔天清晨收盤，台股開盤前看到的是之前已收盤的美股；合併才不漏掉連假期間的行情；留空才不會把同一根畫兩次 |
| 費半均線在 `ApplyMarketIndex` 內用完整美股序列算，再依美股日期對應到台股日 | 對齊後的序列有休市留空與連假合併，在上面算 MA5 會變成「5 個台股日」而不是 5 個美股交易日 |
| 費半來源用 Yahoo `^SOX` 區間查詢（`period1/period2&interval=1d`） | 使用者只要一年；`range=max` 會被降成月線；非官方 API，壞了只需改 `MarketIndexApiClient.ParseResponse` |
| 附圖列高度可個別指定（`SUB_CHARTS[x].heightScale`，費半 = 2），整張圖高度用 `stackedHeight(layout, subs)` 計算 | 費半是蠟燭，要比一般附圖高才看得清楚（使用者指定只加高這一列）；沒勾費半時高度與原本相同 |
| 型態掃描頁的狀態放在 Scoped 的 `PatternScanState` | 使用者要求切換頁面後保留結果；Blazor 元件離開頁面即銷毀，Scoped 服務在同一連線內持續存在（F5 重新整理才重置） |

## 核心邏輯（修改前須先與使用者確認）

- 滾動報酬率：`(今日收盤 − N 個交易日前收盤) / N 日前收盤 × 100`，N 以所有股票「聯集」的時間軸計算；缺資料為 `null`（圖上斷線）。
- 台股配色：漲紅 `#ef5350`、跌綠 `#26a69a`。
- 型態掃描：小卡為型態成立日前 10 天、後 max(20, 最長觀察天數) 天；均線 MA5／10／20／60／120 用完整歷史計算，預設顯示 5／10／20／60（使用者指定）；之後報酬以型態成立日收盤為基準；結果新的日期在前；每頁筆數可選（預設 10）。
- 小卡附圖可複選、預設依 成交量→MACD→KD→RSI→三大法人→外資→投信→自營商→原油→費半 疊在 K 線下方，**型態掃描頁的勾選列不可拖曳排序（使用者要求取消，順序固定為預設）；技術圖表頁的勾選列也不可拖，改拖圖上附圖左側的 ≡ 把手，見下（兩頁各自一份、重新整理後回到預設、有「恢復預設順序」按鈕；取消勾選再勾回來維持原位）**；**K 線區高度固定 150px**，每多一個附圖小卡增高一列（版面數值在 `.razor.js` 的 `LAYOUT`）。預設只有成交量（使用者指定；含量 MA5／MA10）。
- 技術圖表頁與型態掃描用同一套附圖、均線、指標（使用者要求）：K 線區固定 340px、每個附圖 103px（27px 標題區〔含上方 6px 間隔、6px 上內距、15px 資訊文字〕＋ 70px 繪圖區 ＋ 6px 下內距），寬度隨視窗；附圖與附圖、K 線區與第一個附圖的間隔都是 6px（`LAYOUT.subGap`，淡底色帶之間的空隙）；色帶上下各有 6px 內距（`subPad`）、圓角 8px（`subRadius`），技術圖表的色帶是圓角 HTML 方塊（`StockChart.razor.js` 的 `renderSubBands`，放在透明的圖表後面），型態掃描小卡仍是 3px 間隔的直角漸層；這些版面數字都在 `StockChart.razor.js` 的 `LAYOUT`；均線與指標用完整歷史計算；滑鼠所在日的數值顯示在各圖上方（`infoTexts`：K 線區放開高低收＋已勾選均線與日期，附圖各放自己的數值；無提示框、圖例隱藏；預設顯示最新日或標示日；**每個欄位固定寬度**——`infoWidths` 用 canvas 量整份資料裡該欄位最長文字的寬度，所以「開 2310」與「開 2310.13」一樣寬、後面的欄位不會跳動，附圖標題各列共用同一寬度）；顯示筆數 半年 120／一年 250（預設）／三年 750／全部；預設縮放顯示最近約 60 根；換附圖時保留目前縮放範圍。圖表標題置中；每個附圖上方資訊列開頭有粗體標題（`SUB_CHARTS[x].title`），**拖曳每個附圖左側（Y 軸刻度左邊、垂直置中）的 ≡ 把手可調整附圖順序**（使用者要求；只有把手可拖，圖表本身的拖曳平移不受影響；`StockChart.razor.js` 的 `bindSubDrag` 在左側放 ≡ 把手、拖曳時才蓋放開區並畫藍色插入線，放開後以 `DotNetObjectReference` 回呼 `OnSubChartMoved` → `MoveSubChart`，勾選列順序一併更新；HTML5 拖放，不支援觸控；型態掃描小卡沒有），每個附圖左側都有 Y 軸（`buildStackedOption` 的 `subAxes`，只有技術圖表頁開；約 3 個刻度、大數字用萬／億縮寫，KD／RSI 固定 0／50／100；外資持股％的軸在右側、原油的軸在左側；型態掃描小卡不顯示）。從小卡開啟時標示型態日（黃色虛線）、縮放到該日前 30 後 60 根，該日不在範圍內時自動改用夠大的筆數（多留前 30 根）。
- 附圖列各自有淡底色（使用者要求；`candle-chart.js` 的 `stackedBackground`，用容器 CSS 背景畫在透明的圖表後面，每列頂端留 3px 不上色以區隔相鄰附圖）；K 線與附圖上方的資訊文字 13px、小卡圖例 12px（使用者要求比原本大 2px）。
- 指標預設：MACD 12,26,9（DIF − 訊號線 = OSC 柱狀，正紅負綠，技術圖表資訊列的 OSC 數字也依正負上色；EMA 以第一筆收盤為起始值）、KD 9,3,3（K/D 起始 50，參考線 80/20）、RSI 6 與 12（Wilder 平滑，參考線 70/30）。三者參數都可在頁面調整（定義在 `ChartDisplayOptions.razor` 的 `IndicatorGroups`，新增指標時加一組）。
- 指標用 `PatternScanService.ApplyIndicators` 以完整日線計算；掃描結果保留各股完整日線（`FullCandles`），調整指標參數時 `RecomputeIndicators` 即時重算，不重新查資料庫。
- 勝率：看多型態之後第 N 日報酬 > 0 算勝、看空型態 < 0 算勝，報酬 0 算輸；之後天數不足的不計入分母。觀察天數固定 5／10／20，加上使用者輸入的「額外觀察天數」（可多組，如 `60,120`，1～250 天；以完整歷史計算，可超過小卡的後 20 天）。
- 三大法人附圖（使用者指定）：單位「張」（股 ÷ 1000），正紅負綠（技術圖表上方資訊列的買賣超數字也依正負上色，0 或沒有資料用一般顏色）。「三大法人」只畫合計買賣超柱狀；「外資」= 外陸資（不含外資自營商）＋ 外資自營商，另畫「全體外資及陸資持股比率」線（自己的 Y 軸）；投信、自營商（自行買賣＋避險）只畫買賣超。整張小卡無資料時顯示提示。證交所同步範圍：最近一週（預設）／最近一個月（使用者指定）。FinMind 分類換算：外資 = `Foreign_Investor`、外資自營商 = `Foreign_Dealer_Self`（2017-12-18 前無此列，留 null）、投信 = `Investment_Trust`、自營商 = `Dealer` + `Dealer_self` + `Dealer_Hedging`（2014-12-01 前後擇一有值）；持股比例 = `TaiwanStockShareholding.ForeignInvestmentSharesRatio`。
- 布林通道（使用者要求）：畫在 K 線上，只填上下軌之間的淡灰藍底色（中軌 = MA20 不重畫）；勾選在「顯示均線」那排，參數（天數 20、標準差倍數 2，母體標準差）技術圖表頁放在勾選框旁（型態掃描頁為收折區）；預設關閉，關閉時不計算；設定放在 `IndicatorSettings.Bollinger`，兩頁共用。
- 原油附圖（使用者要求）：Brent 當日均價（美元 / 桶，不是收盤價）一條橘線、自己的 Y 軸，排在附圖最後、預設不勾；`/stock-update` 的 oilpriceapi 卡片同步最近一年（免費每天 50 次），更早的資料靠多次同步累積。
- 費半附圖（使用者要求）：美國費城半導體指數 SOX 的紅綠蠟燭（漲紅跌綠），自己的 Y 軸並顯示價格刻度，排在附圖最後、預設不勾；**這一列比其他附圖高 2 倍**（技術圖表附圖區 70 → 140px、小卡 48 → 96px，其他附圖高度不變）。資訊列／提示框顯示「開高低收＋美股日期」。**費半也畫均線（使用者要求）：週期與開關和個股共用「顯示均線」（5／10／20／60／120，勾選時個股與費半同時畫；同名 series 由同一個圖例控制）；均線用「美股自己的交易日」算（MA5 = 最近 5 個美股交易日，不是對齊後的台股日），對應到台股日時取合併後最後一個美股交易日的值；來源只有一年，所以 MA60／MA120 在資料最前面幾個月是空的。**`/stock-update` 的 Yahoo Finance 卡片同步最近一年，更早的資料靠多次同步累積。
- 勝率醒目提示：50% 起每 5 個百分點一級（共 5 級），用黃色系而非紅綠（紅綠在台股代表漲跌）。
- 看漲吞沒、晨星、紅三兵、黑三兵的預設參數都由使用者確認過（修改預設前先問使用者）。
- `TradeDate` 存 `yyyy-MM-dd` 字串，靠字串比較排序與篩選，**格式不可改**。

## 環境陷阱（雲端沙盒）

- 沒有預裝 .NET：`apt-get install -y dotnet-sdk-8.0`（約 1 分鐘）。
- 連不到 `twse.com.tw`、`openapi.twse.com.tw`、`api.finmindtrade.com`、ECharts CDN（使用者可在環境 Network access 加 Allowed domains）：
  - 測 TWSE／FinMind：`python3 tools/fake-twse.py`，`Twse:*`、`FinMind:DataUrl` 設定指向它（用法見檔案開頭）；啟動網站要加 `NO_PROXY=127.0.0.1,localhost`，否則會走沙盒 proxy。
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

## TODO 維護規則
（原 `PROGRESS.md` 已併入 `docs/TODO.md`）
- 每次 commit 時一併更新 `docs/TODO.md`：做完的刪掉、新發現的加進去、不適用的刪掉或改寫成新的形式
- 只記現況、不寫完成紀錄（不要搬到「已完成」；歷史看 `git log`）；每項以一行為主，保持精簡
- 限制或問題解除時直接刪除
- 使用者說「做待辦 N」時，以 `docs/TODO.md` 的編號為準；整理時可以重新編號
- 「完成任務後的回覆」中的尚待改善／已知問題、建議改善方向，要和 `docs/TODO.md` 的內容一致

## 完成任務後的回覆
簡短列出：
1. **本次修改**：做了什麼
2. *尚待改善項目／問題、已知問題**：還沒做完的、發現但未處理的問題、已知限制（沒有就省略）
3. **建議改善方向或功能**：之後可以考慮的改進或新功能，或是可以提高CLAUDE CODE效率的建議（沒有就省略）
