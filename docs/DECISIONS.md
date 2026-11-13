# 架構決策（不要改回去）

改到同步流程、資料庫寫入、靜態檔載入、原油／費半日期對齊、頁面狀態之前先讀。

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
| 依產業批次同步：已完整月份不重抓、遇錯即停整批、背景執行者用 Singleton | 整個產業要跑數小時到十幾小時且常被封鎖，必須可續跑；下載錯誤若當成「沒資料」會把每一檔誤判成已到上市日；使用者要放著去吃飯洗澡，所以不能綁在頁面或連線上（Scoped／元件會隨連線中斷） |
| 型態掃描頁的狀態放在 Scoped 的 `PatternScanState` | 使用者要求切換頁面後保留結果；Blazor 元件離開頁面即銷毀，Scoped 服務在同一連線內持續存在（F5 重新整理才重置） |
