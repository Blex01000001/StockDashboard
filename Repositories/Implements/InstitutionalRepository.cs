using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Repositories.Implements
{
    /// <summary>
    /// InstitutionalDaily (三大法人) 與 InstitutionalSyncLog (同步紀錄) 的存取入口。
    /// 買賣超與持股來自不同來源、分開寫入，所以用 ON CONFLICT DO UPDATE 只更新各自的欄位。
    /// </summary>
    public class InstitutionalRepository : IInstitutionalRepository
    {
        private const string AllColumns = @"StockId, TradeDate, ForeignNet, ForeignDealerNet, TrustNet, DealerNet, TotalNet,
                                            IssuedShares, ForeignShares, ForeignHoldingPct";

        private readonly string _connectionString;

        public InstitutionalRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration), "系統設定中遺失 DefaultConnection 連線字串。");
        }

        public Task<int> UpsertTradingAsync(IEnumerable<InstitutionalDailyEntity> rows) => ExecuteInTransactionAsync(@"
            INSERT INTO InstitutionalDaily (StockId, TradeDate, ForeignNet, ForeignDealerNet, TrustNet, DealerNet, TotalNet)
            VALUES (@StockId, @TradeDate, @ForeignNet, @ForeignDealerNet, @TrustNet, @DealerNet, @TotalNet)
            ON CONFLICT(StockId, TradeDate) DO UPDATE SET
                ForeignNet = excluded.ForeignNet, ForeignDealerNet = excluded.ForeignDealerNet,
                TrustNet = excluded.TrustNet, DealerNet = excluded.DealerNet, TotalNet = excluded.TotalNet;", rows);

        public Task<int> UpsertHoldingAsync(IEnumerable<InstitutionalDailyEntity> rows) => ExecuteInTransactionAsync(@"
            INSERT INTO InstitutionalDaily (StockId, TradeDate, IssuedShares, ForeignShares, ForeignHoldingPct)
            VALUES (@StockId, @TradeDate, @IssuedShares, @ForeignShares, @ForeignHoldingPct)
            ON CONFLICT(StockId, TradeDate) DO UPDATE SET
                IssuedShares = excluded.IssuedShares, ForeignShares = excluded.ForeignShares,
                ForeignHoldingPct = excluded.ForeignHoldingPct;", rows);

        public async Task LogCheckAsync(string tradeDate, string source, bool hasData, DateTime checkedAt)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.ExecuteAsync(@"
                INSERT INTO InstitutionalSyncLog (TradeDate, Source, HasData, CheckedAt)
                VALUES (@TradeDate, @Source, @HasData, @CheckedAt)
                ON CONFLICT(TradeDate, Source) DO UPDATE SET HasData = excluded.HasData, CheckedAt = excluded.CheckedAt;",
                new { TradeDate = tradeDate, Source = source, HasData = hasData ? 1 : 0, CheckedAt = checkedAt.ToString("yyyy-MM-dd HH:mm:ss") });
        }

        public async Task<Dictionary<string, (bool HasData, DateTime CheckedAt)>> GetCheckedDatesAsync(string source)
        {
            using var connection = new SqliteConnection(_connectionString);
            var rows = await connection.QueryAsync<(string TradeDate, long HasData, string CheckedAt)>(
                "SELECT TradeDate, HasData, CheckedAt FROM InstitutionalSyncLog WHERE Source = @Source", new { Source = source });
            return rows.ToDictionary(
                r => r.TradeDate,
                r => (r.HasData == 1, DateTime.ParseExact(r.CheckedAt, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        }

        public async Task LogFinMindSyncAsync(string stockId, string dataset, string lastDate, DateTime checkedAt)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.ExecuteAsync(@"
                INSERT INTO FinMindSyncLog (StockId, Dataset, LastDate, CheckedAt)
                VALUES (@StockId, @Dataset, @LastDate, @CheckedAt)
                ON CONFLICT(StockId, Dataset) DO UPDATE SET LastDate = excluded.LastDate, CheckedAt = excluded.CheckedAt;",
                new { StockId = stockId, Dataset = dataset, LastDate = lastDate, CheckedAt = checkedAt.ToString("yyyy-MM-dd HH:mm:ss") });
        }

        public async Task<Dictionary<string, string>> GetFinMindSyncedAsync(string dataset)
        {
            using var connection = new SqliteConnection(_connectionString);
            var rows = await connection.QueryAsync<(string StockId, string LastDate)>(
                "SELECT StockId, LastDate FROM FinMindSyncLog WHERE Dataset = @Dataset", new { Dataset = dataset });
            return rows.ToDictionary(r => r.StockId, r => r.LastDate);
        }

        public async Task<IEnumerable<InstitutionalDailyEntity>> GetByStockAsync(string stockId)
        {
            using var connection = new SqliteConnection(_connectionString);
            return await connection.QueryAsync<InstitutionalDailyEntity>(
                $"SELECT {AllColumns} FROM InstitutionalDaily WHERE StockId = @StockId ORDER BY TradeDate", new { StockId = stockId });
        }

        public async Task<(string? FirstDate, string? LastDate, int Days)> GetSummaryAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            var row = await connection.QuerySingleAsync<SummaryRow>(@"
                SELECT MIN(TradeDate) AS FirstDate, MAX(TradeDate) AS LastDate, COUNT(DISTINCT TradeDate) AS Days FROM InstitutionalDaily");
            return (row.FirstDate, row.LastDate, (int)row.Days);
        }

        private async Task<int> ExecuteInTransactionAsync(string sql, IEnumerable<InstitutionalDailyEntity> rows)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();   // 一天約 1,300 筆，包成一個交易才快
            int affected = await connection.ExecuteAsync(sql, rows, transaction);
            transaction.Commit();
            return affected;
        }

        private sealed class SummaryRow
        {
            public string? FirstDate { get; set; }
            public string? LastDate { get; set; }
            public long Days { get; set; }
        }
    }
}
