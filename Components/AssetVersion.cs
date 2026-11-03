namespace StockDashboard.Components
{
    /// <summary>
    /// 靜態檔案 (CSS / JS) 的版本號：每次編譯產生的組件識別碼 (MVID) 前 8 碼。
    /// 加在網址後 (?v=...)，強迫瀏覽器在程式更新後重新下載，避免沿用快取中的舊檔造成版面或圖表錯亂。
    /// </summary>
    public static class AssetVersion
    {
        public static readonly string Value = typeof(AssetVersion).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..8];

        // 頁面的 JS 模組網址，例如 ./Components/Pages/StockChart.razor.js?v=1a2b3c4d
        public static string Module(string path) => $"{path}?v={Value}";
    }
}
