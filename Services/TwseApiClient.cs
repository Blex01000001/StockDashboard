using System.Globalization;
using System.Text.Json;
using StockDashboard.Models;
using StockDashboard.Models.Entities;

namespace StockDashboard.Services
{
    /// <summary>
    /// 證交所 STOCK_DAY API：個股日成交資訊 (一次回傳一整個月)
    /// </summary>
    public class TwseApiClient : ITwseApiClient
    {
        // 預設網址；證交所改版時可在 appsettings.json 的 "Twse" 區段覆寫，不必改程式
        // 證交所網站「個股日成交資訊」：一次回傳一檔股票一個月
        public const string DefaultStockDayUrl = "https://www.twse.com.tw/exchangeReport/STOCK_DAY";

        // 證交所 OpenAPI「上市個股日成交資訊」：一次回傳最近一個交易日的所有上市證券
        public const string DefaultStockDayAllUrl = "https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL";

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _http;
        private readonly string _stockDayUrl;
        private readonly string _stockDayAllUrl;

        // HttpClient 由 Program.cs 的 AddHttpClient<ITwseApiClient, TwseApiClient>() 注入
        public TwseApiClient(HttpClient http, IConfiguration? configuration = null)
        {
            _http = http;
            _stockDayUrl = configuration?["Twse:StockDayUrl"] ?? DefaultStockDayUrl;
            _stockDayAllUrl = configuration?["Twse:StockDayAllUrl"] ?? DefaultStockDayAllUrl;
        }

        public async Task<List<StockDailyPriceEntity>> GetMonthlyPricesAsync(string stockId, DateTime month, CancellationToken cancellationToken = default)
        {
            string queryDateStr = month.ToString("yyyyMM01");
            string url = $"{_stockDayUrl}?response=json&date={queryDateStr}&stockNo={Uri.EscapeDataString(stockId)}";

            var response = await GetJsonAsync<TwseStockDayResponse>(url, cancellationToken);

            // 證交所回傳 "很抱歉，沒有符合條件的資料!" 時 Stat 不是 "OK"，或 Data 為空
            if (response == null || response.Stat != "OK" || response.Data == null || response.Data.Count == 0)
            {
                return new List<StockDailyPriceEntity>();
            }

            return ParseRows(stockId, response.Data);
        }

        public async Task<List<StockNameEntity>> GetListedStocksAsync(CancellationToken cancellationToken = default)
        {
            var items = await GetJsonAsync<List<TwseStockDayAllItem>>(_stockDayAllUrl, cancellationToken)
                ?? new List<TwseStockDayAllItem>();

            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            return items
                .Where(i => !string.IsNullOrWhiteSpace(i.Code) && !string.IsNullOrWhiteSpace(i.Name))
                .GroupBy(i => i.Code.Trim())          // 防止重複代號造成主鍵衝突
                .Select(g => new StockNameEntity
                {
                    StockId = g.Key,
                    StockName = g.First().Name.Trim(),
                    Market = "TWSE",
                    UpdatedAt = now
                })
                .ToList();
        }

        /// <summary>
        /// 下載並解析 JSON。失敗時拋出「看得懂原因」的例外訊息，方便在畫面上直接顯示。
        /// </summary>
        private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"證交所回應 HTTP {(int)response.StatusCode} {response.StatusCode}" +
                    ((int)response.StatusCode is 403 or 429 ? "（可能因請求過於頻繁被暫時封鎖，請稍候再試）" : ""));
            }

            try
            {
                return JsonSerializer.Deserialize<T>(body, JsonOptions);
            }
            catch (JsonException)
            {
                // 被封鎖或網址改版時，證交所常回傳 HTML 網頁而不是 JSON
                string head = body.Length > 60 ? body[..60] : body;
                throw new InvalidOperationException(
                    $"證交所回傳的不是 JSON 資料（可能被暫時封鎖或網址已變更）。回應開頭：{head.ReplaceLineEndings(" ")}");
            }
        }

        /// <summary>
        /// 將證交所原始文字陣列清洗為標準型態
        /// </summary>
        private static List<StockDailyPriceEntity> ParseRows(string stockId, List<List<string>> rawData)
        {
            var entities = new List<StockDailyPriceEntity>();

            foreach (var row in rawData)
            {
                // 欄位順序: 0:日期, 1:成交股數, 2:成交金額, 3:開盤價, 4:最高價, 5:最低價, 6:收盤價, 7:漲跌價差, 8:成交筆數
                if (row.Count < 7) continue;

                string? tradeDate = ConvertRocDate(row[0]);
                if (tradeDate == null) continue; // 日期格式異常則跳過

                entities.Add(new StockDailyPriceEntity
                {
                    StockId = stockId,
                    TradeDate = tradeDate,
                    Volume = ParseLong(row[1]),
                    Amount = ParseLong(row[2]),
                    OpenPrice = ParseDouble(row[3]),
                    HighPrice = ParseDouble(row[4]),
                    LowPrice = ParseDouble(row[5]),
                    ClosePrice = ParseDouble(row[6]),
                    // 漲跌價差可能是 "+5.00"、"-3.50"，除權息日會出現 "X0.00" 這類字樣 → 解析失敗則存 null
                    PriceChange = row.Count > 7 ? ParseDouble(row[7]) : null,
                    TradeCount = row.Count > 8 ? ParseInt(row[8]) : null
                });
            }

            return entities;
        }

        /// <summary>
        /// 民國日期轉西元：例如 "115/05/04" → "2026-05-04"；格式不符回傳 null
        /// </summary>
        private static string? ConvertRocDate(string rawDate)
        {
            var dateParts = rawDate.Split('/');
            if (dateParts.Length != 3 || !int.TryParse(dateParts[0], out int rocYear))
            {
                return null;
            }

            int adYear = rocYear + 1911;
            return $"{adYear}-{dateParts[1]}-{dateParts[2]}";
        }

        // 移除千分號 (,) 後再轉型；用 InvariantCulture 避免因電腦地區設定不同而解析錯誤
        private static long? ParseLong(string input) =>
            long.TryParse(input.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out long val) ? val : null;

        private static int? ParseInt(string input) =>
            int.TryParse(input.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out int val) ? val : null;

        private static double? ParseDouble(string input) =>
            double.TryParse(input.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double val) ? val : null;
    }
}
