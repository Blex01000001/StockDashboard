using Dapper;
using Microsoft.Data.Sqlite;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Repositories.Implements
{
    /// <summary>StockCorporateAction (除權息事件) 的存取入口。</summary>
    public class StockCorporateActionRepository : IStockCorporateActionRepository
    {
        private readonly string _connectionString;

        public StockCorporateActionRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration), "系統設定中遺失 DefaultConnection 連線字串。");
        }

        public async Task<int> UpsertAsync(IEnumerable<StockCorporateActionEntity> rows)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            int affected = await connection.ExecuteAsync(@"
                INSERT INTO StockCorporateAction (StockId, ExDate, PreClose, RefPrice)
                VALUES (@StockId, @ExDate, @PreClose, @RefPrice)
                ON CONFLICT(StockId, ExDate) DO UPDATE SET
                    PreClose = excluded.PreClose, RefPrice = excluded.RefPrice;",
                rows, transaction);
            transaction.Commit();
            return affected;
        }

        public async Task<IEnumerable<StockCorporateActionEntity>> GetByStockIdAsync(string stockId)
        {
            using var connection = new SqliteConnection(_connectionString);
            return await connection.QueryAsync<StockCorporateActionEntity>(
                "SELECT StockId, ExDate, PreClose, RefPrice FROM StockCorporateAction WHERE StockId = @StockId ORDER BY ExDate",
                new { StockId = stockId });
        }

        public async Task<(int Stocks, int Events)> GetSummaryAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            var row = await connection.QuerySingleAsync<SummaryRow>(
                "SELECT COUNT(DISTINCT StockId) AS Stocks, COUNT(*) AS Events FROM StockCorporateAction");
            return ((int)row.Stocks, (int)row.Events);
        }

        private sealed class SummaryRow
        {
            public long Stocks { get; set; }
            public long Events { get; set; }
        }
    }
}
