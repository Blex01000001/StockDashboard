using Microsoft.Data.Sqlite;
using StockDashboard.Models;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;
using Dapper;


namespace StockDashboard.Repositories.Implements
{
    /// <summary>
    /// StockDailyPrice 資料表的唯一存取入口。
    /// 所有頁面 / Service 都應透過此類別讀寫股價，不要在 Razor 元件內直接寫 SQL。
    /// </summary>
    public class StockPriceRepository : IStockPriceRepository
    {
        private readonly string _connectionString;

        // 明確列出需要的欄位，避免使用 SELECT *，這在大型查詢中能顯著降低記憶體佔用與 I/O 成本
        private const string AllColumns = @"
                    StockId, TradeDate, Volume, Amount,
                    OpenPrice, HighPrice, LowPrice, ClosePrice,
                    PriceChange, TradeCount, Note";

        // 透過建構子注入 IConfiguration，統一從 appsettings.json 讀取組態
        public StockPriceRepository(IConfiguration configuration)
        {
            // 採用 Fail-fast (快速失敗) 原則：若系統找不到連線字串，應立即拋出例外，而非等到查詢時才報錯。
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration), "系統設定中遺失 DefaultConnection 連線字串。");
        }

        public async Task<IEnumerable<StockDailyPriceEntity>> GetHistoricalPricesAsync(string stockId, string startDate, string endDate)
        {
            // 使用 using var 確保離開作用域時，確實關閉並釋放資料庫連線
            using var connection = new SqliteConnection(_connectionString);

            string sql = $@"
                SELECT {AllColumns}
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

            // Dapper 針對 IN 語法的支援：它會自動將 @StockIds 展開為參數化查詢 (如 @StockIds1, @StockIds2...)，
            // 避免了 SQL Injection 風險，同時保持語法簡潔。
            string sql = $@"
                SELECT {AllColumns}
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

        public async Task<IEnumerable<StockDataPoint>> GetLatestCandlesAsync(string stockId, int count)
        {
            using var connection = new SqliteConnection(_connectionString);

            // 1. 內層 (子查詢) 先用 DESC 排序並 LIMIT，確保抓到的是「最新」的 N 筆。
            // 2. 外層再用 ORDER BY TradeDate ASC，把這 N 筆重新按時間從舊到新排好，餵給圖表。
            // 欄位以 AS 別名對應 StockDataPoint 的屬性名稱。
            string sql = @"
                SELECT
                    TradeDate AS Date,
                    OpenPrice AS Open,
                    HighPrice AS High,
                    LowPrice AS Low,
                    ClosePrice AS Close,
                    Volume
                FROM (
                    SELECT TradeDate, OpenPrice, HighPrice, LowPrice, ClosePrice, Volume
                    FROM StockDailyPrice
                    WHERE StockId = @StockId
                    ORDER BY TradeDate DESC
                    LIMIT @Count
                )
                ORDER BY TradeDate ASC";

            return await connection.QueryAsync<StockDataPoint>(sql, new { StockId = stockId, Count = count });
        }

        public async Task<StockDailyPriceEntity?> GetLatestPriceAsync(string stockId)
        {
            using var connection = new SqliteConnection(_connectionString);

            string sql = $@"
                SELECT {AllColumns}
                FROM StockDailyPrice
                WHERE StockId = @StockId
                ORDER BY TradeDate DESC
                LIMIT 1";

            return await connection.QueryFirstOrDefaultAsync<StockDailyPriceEntity>(sql, new { StockId = stockId });
        }

        public async Task<int> UpsertPricesAsync(IEnumerable<StockDailyPriceEntity> prices)
        {
            // 使用 SQLite 的 INSERT OR REPLACE 語法：
            // 若 PRIMARY KEY (StockId, TradeDate) 已存在就整筆覆寫，不存在就新增。
            string sql = @"
                INSERT OR REPLACE INTO StockDailyPrice
                (StockId, TradeDate, Volume, Amount, OpenPrice, HighPrice, LowPrice, ClosePrice, PriceChange, TradeCount)
                VALUES
                (@StockId, @TradeDate, @Volume, @Amount, @OpenPrice, @HighPrice, @LowPrice, @ClosePrice, @PriceChange, @TradeCount);";

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 批次寫入包在同一個交易 (Transaction) 中：
            // SQLite 預設每一筆 INSERT 都是獨立交易 (每筆都寫一次硬碟)，包起來後速度可快上數十倍，
            // 且中途失敗會整批還原，不會留下寫一半的資料。
            using var transaction = connection.BeginTransaction();
            int affectedRows = await connection.ExecuteAsync(sql, prices, transaction);
            transaction.Commit();

            return affectedRows;
        }
    }
}
