namespace StockDashboard.Models.Entities
{
    /// <summary>
    /// 個股每日三大法人資料 (對應 SQLite InstitutionalDaily 表格)。單位一律為「股」。
    /// 買賣超來自證交所 T86 (三大法人買賣超日報)；持股來自 MI_QFIIS (外資及陸資投資持股統計)。
    /// 兩個來源分開同步，所以任一組欄位都可能是 null。
    /// </summary>
    public class InstitutionalDailyEntity
    {
        public string StockId { get; set; } = string.Empty;
        public string TradeDate { get; set; } = string.Empty;   // yyyy-MM-dd

        // ── T86 買賣超 ──
        public long? ForeignNet { get; set; }        // 外陸資買賣超 (不含外資自營商)
        public long? ForeignDealerNet { get; set; }  // 外資自營商買賣超
        public long? TrustNet { get; set; }          // 投信買賣超
        public long? DealerNet { get; set; }         // 自營商買賣超 (自行買賣 + 避險)
        public long? TotalNet { get; set; }          // 三大法人買賣超

        // ── MI_QFIIS 外資持股 ──
        public long? IssuedShares { get; set; }       // 發行股數
        public long? ForeignShares { get; set; }      // 全體外資及陸資持有股數
        public double? ForeignHoldingPct { get; set; } // 全體外資及陸資持股比率 (%)

        // 外資合計 = 外陸資 + 外資自營商 (兩者都屬外資；三大法人合計也是這樣加總)
        public long? ForeignTotalNet => ForeignNet == null && ForeignDealerNet == null ? null : (ForeignNet ?? 0) + (ForeignDealerNet ?? 0);
    }
}
