using Microsoft.Data.Sqlite;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;
using Dapper;


namespace StockDashboard.Repositories.Implements
{
    public class StockPriceRepository : IStockPriceRepository
    {
        private readonly string _connectionString;

        // 透過建構子注入 IConfiguration，統一從 appsettings.json 讀取組態
        public StockPriceRepository(IConfiguration configuration)
        {
            // 採用 Fail-fast (快速失敗) 原則：若系統找不到連線字串，應在啟動時立即拋出例外，而非等到查詢時才報錯。
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration), "系統設定中遺失 DefaultConnection 連線字串。");
        }
        
        public async Task<IEnumerable<StockDailyPriceEntity>> GetHistoricalPricesAsync(string stockId, string startDate, string endDate)
        {
            // 使用 using var 確保離開作用域時，確實關閉並釋放資料庫連線
            using var connection = new SqliteConnection(_connectionString);

            // 明確列出需要的欄位，避免使用 SELECT *，這在大型查詢中能顯著降低記憶體佔用與 I/O 成本
            string sql = @"
                SELECT 
                    StockId, TradeDate, Volume, Amount, 
                    OpenPrice, HighPrice, LowPrice, ClosePrice, 
                    PriceChange, TradeCount, Note
                FROM StockDailyPrice 
                WHERE StockId = @StockId 
                  AND TradeDate >= @StartDate 
                  AND TradeDate <= @EndDate
                ORDER BY TradeDate ASC";

            return await connection.QueryAsync<StockDailyPriceEntity>(sql, new
            {
                StockId = stockId,
                StartDate = startDate,
                EndDate = endDate
            });
        }

        public async Task<IEnumerable<StockDailyPriceEntity>> GetMultipleHistoricalPricesAsync(IEnumerable<string> stockIds, string startDate, string endDate)
        {
            using var connection = new SqliteConnection(_connectionString);

            // Dapper 針對 IN 語法的強大支援：它會自動將 @StockIds 展開為參數化查詢 (如 @StockIds1, @StockIds2...)，
            // 避免了 SQL Injection 風險，同時保持語法簡潔。
            string sql = @"
                SELECT 
                    StockId, TradeDate, Volume, Amount, 
                    OpenPrice, HighPrice, LowPrice, ClosePrice, 
                    PriceChange, TradeCount, Note
                FROM StockDailyPrice 
                WHERE StockId IN @StockIds 
                  AND TradeDate >= @StartDate 
                  AND TradeDate <= @EndDate
                ORDER BY TradeDate ASC";

            return await connection.QueryAsync<StockDailyPriceEntity>(sql, new
            {
                StockIds = stockIds,
                StartDate = startDate,
                EndDate = endDate
            });
        }
    }
}
