using Dapper;
using Microsoft.Data.Sqlite;
using StockDashboard.Models;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Repositories.Implements
{
    /// <summary>
    /// 股票名稱的存取入口，合併兩個來源 (說明見 IStockNameRepository)。
    /// </summary>
    public class StockNameRepository : IStockNameRepository, IIndustryRepository
    {
        // 使用者既有的「上市公司基本資料」表：只讀取，絕不寫入
        private const string CompanyTable = "StockList";

        // 「更新股票清單」寫入的表 (含 ETF)
        private const string SyncedTable = "StockNameList";

        // StockList 中要讀取的中文欄位名稱 (比對時忽略空白)
        private const string CompanyCodeColumn = "公司代號";
        private const string CompanyNameColumn = "公司簡稱";
        private const string IndustryColumn = "產業別";

        private readonly string _connectionString;

        public StockNameRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration), "系統設定中遺失 DefaultConnection 連線字串。");
        }

        public async Task<IEnumerable<StockNameEntity>> GetAllStocksAsync()
        {
            using var connection = new SqliteConnection(_connectionString);

            var synced = await connection.QueryAsync<StockNameEntity>(
                $"SELECT StockId, StockName, Market, UpdatedAt FROM {SyncedTable}");

            var companySql = await BuildCompanySelectAsync(connection);
            var companies = companySql == null
                ? Enumerable.Empty<StockNameEntity>()
                : await connection.QueryAsync<StockNameEntity>(companySql);

            return Merge(synced, companies).Values.OrderBy(s => s.StockId, StringComparer.Ordinal);
        }

        public async Task<string?> GetStockNameAsync(string stockId)
        {
            var names = await GetStockNamesAsync(new[] { stockId });
            return names.TryGetValue(stockId, out var name) ? name : null;
        }

        public async Task<Dictionary<string, string>> GetStockNamesAsync(IEnumerable<string> stockIds)
        {
            var ids = stockIds.ToList();
            using var connection = new SqliteConnection(_connectionString);

            var synced = await connection.QueryAsync<StockNameEntity>(
                $"SELECT StockId, StockName FROM {SyncedTable} WHERE StockId IN @Ids",
                new { Ids = ids });

            var companySql = await BuildCompanySelectAsync(connection);
            var companies = companySql == null
                ? Enumerable.Empty<StockNameEntity>()
                : await connection.QueryAsync<StockNameEntity>(
                    $"SELECT * FROM ({companySql}) WHERE StockId IN @Ids",
                    new { Ids = ids });

            return Merge(synced, companies).ToDictionary(kv => kv.Key, kv => kv.Value.StockName);
        }

        public async Task<StockNameSummary> GetSummaryAsync()
        {
            using var connection = new SqliteConnection(_connectionString);

            var summary = await connection.QuerySingleAsync<StockNameSummary>(
                $"SELECT COUNT(*) AS SyncedCount, MAX(UpdatedAt) AS LastSyncedAt FROM {SyncedTable}");

            var companySql = await BuildCompanySelectAsync(connection);
            if (companySql != null)
            {
                summary.CompanyCount = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM ({companySql})");
            }

            return summary;
        }

        public async Task<int> UpsertStocksAsync(IEnumerable<StockNameEntity> stocks)
        {
            // ON CONFLICT ... DO UPDATE：代號已存在就更新名稱 (公司改名時)，不存在就新增
            string sql = $@"
                INSERT INTO {SyncedTable} (StockId, StockName, Market, UpdatedAt)
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

        public async Task<List<IndustryOption>> GetIndustriesAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            var columns = (await connection.QueryAsync<string>($"SELECT name FROM pragma_table_info('{CompanyTable}')")).ToList();
            string? Find(string target) => columns.FirstOrDefault(c => RemoveWhitespace(c) == target);
            string? codeColumn = Find(CompanyCodeColumn), industryColumn = Find(IndustryColumn);
            var options = new List<IndustryOption>();
            if (codeColumn != null && industryColumn != null) options = await ReadIndustriesAsync(connection, codeColumn, industryColumn);

            // ETF：StockList 不含 ETF，另外從同步來的清單找出
            int etfCount = (await GetEtfIdsAsync(connection)).Count;
            if (etfCount > 0) options.Add(new IndustryOption(IndustryNames.EtfCode, "ETF", etfCount));
            return options;
        }

        private static async Task<List<IndustryOption>> ReadIndustriesAsync(SqliteConnection connection, string codeColumn, string industryColumn)
        {
            var rows = await connection.QueryAsync<(string Industry, long Count)>($@"
                SELECT TRIM(CAST({Quote(industryColumn)} AS TEXT)) AS Industry, COUNT(DISTINCT TRIM(CAST({Quote(codeColumn)} AS TEXT))) AS Count
                FROM {CompanyTable}
                WHERE TRIM(IFNULL(CAST({Quote(industryColumn)} AS TEXT), '')) <> ''
                  AND TRIM(IFNULL(CAST({Quote(codeColumn)} AS TEXT), '')) <> ''
                GROUP BY TRIM(CAST({Quote(industryColumn)} AS TEXT))");

            // "1" 與 "01" 視為同一個產業
            return rows.GroupBy(r => IndustryNames.Normalize(r.Industry))
                .Where(g => g.Key.Length > 0)
                .Select(g => new IndustryOption(g.Key, IndustryNames.NameOf(g.Key), (int)g.Sum(r => r.Count)))
                .OrderBy(o => o.Code, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// ETF 代號：StockNameList (證交所同步，含 ETF) 中代號以 00 開頭者 (使用者指定的判斷依據)。
        /// 還沒按過「更新股票清單」時沒有資料，回傳空清單。
        /// </summary>
        private static async Task<List<string>> GetEtfIdsAsync(SqliteConnection connection) =>
            (await connection.QueryAsync<string>($"SELECT StockId FROM {SyncedTable} WHERE StockId LIKE '00%'"))
                .OrderBy(id => id, StringComparer.Ordinal).ToList();

        public async Task<List<string>> GetStockIdsByIndustryAsync(string industryCode)
        {
            string code = (industryCode ?? string.Empty).Trim().TrimStart('0');
            if (code.Length == 0) return new List<string>();

            using var connection = new SqliteConnection(_connectionString);
            if (code.Equals(IndustryNames.EtfCode, StringComparison.OrdinalIgnoreCase)) return await GetEtfIdsAsync(connection);
            var columns = (await connection.QueryAsync<string>($"SELECT name FROM pragma_table_info('{CompanyTable}')")).ToList();
            string? Find(string target) => columns.FirstOrDefault(c => RemoveWhitespace(c) == target);
            string? codeColumn = Find(CompanyCodeColumn), industryColumn = Find(IndustryColumn);
            if (codeColumn == null || industryColumn == null) return new List<string>();

            // 產業別可能被匯入成數字或文字 (01 與 1)：轉成文字、去空白與前導 0 再比對
            var ids = await connection.QueryAsync<string>($@"
                SELECT DISTINCT TRIM(CAST({Quote(codeColumn)} AS TEXT))
                FROM {CompanyTable}
                WHERE LTRIM(TRIM(CAST({Quote(industryColumn)} AS TEXT)), '0') = @Code
                  AND TRIM(IFNULL(CAST({Quote(codeColumn)} AS TEXT), '')) <> ''", new { Code = code });
            return ids.OrderBy(i => i, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// 產生「從 StockList 讀出 StockId / StockName」的 SELECT 語法。
        /// StockList 不存在、或找不到「公司代號 / 公司簡稱」欄位時回傳 null (只使用 StockNameList)。
        /// </summary>
        private static async Task<string?> BuildCompanySelectAsync(SqliteConnection connection)
        {
            var columns = (await connection.QueryAsync<string>(
                $"SELECT name FROM pragma_table_info('{CompanyTable}')")).ToList();

            // 欄位名稱比對時忽略空白 (例如 "公司 簡稱" 也視為 "公司簡稱")
            string? Find(string target) => columns.FirstOrDefault(c => RemoveWhitespace(c) == target);

            string? codeColumn = Find(CompanyCodeColumn);
            string? nameColumn = Find(CompanyNameColumn);
            if (codeColumn == null || nameColumn == null) return null;

            // 公司代號可能被匯入成數字型別，一律轉成文字並去除前後空白
            return $@"SELECT TRIM(CAST({Quote(codeColumn)} AS TEXT)) AS StockId,
                             TRIM({Quote(nameColumn)}) AS StockName,
                             'TWSE' AS Market
                      FROM {CompanyTable}
                      WHERE TRIM(IFNULL({Quote(nameColumn)}, '')) <> ''";
        }

        /// <summary>
        /// 合併兩個來源：先放同步清單，再以公司基本資料的簡稱覆蓋
        /// </summary>
        private static Dictionary<string, StockNameEntity> Merge(IEnumerable<StockNameEntity> synced, IEnumerable<StockNameEntity> companies)
        {
            var result = new Dictionary<string, StockNameEntity>();
            foreach (var s in synced.Concat(companies))
            {
                if (string.IsNullOrWhiteSpace(s.StockId) || string.IsNullOrWhiteSpace(s.StockName)) continue;
                result[s.StockId] = s;
            }
            return result;
        }

        private static string RemoveWhitespace(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

        // SQLite 識別字 (欄位名) 以雙引號包住，內含的雙引號要重複一次
        private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
