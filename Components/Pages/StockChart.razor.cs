using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Data.Sqlite; // 💡 新增這行
using Dapper;                // 💡 新增這行
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StockDashboard.Components.Pages
{
    public partial class StockChart : ComponentBase, IAsyncDisposable
    {
        [Parameter] public string TargetStockId { get; set; } = "2330";
        public string Message { get; set; } = string.Empty;
        private bool _isLoading = false;
        private IJSObjectReference? _jsModule;

        // 💡 請在此處替換成你實際的 SQLite 資料庫檔案路徑
        // 如果資料庫在專案根目錄，可以使用 "Data Source=stock.db"
        private readonly string _connectionString = "Data Source=E:\\Code\\C#\\StockDashboard\\stock.db";

        public async Task LoadChartData()
        {
            if (string.IsNullOrWhiteSpace(TargetStockId))
            {
                Message = "請輸入有效的股票代號！";
                return;
            }
            if (_isLoading) return;

            try
            {
                _isLoading = true;
                Message = "資料載入中...";

                // ✨ 核心改變：從資料庫撈取真實資料
                var realData = await GetStockDataFromDb(TargetStockId);

                if (realData == null || !realData.Any())
                {
                    Message = $"找不到代號 {TargetStockId} 的股票資料。";
                    return;
                }

                Message = string.Empty;

                if (_jsModule == null)
                {
                    // 根據你先前的設定，確保路徑正確
                    _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                        "import", "./Components/Pages/StockChart.razor.js");
                }

                await _jsModule.InvokeVoidAsync("renderStockChart", "chart-container", TargetStockId, realData);
            }
            catch (Exception ex)
            {
                Message = $"圖表渲染失敗或資料庫讀取失敗: {ex.Message}";
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// ✨ 從 SQLite 資料庫讀取真實 K 線資料
        /// </summary>
        private async Task<List<StockDataPoint>> GetStockDataFromDb(string stockId)
        {
            // ✨ 修改後的 SQL 語法：
            // 1. 內層 (子查詢) 先用 DESC 排序並 LIMIT 220，確保抓到的是「最新」的 220 筆。
            // 2. 外層再用 ORDER BY TradeDate ASC，把這 220 筆重新按時間從舊到新排好，餵給圖表。
            string sql = @"
                SELECT 
                    TradeDate AS Date,
                    OpenPrice AS Open,
                    HighPrice AS High,
                    LowPrice AS Low,
                    ClosePrice AS Close,
                    Volume
                FROM (
                    SELECT * FROM StockDailyPrice
                    WHERE StockId = @StockId
                    ORDER BY TradeDate DESC
                    LIMIT 220
                )
                ORDER BY TradeDate ASC";

            using (var connection = new SqliteConnection(_connectionString))
            {
                var result = await connection.QueryAsync<StockDataPoint>(sql, new { StockId = stockId });
                return result.ToList();
            }
        }
        public async ValueTask DisposeAsync()
        {
            if (_jsModule != null)
            {
                await _jsModule.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// K 線資料結構（保持不變，剛好能對應 SQL 的 AS 別名）
    /// </summary>
    public class StockDataPoint
    {
        public string Date { get; set; } = string.Empty;
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public long Volume { get; set; }
    }
}