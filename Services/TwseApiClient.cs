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

        // 證交所網站「三大法人買賣超日報」與「外資及陸資投資持股統計」：一次回傳某一天的所有上市股票
        public const string DefaultT86Url = "https://www.twse.com.tw/fund/T86";
        public const string DefaultQfiisUrl = "https://www.twse.com.tw/fund/MI_QFIIS";

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _http;
        private readonly string _stockDayUrl;
        private readonly string _stockDayAllUrl;
        private readonly string _t86Url;
        private readonly string _qfiisUrl;

        // HttpClient 由 Program.cs 的 AddHttpClient<ITwseApiClient, TwseApiClient>() 注入
        public TwseApiClient(HttpClient http, IConfiguration? configuration = null)
        {
            _http = http;
            _stockDayUrl = configuration?["Twse:StockDayUrl"] ?? DefaultStockDayUrl;
            _stockDayAllUrl = configuration?["Twse:StockDayAllUrl"] ?? DefaultStockDayAllUrl;
            _t86Url = configuration?["Twse:T86Url"] ?? DefaultT86Url;
            _qfiisUrl = configuration?["Twse:QfiisUrl"] ?? DefaultQfiisUrl;
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

        public async Task<List<InstitutionalDailyEntity>> GetInstitutionalTradingAsync(DateTime date, CancellationToken cancellationToken = default)
        {
            var table = await GetFieldTableAsync(_t86Url, date, cancellationToken);
            if (table == null) return new List<InstitutionalDailyEntity>();

            // 欄位依名稱尋找 (證交所曾改版：2014 年自營商拆成自行買賣 / 避險，2017 年外資拆出外資自營商)
            int code = table.Find("證券代號");
            int foreign = table.Find("外陸資買賣超股數(不含外資自營商)", "外資買賣超股數");
            int foreignDealer = table.Find("外資自營商買賣超股數");
            int trust = table.Find("投信買賣超股數");
            int dealer = table.Find("自營商買賣超股數");
            int dealerSelf = table.Find("自營商買賣超股數(自行買賣)");
            int dealerHedge = table.Find("自營商買賣超股數(避險)");
            int total = table.Find("三大法人買賣超股數");
            if (code < 0 || total < 0) throw new InvalidOperationException("三大法人買賣超日報的欄位格式無法辨識");

            string tradeDate = date.ToString("yyyy-MM-dd");
            return table.Response.Data.Select(row => new InstitutionalDailyEntity
            {
                StockId = Cell(row, code)?.Trim() ?? string.Empty,
                TradeDate = tradeDate,
                ForeignNet = LongCell(row, foreign),
                ForeignDealerNet = LongCell(row, foreignDealer),
                TrustNet = LongCell(row, trust),
                // 舊格式沒有自營商合計欄位時，以自行買賣 + 避險加總
                DealerNet = dealer >= 0 ? LongCell(row, dealer)
                          : dealerSelf >= 0 || dealerHedge >= 0 ? (LongCell(row, dealerSelf) ?? 0) + (LongCell(row, dealerHedge) ?? 0) : null,
                TotalNet = LongCell(row, total)
            }).Where(e => e.StockId.Length > 0).ToList();
        }

        public async Task<List<InstitutionalDailyEntity>> GetForeignHoldingAsync(DateTime date, CancellationToken cancellationToken = default)
        {
            var table = await GetFieldTableAsync(_qfiisUrl, date, cancellationToken);
            if (table == null) return new List<InstitutionalDailyEntity>();

            int code = table.Find("證券代號");
            int issued = table.Find("發行股數");
            int held = table.Find("全體外資及陸資持有股數");
            int pct = table.Find("全體外資及陸資持股比率");
            if (code < 0 || pct < 0) throw new InvalidOperationException("外資持股統計的欄位格式無法辨識");

            string tradeDate = date.ToString("yyyy-MM-dd");
            return table.Response.Data.Select(row => new InstitutionalDailyEntity
            {
                StockId = Cell(row, code)?.Trim() ?? string.Empty,
                TradeDate = tradeDate,
                IssuedShares = LongCell(row, issued),
                ForeignShares = LongCell(row, held),
                ForeignHoldingPct = DoubleCell(row, pct)
            }).Where(e => e.StockId.Length > 0).ToList();
        }

        // 下載某一天的 fields + data 表格；非交易日 (stat 不是 OK) 回傳 null
        private async Task<FieldTable?> GetFieldTableAsync(string baseUrl, DateTime date, CancellationToken cancellationToken)
        {
            string url = $"{baseUrl}?response=json&date={date:yyyyMMdd}&selectType=ALLBUT0999";
            var response = await GetJsonAsync<TwseFieldTableResponse>(url, cancellationToken);
            if (response == null || response.Stat != "OK" || response.Data == null || response.Data.Count == 0) return null;
            return new FieldTable(response);
        }

        private sealed class FieldTable
        {
            public TwseFieldTableResponse Response { get; }
            private readonly List<string> _normalized;

            public FieldTable(TwseFieldTableResponse response)
            {
                Response = response;
                _normalized = response.Fields.Select(f => string.Concat(f.Where(c => !char.IsWhiteSpace(c)))).ToList();
            }

            // 依序嘗試多個可能的欄位名稱 (完全相符，忽略空白)；找不到回傳 -1
            public int Find(params string[] names)
            {
                foreach (var name in names)
                {
                    int i = _normalized.IndexOf(name);
                    if (i >= 0) return i;
                }
                return -1;
            }
        }

        private static string? Cell(List<JsonElement> row, int index)
        {
            if (index < 0 || index >= row.Count) return null;
            var e = row[index];
            return e.ValueKind == JsonValueKind.String ? e.GetString() : e.ValueKind == JsonValueKind.Number ? e.GetRawText() : null;
        }

        private static long? LongCell(List<JsonElement> row, int index) => Cell(row, index) is string s ? ParseLong(s) : null;
        private static double? DoubleCell(List<JsonElement> row, int index) => Cell(row, index) is string s ? ParseDouble(s) : null;

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
