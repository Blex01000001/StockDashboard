# PROGRESS.md

已知限制與待辦（規則見 `CLAUDE.md` 的「PROGRESS.md」章節）。

## 已知限制
- 只支援上市 (TWSE)：上櫃 (TPEx) 股票查不到日線，也沒有名稱。
- 股價為原始價，未做除權息還原。
- 已下市股票不會從 `StockNameList` 移除。
- 滾動報酬率以所有股票「聯集」時間軸位移 N 格計算；股票交易日不同（停牌）時，N 日的意義略有偏差。
- ECharts 走 CDN，離線時無法畫圖。
- 「更新股票清單」「三大法人同步」（證交所、FinMind）的實際連線尚未由使用者確認成功（開發環境連不到；解析以使用者提供的實際回應測試）。
- 證交所三大法人只有上市 (T86 `ALLBUT0999`)，上櫃股票可用 FinMind 同步；外資持股 MI_QFIIS 若比 T86 晚公布，當天同步可能只有買賣超、沒有持股比例（隔天再同步會補上）。

## 待辦
- 還原股價：實作 `StockPriceService.ApplyDividendAdjustment`，需要除權息資料表與 `IStockDividendRepository` 實作。
- `StockCorporateAction`、`StockCombinationDto` 為規劃中的空殼。
