using StockDashboard.Models;
using StockDashboard.Models.Patterns;

namespace StockDashboard.Services.Patterns
{
    /// <summary>
    /// 一種 K 線型態。新增型態：實作此介面 → 在 Program.cs 註冊為 ICandlePattern → 補測試，頁面會自動出現。
    /// </summary>
    public interface ICandlePattern
    {
        // 唯一識別碼 (英文，存在畫面狀態中)
        string Key { get; }

        // 顯示名稱，例如「看漲吞沒」
        string Name { get; }

        // 一句話說明型態 (顯示在預覽圖下方)
        string Description { get; }

        // 型態由幾根 K 棒組成；最後一根是「型態成立日」
        int BarCount { get; }

        // 可調參數 (頁面依此產生輸入框)
        IReadOnlyList<PatternParameter> Parameters { get; }

        // candles 由舊到新；判斷 index 那天 (型態最後一根) 是否成立
        bool IsMatch(IReadOnlyList<StockDataPoint> candles, int index, PatternParameterValues values);

        // 依目前參數產生一段一定符合型態的範例 K 線 (預覽用)
        PatternExample BuildExample(PatternParameterValues values);
    }
}
