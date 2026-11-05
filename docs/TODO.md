# TODO：目前狀態

本檔只記**現況**：待辦、已知問題／限制、想法（歷史請看 `git log`）。維護規則見 `CLAUDE.md` 的「TODO 維護規則」。
待辦標記：【直接做】使用者點名即可動手；【先討論】會改架構／輸出格式／引入新工具，或方向未定；【使用者】需要使用者自己操作。

## 待辦

1. 【先討論】原油（UKOIL／Brent）：建議做成附圖（加到 `ChartDisplaySettings.SubChartOptions`＋`candle-chart.js` 的 `SUB_CHARTS`），需新資料表與同步卡片（建表前確認 stock.db 無同名表）。待使用者決定：附圖或主圖疊加、資料來源（FinMind `CrudeOilPrices`？需使用者貼回傳範例）、日期對齊（建議用前一個已收盤油價、休市沿用前值）。
2. 【先討論】布林通道：畫在 K 線上、上下軌間只填淡底色（中軌 = MA20 不重畫），在 `ChartDisplayOptions` 加勾選與收折參數（`IndicatorSettings` 加 period／倍數，`TechnicalIndicators` 加計算並補測試），兩頁共用。待確認：參數 20, 2、是否也加到型態小卡、「顯示均線」群組是否改名「主圖疊加」。
3. 【使用者】確認晨星、紅三兵、黑三兵的預設參數（`Services/Patterns/*Pattern.cs`，目前是 Claude 依常見定義訂的）。
4. 【使用者】在自己的電腦實際測試「更新股票清單」「三大法人同步（證交所、FinMind）」能連線成功（沙盒連不到；FinMind 先同步 1～2 檔），並確認 stock.db 沒有既存的 `FinMindSyncLog`／`InstitutionalDaily`／`InstitutionalSyncLog` 同名表。
5. 【直接做】日線逐月同步的迴圈仍寫在 `StockUpdate.razor.cs` 的 `SyncFromApi`：比照 `InstitutionalSyncService` 移到 Service（可停止、進度回報）並補測試；畫面與訊息不變。
6. 【直接做】`tools/make-test-db.py` 的最高／最低價沒有四捨五入（提示框出現 `872.1892499999`）：改為 2 位小數；會改變 UI 快照基準，改完重新擷取 before。
7. 【直接做】`IStockPriceRepository.GetLatestCandlesAsync` 網站已不再使用（技術圖表改用 `GetAllCandlesAsync`），只剩測試：刪除介面／實作與對應測試，或保留並註明用途。
8. 【先討論】還原股價：實作 `StockPriceService.ApplyDividendAdjustment`（未註冊 DI），需除權息資料來源、資料表與 `IStockDividendRepository` 實作；`StockCorporateAction*`、`StockCombinationDto` 也是規劃中的空殼。

## 已知問題／限制

- 日線與股票名稱只支援上市 (TWSE)：上櫃 (TPEx) 查不到日線、沒有名稱（三大法人可用 FinMind 同步上櫃，但沒有日線仍無法掃描）。
- 股價為原始價，未做除權息還原。
- 已下市股票不會從 `StockNameList` 移除。
- 滾動報酬率以所有股票「聯集」時間軸位移 N 格計算；股票交易日不同（停牌）時，N 日的意義略有偏差。
- ECharts 走 CDN，離線時無法畫圖。
- 證交所三大法人只有上市 (T86 `ALLBUT0999`)；MI_QFIIS 若比 T86 晚公布，當天同步可能只有買賣超、沒有持股比例（下次同步會補）。
- FinMind 匿名請求額度未查證（印象約每小時 300 次）；每檔 2 個請求，一次同步太多檔可能達上限而自動停止。
- 型態掃描頁與技術圖表頁的附圖／均線／指標設定各自獨立，不會互相帶入（小卡連結開啟技術圖表時也不帶）。
- 技術圖表從小卡開啟後網址帶 `?id=&date=`，在該頁按 F5 會重新標示同一個型態日。
- 附圖全部勾選時小卡與技術圖表會很高（每個附圖固定一列，刻意的設計）。

## 想法

- 技術圖表標出該股所有符合型態的日子，並加「上一筆／下一筆型態」逐筆瀏覽掃描結果。
- 型態掃描加三大法人篩選條件，例如「型態成立當天外資買超」。
- FinMind 三大法人卡片加「使用型態掃描的股票清單」按鈕，省去重複輸入代號。
- 用 FinMind 日線補上櫃股票，解除「只支援上市」限制。
- ECharts 改為本機檔案（`wwwroot`），離線也能畫圖。
