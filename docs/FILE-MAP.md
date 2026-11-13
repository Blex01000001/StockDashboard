# 檔案地圖

各檔案負責什麼、常見修改在哪裡。找不到要改哪個檔時才讀（維護規則見 `CLAUDE.md`）。

| 檔案 | 負責 | 常見修改 |
|---|---|---|
| `Program.cs` | DI 註冊、HttpClient 設定、啟動流程 | 新增 Service / Repository 註冊 |
| `Components/App.razor` | HTML 外殼、CSS/JS 載入 | 新增全域 CSS（要加 `?v=@AssetVersion.Value`） |
| `wwwroot/favicon.svg`、`favicon.png` | 瀏覽器分頁圖示 📈（SVG 為 emoji 文字，PNG 是同圖的 64×64 備援；`App.razor` 的 `<link rel="icon">` 都加 `?v=`） | |
| `Components/AssetVersion.cs` | 靜態檔版本號（MVID）；頁面 JS 模組用 `AssetVersion.Module(path)` 載入 | |
| `Components/Layout/NavMenu.razor` | 側邊欄：品牌標誌（SVG）、分組標題（資料／分析）、線條 SVG 圖示、底部「本機模式」狀態；連結順序：API 資料更新、K線技術圖表、K線型態掃描、多檔績效比較；可收合（收合時標誌隱藏，只留圖示與切換按鈕） | 新增頁面時加連結（含 SVG 圖示） |
| `Components/Pages/StockChart.*` | `/stock-chart` 技術圖表：K 線＋均線＋附圖（同型態掃描），寬度隨容器（`ResizeObserver`），顯示筆數可選，可縮放；`?id=&date=` 開啟並標示型態日（型態掃描小卡「技術圖表」連結）；狀態在 Scoped `StockChartState` | 圖表設定改 `.razor.js` |
| `Components/Pages/StockCompare.*` | `/stock-compare` 多檔 N 日滾動報酬率；「更新並比較」先用 `PriceSyncService.SyncManyAsync` 更新輸入代號近三個月日線再比較（更新失敗仍用現有資料比較並提示）；「設為預設」把股票代號存在瀏覽器 localStorage（`stockCompare.defaultIds`，只在這個瀏覽器有效），開頁自動帶入 | 圖表設定改 `.razor.js` |
| `Components/Pages/PatternScan.*` | `/pattern-scan` 型態掃描：控制列（設定／參數／範例預覽）＋分頁小卡；參數輸入框依型態定義自動產生，勾選參數與其 `EnabledBy` 附屬數值自動框成一組（`PatternParameterLayout`）；「產業別」下拉選了會把該產業全部代號帶入股票清單（`IIndustryRepository`） | 小卡與預覽圖改 `.razor.js` |
| `Components/Pages/StockUpdate.*` | `/stock-update` 依來源分框（`.source-group`）：證交所（日線逐月、三大法人逐日、股票清單）、FinMind（個股多年三大法人）；卡片標題下列 API 簡短網址 | 同步流程、錯誤訊息 |
| `Components/Shared/StockIdDataList.razor` | 股票代號自動完成 `<datalist>` | |
| `Components/Shared/ChartDisplayOptions.razor` | 附圖勾選、指標參數（`IndicatorGroups`）、均線勾選；型態掃描與技術圖表共用，改動後 `OnChanged(指標是否改變)`；`Framed`（技術圖表頁）= 單一設定面板（附圖／均線各一列；MACD、KD、RSI、布林通道的參數直接放在各自的勾選框旁，框成一組、勾選才亮起，輸入框窄、按鈕只寫「預設」，沒有獨立的指標參數區），否則（型態掃描頁）指標參數收折；勾選標記由 `SubChartToggles`／`MovingAverageToggles` 兩種版面共用 | 新增附圖／指標參數 |
| `wwwroot/js/candle-chart.js` | 共用繪圖：`SUB_CHARTS`（附圖定義）、`MA_LINES`、`buildStackedOption`（K 線＋附圖堆疊）；頁面 `.razor.js` 以 `await import(... + import.meta.url 的 ?v=)` 載入 | 附圖外觀、新增附圖 |
| `Services/TwseApiClient.cs` | 證交所 API 呼叫與資料清洗（民國年、千分號）；T86／MI_QFIIS 依欄位名稱取值 | 證交所格式改變時 |
| `Services/StockIdParser.cs` | 股票代號輸入拆解（逗號、全形逗號、頓號、空白、分號；轉大寫、去重）；`/stock-compare` 使用 | |
| `Services/PriceSyncService.cs` | 個股日線逐月同步（由本月往前、連續 3 個月無資料中斷、請求間隔 `Twse:RequestDelayMs`、可取消、進度回報），最後一次寫入 `StockDailyPrice`；`SyncManyAsync` 多檔依序同步（`/stock-update` 日線卡片可輸入 `006208,00981A` 這樣的多檔；某檔完全下載失敗即中斷整批） | |
| `Services/IndustryPriceSyncService.cs` | 依產業別（`StockList` 的「產業別」代碼，例如 24）批次同步日線：每檔由本月往前逐月抓到上市日（最多 480 個月）；可續跑（已完整月份〔最後交易日 ≥ 25 號且早於本月〕不重抓、`PriceSyncLog` 記錄已追溯到上市日）；任何下載錯誤立即停止整批（不當成沒資料）；間隔 `Twse:BatchRequestDelayMs`（預設 3 秒）；每檔抓完即寫入；`maxMonths` 可限制每檔往前幾個月（頁面有「近三個月」按鈕） | |
| `Services/IndustryPriceSyncRunner.cs` | 上面服務的背景執行者（**Singleton**，`Task.Run`，不綁頁面／連線；關閉分頁也繼續跑，網站程式關掉才中止）；`/stock-update` 訂閱 `Changed` 顯示進度 | |
| `Services/InstitutionalSyncService.cs` | 三大法人逐日同步（T86 買賣超＋MI_QFIIS 外資持股），寫入 `InstitutionalDaily`、記錄 `InstitutionalSyncLog` | |
| `Services/FinMindApiClient.cs` | FinMind API（匿名、無 Token）：個股三大法人買賣超（每日多列合併）、外資持股；`ip banned`／402 丟 `FinMindBlockedException` | FinMind 格式改變時 |
| `Services/FinMindSyncService.cs` | FinMind 逐檔同步，寫入 `InstitutionalDaily`、記錄 `FinMindSyncLog` | |
| `Services/OilPriceApiClient.cs` | oilpriceapi.com：Brent `past_year`（每天一筆當日均價 `daily_average`）；API Key 來自 `OilPrice:ApiKey`（錯誤訊息不可含 Key） | oilpriceapi 格式改變時 |
| `Services/YahooOilSyncService.cs` | Yahoo Brent 期貨 `BZ=F` 同步（暫時的比較用）：借用 `IMarketIndexApiClient` 一次取回 2007-07-30 起的日線，只存**收盤價**到 `OilPriceDaily`（代號 `BRENT_YAHOO_BZF`，與 oilpriceapi 的 `BRENT_CRUDE_USD` 分開） | |
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
| `Repositories/Implements/StockPriceRepository.cs` | `StockDailyPrice` 唯一讀寫入口；另實作 `IPriceCoverageRepository`（各月最後交易日、`PriceSyncLog` 的「已追溯到上市日」） | 新查詢 |
| `Repositories/Implements/StockNameRepository.cs` | 股票名稱（合併 `StockList` ＋ `StockNameList`）；另實作 `IIndustryRepository`（產業別清單〔代碼統一兩位數、名稱對照在 `Models/IndustryOption.cs` 的 `IndustryNames`〕、依產業別查代號，忽略前導 0 與欄位空白；另有特別的「ETF」選項 = `StockNameList` 中代號以 00 開頭者（使用者指定的判斷依據），需先按過「更新股票清單」） | |
| `Repositories/Implements/InstitutionalRepository.cs` | `InstitutionalDaily`／`InstitutionalSyncLog`／`FinMindSyncLog` 讀寫 | |
| `Repositories/StockNameRepositoryExtensions.cs` | 顯示用名稱查詢，失敗回傳 null／空字典 | |
| `Repositories/DatabaseInitializer.cs` | 啟動時 `CREATE TABLE IF NOT EXISTS`（Schema 在此） | 新增資料表 |
| `wwwroot/app.css` | 全域：色彩變數、版面、側邊欄、共用面板 `.panel-standard` | |
| `Components/Layout/NavMenu.razor.css` | 側邊欄**全部**樣式（深色漸層底、選取項目淡藍底＋左側藍色指示條、收合狀態；`app.css` 已不含側邊欄規則） | |
| `Components/Pages/*.razor.css` | 各頁 scoped CSS | |
| `docs/TODO.md` | 目前狀態：待辦（有編號與標記）、已知問題／限制、想法；**每次 commit 時同步更新** | 每次 commit |
| `appsettings.Example.json` | 設定範本（`appsettings.json` 被 gitignore） | 新增設定鍵時同步更新 |
| `tests/StockDashboard.Tests/` | xUnit：`TwseApiClient` 解析、報酬率計算、Repository（暫存 SQLite） | 改這些類別時補測試 |
| `tests/.../Fixtures/` | 證交所實際回應範例（測試與 `fake-twse.py` 共用） | 證交所格式改變時更新 |
| `tools/ui-snapshot.js` | UI 快照：擷取／比對 computed style 與截圖 | 新增頁面或互動狀態時加到 `STATES` |
| `tools/fake-twse.py`、`tools/make-test-db.py` | 假證交所（含 FinMind 端點）伺服器、確定性測試資料庫 | |

還原股價（型態掃描、技術圖表有「還原股價」開關（型態掃描預設還原〔使用者指定〕，技術圖表預設原始價）；`PatternScanService.ScanAsync`／`StockChartService.LoadAsync` 的 `adjusted` 參數用 `PriceAdjuster.ApplyToCandles` 還原完整日線後才判斷／算指標；小卡連結帶 `&adj=1`；`/stock-compare` 有開關，預設原始價，`StockPerformanceService` 的 `adjusted` 參數）：`Services/PriceAdjuster.cs`（比例還原純計算：係數 = 參考價 / 前收盤，除權息日之前的 OHLC 與漲跌價差連乘）、`StockPriceService`（`isAdjusted` 才套用）、`StockExRightEvent` 表（**不是 `StockCorporateAction`：使用者的 stock.db 已有同名但欄位不同的表**）與 `StockCorporateActionRepository`、`StockCorporateActionSyncService`（逐檔、封鎖即停）＋`FinMindCorporateActionApiClient`（FinMind `TaiwanStockDividendResult`：`before_price` → PreClose、`reference_price`（無則 `after_price`）→ RefPrice，2003 年起整段一次取回；`/stock-update` 有同步卡片）。
主專案 `StockDashboard.csproj` 在 repo 根目錄，**已排除 `tests/**`、`tools/**`**（否則會把測試碼編進網站）；新增資料夾若不屬於網站也要排除。
