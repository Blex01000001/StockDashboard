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

            -- 證交所同步的股票代號清單 (含 ETF)。
            -- 注意：StockList 是使用者既有的「上市公司基本資料」表 (中文欄位)，本程式只讀取、不建立也不修改。
            -- 個股每日三大法人 (T86 買賣超 + MI_QFIIS 外資持股)，單位：股
            CREATE TABLE IF NOT EXISTS InstitutionalDaily (
                StockId           TEXT NOT NULL,
                TradeDate         TEXT NOT NULL,      -- yyyy-MM-dd
                ForeignNet        INTEGER,            -- 外陸資買賣超 (不含外資自營商)
                ForeignDealerNet  INTEGER,            -- 外資自營商買賣超
                TrustNet          INTEGER,            -- 投信買賣超
                DealerNet         INTEGER,            -- 自營商買賣超
                TotalNet          INTEGER,            -- 三大法人買賣超
                IssuedShares      INTEGER,            -- 發行股數
                ForeignShares     INTEGER,            -- 外資及陸資持有股數
                ForeignHoldingPct REAL,               -- 外資及陸資持股比率 (%)
                PRIMARY KEY (StockId, TradeDate)
            );

            -- 三大法人同步紀錄：哪一天、哪個來源已查過 (含非交易日)，避免重複請求證交所
            CREATE TABLE IF NOT EXISTS InstitutionalSyncLog (
                TradeDate TEXT NOT NULL,              -- yyyy-MM-dd
                Source    TEXT NOT NULL,              -- T86 / MI_QFIIS
                HasData   INTEGER NOT NULL,           -- 1 = 有資料，0 = 當天無資料 (假日或尚未公布)
                CheckedAt TEXT NOT NULL,              -- yyyy-MM-dd HH:mm:ss
                PRIMARY KEY (TradeDate, Source)
            );

            CREATE TABLE IF NOT EXISTS StockNameList (
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
            }
            catch (Exception ex)
            {
                // 資料庫有問題時仍讓網站啟動，錯誤會顯示在各頁面的訊息中
                logger.LogError(ex, "初始化 SQLite 資料庫失敗");
            }
        }
    }
}
