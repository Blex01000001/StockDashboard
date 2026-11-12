namespace StockDashboard.Services
{
    /// <summary>把使用者輸入的股票代號文字 (逗號、全形逗號、頓號、空白、分號分隔) 拆成大寫、不重複的代號清單。</summary>
    public static class StockIdParser
    {
        private static readonly char[] Separators = { ',', '，', '、', ' ', ';', '；' };

        public static List<string> Parse(string? input) =>
            (input ?? string.Empty)
                .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => s.Length > 0)
                .Distinct()
                .ToList();
    }
}
