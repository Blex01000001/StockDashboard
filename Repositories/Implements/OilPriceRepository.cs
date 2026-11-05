using Dapper;
using Microsoft.Data.Sqlite;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Repositories.Implements
{
    /// <summary>OilPriceDaily (每日原油價格) 的存取入口。</summary>
    public class OilPriceRepository : IOilPriceRepository
    {
        private readonly string _connectionString;

        public OilPriceRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration), "系統設定中遺失 DefaultConnection 連線字串。");
        }

        public async Task<int> UpsertAsync(IEnumerable<OilPriceEntity> rows)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            int affected = await connection.ExecuteAsync(@"
                INSERT INTO OilPriceDaily (Code, TradeDate, Price) VALUES (@Code, @TradeDate, @Price)
                ON CONFLICT(Code, TradeDate) DO UPDATE SET Price = excluded.Price;", rows, transaction);
            transaction.Commit();
            return affected;
        }

        public async Task<IEnumerable<OilPriceEntity>> GetAllAsync(string code)
        {
            using var connection = new SqliteConnection(_connectionString);
            return await connection.QueryAsync<OilPriceEntity>(
                "SELECT Code, TradeDate, Price FROM OilPriceDaily WHERE Code = @Code ORDER BY TradeDate", new { Code = code });
        }

        public async Task<(string? FirstDate, string? LastDate, int Days)> GetSummaryAsync(string code)
        {
            using var connection = new SqliteConnection(_connectionString);
            var row = await connection.QuerySingleAsync<SummaryRow>(
                "SELECT MIN(TradeDate) AS FirstDate, MAX(TradeDate) AS LastDate, COUNT(*) AS Days FROM OilPriceDaily WHERE Code = @Code", new { Code = code });
            return (row.FirstDate, row.LastDate, (int)row.Days);
        }

        private sealed class SummaryRow
        {
            public string? FirstDate { get; set; }
            public string? LastDate { get; set; }
            public long Days { get; set; }
        }
    }
}
