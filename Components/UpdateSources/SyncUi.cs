using Microsoft.JSInterop;
using StockDashboard.Models;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Components.UpdateSources
{
    /// <summary>/stock-update 各來源元件共用的小工具</summary>
    internal static class SyncUi
    {
        /// <summary>所有「停止」按鈕共用的確認視窗；連線中斷等無法詢問時一律不停止</summary>
        public static async Task<bool> ConfirmStopAsync(IJSRuntime js, string what, string note)
        {
            try { return await js.InvokeAsync<bool>("confirm", $"確定要停止{what}嗎？\n\n已抓到的資料都已存入，{note}。"); }
            catch (Exception) { return false; }
        }

        /// <summary>產業別下拉選單的選項；讀取失敗或沒有產業別欄位時回傳空清單</summary>
        public static async Task<List<IndustryOption>> LoadIndustriesAsync(IIndustryRepository repo)
        {
            try { return await repo.GetIndustriesAsync(); }
            catch (Exception) { return new(); }
        }

        /// <summary>讀某產業別的股票代號；沒選或找不到時用 report 顯示原因並回傳 null</summary>
        public static async Task<List<string>?> LoadIndustryIdsAsync(IIndustryRepository repo, string industryCode, Action<string> report)
        {
            if (string.IsNullOrWhiteSpace(industryCode)) { report("請先選擇產業別！"); return null; }
            try
            {
                var ids = await repo.GetStockIdsByIndustryAsync(industryCode);
                if (ids.Count == 0) { report($"StockList 找不到產業別 {industryCode} 的股票"); return null; }
                return ids;
            }
            catch (Exception ex)
            {
                report($"讀取產業別失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 同步回報進度用：不用 System.Progress&lt;T&gt;，因為它的回呼是非同步排入的，
        /// 可能在同步結束後才執行，蓋掉最後的結果訊息
        /// </summary>
        public sealed class Progress<T>(Action<T> onReport) : IProgress<T>
        {
            public void Report(T value) => onReport(value);
        }
    }
}
