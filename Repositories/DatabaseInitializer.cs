using Dapper;
using Microsoft.Data.Sqlite;

namespace StockDashboard.Repositories
{
    /// <summary>
    /// 程式啟動時確保資料表存在 (CREATE TABLE IF NOT EXISTS)。
    /// 已存在的資料表與資料完全不會被修改；全新環境則會自動建立空的資料表。
    /// </summary>
    public static class DatabaseInitializer
    {
        private const string CreateTablesSql = @"
            CREATE TABLE IF NOT EXISTS StockDailyPrice (
                StockId     TEXT NOT NULL,
                TradeDate   TEXT NOT NULL,      -- yyyy-MM-dd
                Volume      INTEGER,            -- 成交股數
                Amount      INTEGER,            -- 成交金額
                OpenPrice   REAL,
                HighPrice   REAL,
                LowPrice    REAL,
                ClosePrice  REAL,
                PriceChange REAL,               -- 漲跌價差
                TradeCount  INTEGER,            -- 成交筆數
                Note        TEXT,
                PRIMARY KEY (StockId, TradeDate)
            );

            CREATE TABLE IF NOT EXISTS StockList (
                StockId   TEXT NOT NULL PRIMARY KEY,
                StockName TEXT NOT NULL,
                Market    TEXT NOT NULL DEFAULT 'TWSE',  -- TWSE = 上市；預留 TPEx = 上櫃
                UpdatedAt TEXT NOT NULL                  -- yyyy-MM-dd HH:mm:ss
            );";

        public static async Task InitializeAsync(IConfiguration configuration, ILogger logger)
        {
            string? connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                logger.LogError("appsettings.json 中找不到 ConnectionStrings:DefaultConnection，資料庫功能將無法使用。");
                return;
            }

            try
            {
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                await connection.ExecuteAsync(CreateTablesSql);

                // 印出實際使用的資料庫檔案，方便確認連到的是哪一個 stock.db
                logger.LogInformation("SQLite 資料庫：{DataSource}", connection.DataSource);

                // CREATE TABLE IF NOT EXISTS 遇到「同名但結構不同」的舊表會直接略過，因此要另外檢查欄位
                string? problem = await GetStockListSchemaProblemAsync(connection);
                if (problem != null)
                {
                    logger.LogWarning("{Problem}", problem);
                }
            }
            catch (Exception ex)
            {
                // 資料庫有問題時仍讓網站啟動，錯誤會顯示在各頁面的訊息中
                logger.LogError(ex, "初始化 SQLite 資料庫失敗");
            }
        }

        // StockList 資料表必須具備的欄位
        private static readonly string[] RequiredStockListColumns = { "StockId", "StockName", "Market", "UpdatedAt" };

        /// <summary>
        /// 檢查 StockList 資料表的欄位是否符合程式需求。
        /// 符合時回傳 null；不符合時回傳說明文字 (含資料庫中實際的欄位名稱)。
        /// </summary>
        public static async Task<string?> GetStockListSchemaProblemAsync(SqliteConnection connection)
        {
            var columns = (await connection.QueryAsync<string>("SELECT name FROM pragma_table_info('StockList')")).ToList();
            var missing = RequiredStockListColumns
                .Where(required => !columns.Contains(required, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (missing.Count == 0) return null;

            return "資料庫中已存在一個結構不同的 StockList 資料表，股票清單功能暫停使用。" +
                   $"現有欄位：{string.Join(", ", columns)}；缺少：{string.Join(", ", missing)}。";
        }
    }
}
