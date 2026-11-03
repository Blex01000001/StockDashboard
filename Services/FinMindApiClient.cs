using System.Globalization;
using System.Text.Json;
using StockDashboard.Models.Entities;

namespace StockDashboard.Services
{
    public interface IFinMindApiClient
    {
        // 個股三大法人買賣超 (TaiwanStockInstitutionalInvestorsBuySell)，依日期合併成每日一列
        Task<List<InstitutionalDailyEntity>> GetInstitutionalTradingAsync(string stockId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

        // 個股外資持股 (TaiwanStockShareholding)
        Task<List<InstitutionalDailyEntity>> GetForeignHoldingAsync(string stockId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// FinMind 封鎖 IP (ip banned) 或請求次數達上限：必須立即停止，不可再發請求。
    /// </summary>
    public class FinMindBlockedException(string message) : Exception(message);

    /// <summary>
    /// FinMind API：一次請求回傳單一股票一段期間 (可長達十年以上) 的資料。不使用會員 Token (匿名額度)。
    /// </summary>
    public class FinMindApiClient : IFinMindApiClient
    {
        public const string DefaultDataUrl = "https://api.finmindtrade.com/api/v4/data";

        // 三大法人買賣超資料最早的日期 (使用者實測)
        public static readonly DateTime EarliestDate = new(2012, 5, 2);

        private readonly HttpClient _http;
        private readonly string _dataUrl;

        public FinMindApiClient(HttpClient http, IConfiguration? configuration = null)
        {
            _http = http;
            _dataUrl = configuration?["FinMind:DataUrl"] ?? DefaultDataUrl;
        }

        public async Task<List<InstitutionalDailyEntity>> GetInstitutionalTradingAsync(string stockId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            var data = await GetDataAsync("TaiwanStockInstitutionalInvestorsBuySell", stockId, from, to, cancellationToken);
            return ParseTrading(stockId, data);
        }

        public async Task<List<InstitutionalDailyEntity>> GetForeignHoldingAsync(string stockId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            var data = await GetDataAsync("TaiwanStockShareholding", stockId, from, to, cancellationToken);
            return data
                .Select(e => new InstitutionalDailyEntity
                {
                    StockId = stockId,
                    TradeDate = Str(e, "date") ?? string.Empty,
                    IssuedShares = Long(e, "NumberOfSharesIssued"),
                    ForeignShares = Long(e, "ForeignInvestmentShares"),
                    ForeignHoldingPct = Double(e, "ForeignInvestmentSharesRatio")
                })
                .Where(e => e.TradeDate.Length > 0)
                .ToList();
        }

        /// <summary>
        /// 每天有多列 (每種法人一列，buy / sell 股數) → 合併成一列買賣超。
        /// 分類歷年有調整 (見 FinMind 說明)：
        /// - 自營商：2014-12-01 前只有合併的 Dealer，之後拆成 Dealer_self + Dealer_Hedging → 三者相加，任一年代只有一組有值
        /// - 外資自營商 Foreign_Dealer_Self：2017-12-18 起才有；在此之前 Foreign_Investor 已含外資自營商 → 欄位留 null
        /// </summary>
        public static List<InstitutionalDailyEntity> ParseTrading(string stockId, IEnumerable<JsonElement> data)
        {
            var result = new List<InstitutionalDailyEntity>();
            foreach (var day in data.GroupBy(e => Str(e, "date") ?? string.Empty).Where(g => g.Key.Length > 0).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                long? Net(params string[] names)
                {
                    long? sum = null;
                    foreach (var e in day)
                    {
                        string? name = Str(e, "name");
                        if (name == null || !names.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                        sum = (sum ?? 0) + (Long(e, "buy") ?? 0) - (Long(e, "sell") ?? 0);
                    }
                    return sum;
                }

                var row = new InstitutionalDailyEntity
                {
                    StockId = stockId,
                    TradeDate = day.Key,
                    ForeignNet = Net("Foreign_Investor"),
                    ForeignDealerNet = Net("Foreign_Dealer_Self"),
                    TrustNet = Net("Investment_Trust"),
                    DealerNet = Net("Dealer", "Dealer_self", "Dealer_Hedging")
                };
                var parts = new[] { row.ForeignNet, row.ForeignDealerNet, row.TrustNet, row.DealerNet };
                if (parts.All(p => p == null)) continue;   // 都是不認得的分類
                row.TotalNet = parts.Sum(p => p ?? 0);
                result.Add(row);
            }
            return result;
        }

        private async Task<List<JsonElement>> GetDataAsync(string dataset, string stockId, DateTime from, DateTime to, CancellationToken cancellationToken)
        {
            string url = $"{_dataUrl}?dataset={dataset}&data_id={Uri.EscapeDataString(stockId)}&start_date={from:yyyy-MM-dd}&end_date={to:yyyy-MM-dd}";
            using var response = await _http.GetAsync(url, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseResponse(body, (int)response.StatusCode);
        }

        /// <summary>
        /// 解析 {"msg","status","data"}。封鎖 / 達上限 → FinMindBlockedException；其他錯誤 → InvalidOperationException。
        /// </summary>
        public static List<JsonElement> ParseResponse(string body, int httpStatus)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                string head = body.Length > 60 ? body[..60] : body;
                throw new InvalidOperationException($"FinMind 回傳的不是 JSON 資料（HTTP {httpStatus}）。回應開頭：{head.ReplaceLineEndings(" ")}");
            }

            using (doc)
            {
                var root = doc.RootElement;
                int status = root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : httpStatus;
                string msg = root.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";

                if (status == 403 || httpStatus == 403 || msg.Contains("ban", StringComparison.OrdinalIgnoreCase))
                    throw new FinMindBlockedException($"FinMind 已封鎖此 IP（{msg}），已停止更新。請隔一段時間再試，並減少一次同步的股票數量");
                if (status == 402 || httpStatus == 402)
                    throw new FinMindBlockedException($"FinMind 請求次數已達上限（{msg}），已停止更新。請約一小時後再試");
                if (status != 200 || httpStatus is < 200 or >= 300)
                    throw new InvalidOperationException($"FinMind 回應錯誤（status {status}）：{msg}");

                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return new List<JsonElement>();
                return data.EnumerateArray().Select(e => e.Clone()).ToList();
            }
        }

        private static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static long? Long(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt64(out long l) ? l : (long)Math.Round(v.GetDouble());
            return v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long p) ? p : null;
        }

        private static double? Double(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            return v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double p) ? p : null;
        }
    }
}
