using Dapper;
using Microsoft.Data.Sqlite;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Repositories.Implements
{
    /// <summary>
    /// StockList 資料表 (股票代號 ↔ 名稱) 的存取入口
    /// </summary>
    public class StockListRepository : IStockListRepository
    {
        private const string AllColumns = "StockId, StockName, Market, UpdatedAt";

        private readonly string _connectionString;

        public StockListRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration), "系統設定中遺失 DefaultConnection 連線字串。");
        }

        public async Task<IEnumerable<StockListEntity>> GetAllStocksAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            return await connection.QueryAsync<StockListEntity>(
                $"SELECT {AllColumns} FROM StockList ORDER BY StockId");
        }

        public async Task<StockListEntity?> GetStockByIdAsync(string stockId)
        {
            using var connection = new SqliteConnection(_connectionString);
            return await connection.QueryFirstOrDefaultAsync<StockListEntity>(
                $"SELECT {AllColumns} FROM StockList WHERE StockId = @StockId",
                new { StockId = stockId });
        }

        public async Task<Dictionary<string, string>> GetStockNamesAsync(IEnumerable<string> stockIds)
        {
            using var connection = new SqliteConnection(_connectionString);
            var rows = await connection.QueryAsync<StockListEntity>(
                "SELECT StockId, StockName FROM StockList WHERE StockId IN @StockIds",
                new { StockIds = stockIds });
            return rows.ToDictionary(r => r.StockId, r => r.StockName);
        }

        public async Task<(int Count, string? LastUpdatedAt)> GetSummaryAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            var row = await connection.QuerySingleAsync<SummaryRow>(
                "SELECT COUNT(*) AS Count, MAX(UpdatedAt) AS LastUpdatedAt FROM StockList");
            return ((int)row.Count, row.LastUpdatedAt);
        }

        public async Task<int> UpsertStocksAsync(IEnumerable<StockListEntity> stocks)
        {
            // ON CONFLICT ... DO UPDATE：代號已存在就更新名稱 (公司改名時)，不存在就新增
            // 不使用 INSERT OR REPLACE，避免未來此表新增欄位時被整列覆寫成 NULL
            string sql = @"
                INSERT INTO StockList (StockId, StockName, Market, UpdatedAt)
                VALUES (@StockId, @StockName, @Market, @UpdatedAt)
                ON CONFLICT(StockId) DO UPDATE SET
                    StockName = excluded.StockName,
                    Market    = excluded.Market,
                    UpdatedAt = excluded.UpdatedAt;";

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 上千筆一起寫入，包在同一個交易中速度才快
            using var transaction = connection.BeginTransaction();
            int affectedRows = await connection.ExecuteAsync(sql, stocks, transaction);
            transaction.Commit();

            return affectedRows;
        }

        // SQLite 的 COUNT(*) 回傳 64 位元整數，用 long 接收
        private sealed class SummaryRow
        {
            public long Count { get; set; }
            public string? LastUpdatedAt { get; set; }
        }
    }
}
