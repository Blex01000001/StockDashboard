namespace StockDashboard.Models
{
    /// <summary>產業別下拉選單的一個選項：代碼 (兩位數，例如 24)、名稱、該產業在 StockList 的股票檔數</summary>
    public record IndustryOption(string Code, string Name, int Count)
    {
        public string Label => Code == IndustryNames.EtfCode ? $"ETF（{Count} 檔）" : $"{Code} {Name}（{Count} 檔）";
    }

    /// <summary>
    /// 證交所產業別代碼對照名稱 (只用來顯示)。StockList 只存代碼；不認得的代碼名稱顯示為「其他」。
    /// </summary>
    public static class IndustryNames
    {
        /// <summary>特別的「產業別」：ETF (StockNameList 中代號 00 開頭、不在 StockList 的股票)</summary>
        public const string EtfCode = "ETF";

        private static readonly Dictionary<string, string> Names = new()
        {
            ["01"] = "水泥工業", ["02"] = "食品工業", ["03"] = "塑膠工業", ["04"] = "紡織纖維", ["05"] = "電機機械",
            ["06"] = "電器電纜", ["08"] = "玻璃陶瓷", ["09"] = "造紙工業", ["10"] = "鋼鐵工業", ["11"] = "橡膠工業",
            ["12"] = "汽車工業", ["14"] = "建材營造業", ["15"] = "航運業", ["16"] = "觀光餐旅", ["17"] = "金融保險業",
            ["18"] = "貿易百貨業", ["19"] = "綜合", ["20"] = "其他", ["21"] = "化學工業", ["22"] = "生技醫療業",
            ["23"] = "油電燃氣業", ["24"] = "半導體業", ["25"] = "電腦及週邊設備業", ["26"] = "光電業", ["27"] = "通信網路業",
            ["28"] = "電子零組件業", ["29"] = "電子通路業", ["30"] = "資訊服務業", ["31"] = "其他電子業", ["32"] = "文化創意業",
            ["33"] = "農業科技業", ["34"] = "電子商務", ["35"] = "綠能環保", ["36"] = "數位雲端", ["37"] = "運動休閒",
            ["38"] = "居家生活", ["91"] = "存託憑證",
        };

        /// <summary>代碼統一成兩位數 (1 → 01)，去除前後空白</summary>
        public static string Normalize(string code)
        {
            string trimmed = (code ?? string.Empty).Trim();
            if (trimmed.Equals(EtfCode, StringComparison.OrdinalIgnoreCase)) return EtfCode;
            return trimmed.Length == 0 ? string.Empty : trimmed.TrimStart('0').PadLeft(2, '0');
        }

        public static string NameOf(string code) => Names.TryGetValue(Normalize(code), out var name) ? name : "其他";
    }
}
