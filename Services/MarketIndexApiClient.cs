using System.Globalization;
using System.Text.Json;
using StockDashboard.Models.Entities;

namespace StockDashboard.Services
{
    public interface IMarketIndexApiClient
    {
        // 指數日線 (含 from、to 兩天)；code 是存進資料庫的代號，symbol 是來源的代號
        Task<List<MarketIndexEntity>> GetDailyAsync(string code, string symbol, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Yahoo Finance chart API (非官方、不需金鑰)：美股指數日線 OHLC。
    /// 必須用 period1/period2 指定區間；用 range=max 時 Yahoo 會自動降成月線。
    /// </summary>
    public class MarketIndexApiClient : IMarketIndexApiClient
    {
        public const string DefaultChartUrl = "https://query1.finance.yahoo.com/v8/finance/chart";
        public const string SoxCode = "SOX";          // 費城半導體指數：資料庫代號
        public const string SoxSymbol = "^SOX";       // 費城半導體指數：Yahoo 代號

        private readonly HttpClient _http;
        private readonly string _chartUrl;

        public MarketIndexApiClient(HttpClient http, IConfiguration? configuration = null)
        {
            _http = http;
            _chartUrl = (configuration?["MarketIndex:ChartUrl"] ?? DefaultChartUrl).TrimEnd('/');
        }

        public async Task<List<MarketIndexEntity>> GetDailyAsync(string code, string symbol, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            long period1 = new DateTimeOffset(DateTime.SpecifyKind(from.Date, DateTimeKind.Utc)).ToUnixTimeSeconds();
            long period2 = new DateTimeOffset(DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc)).ToUnixTimeSeconds();   // 含 to 當天
            string url = $"{_chartUrl}/{Uri.EscapeDataString(symbol)}?period1={period1}&period2={period2}&interval=1d";

            using var response = await _http.GetAsync(url, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseResponse(body, (int)response.StatusCode, code);
        }

        /// <summary>
        /// 解析 {"chart":{"result":[{"timestamp":[...],"indicators":{"quote":[{open,high,low,close,volume}]}}],"error":null}}。
        /// 日期取時間戳的 UTC 日期 (日線時間戳是美東 09:30 開盤 = 13:30 / 14:30 UTC，與美東日期同一天，不必處理夏令時間)；
        /// 開高低收任一為 null 的列略過；同一天多筆以最後一筆為準；由舊到新。錯誤一律丟 InvalidOperationException。
        /// </summary>
        public static List<MarketIndexEntity> ParseResponse(string body, int httpStatus, string code)
        {
            if (httpStatus == 429)
                throw new InvalidOperationException("Yahoo 回應請求過多 (HTTP 429)，請稍後再試");

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException($"Yahoo 回應不是 JSON (HTTP {httpStatus})");
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("chart", out var chart))
                    throw new InvalidOperationException($"Yahoo 回應格式不符 (HTTP {httpStatus})");

                if (chart.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    string? description = error.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                    throw new InvalidOperationException($"Yahoo 回應錯誤：{description ?? "未知原因"}");
                }
                if (httpStatus >= 400)
                    throw new InvalidOperationException($"Yahoo 回應錯誤 (HTTP {httpStatus})");

                if (!chart.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                    throw new InvalidOperationException("Yahoo 回應格式不符 (找不到 chart.result)");
                var result = results[0];

                // 區間內沒有交易日時 Yahoo 不給 timestamp：視為沒有資料
                if (!result.TryGetProperty("timestamp", out var timestamps) || timestamps.ValueKind != JsonValueKind.Array)
                    return new List<MarketIndexEntity>();
                if (!result.TryGetProperty("indicators", out var indicators) || !indicators.TryGetProperty("quote", out var quotes)
                    || quotes.ValueKind != JsonValueKind.Array || quotes.GetArrayLength() == 0)
                    throw new InvalidOperationException("Yahoo 回應格式不符 (找不到 indicators.quote)");
                var quote = quotes[0];

                var byDate = new SortedDictionary<string, MarketIndexEntity>(StringComparer.Ordinal);
                int i = 0;
                foreach (var ts in timestamps.EnumerateArray())
                {
                    int index = i++;
                    double? open = Value(quote, "open", index), high = Value(quote, "high", index);
                    double? low = Value(quote, "low", index), close = Value(quote, "close", index);
                    if (ts.ValueKind != JsonValueKind.Number || open == null || high == null || low == null || close == null) continue;

                    string date = DateTimeOffset.FromUnixTimeSeconds(ts.GetInt64()).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    byDate[date] = new MarketIndexEntity
                    {
                        Code = code, TradeDate = date, Open = Round(open.Value), High = Round(high.Value), Low = Round(low.Value), Close = Round(close.Value),
                        Volume = (long)(Value(quote, "volume", index) ?? 0)
                    };
                }
                return byDate.Values.ToList();
            }
        }

        // Yahoo 的價格是 32 位元浮點數，會帶長尾小數 (例如 5123.4501953125)，指數取小數 2 位
        private static double Round(double v) => Math.Round(v, 2);

        private static double? Value(JsonElement quote, string name, int index)
        {
            if (!quote.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array || index >= array.GetArrayLength()) return null;
            var item = array[index];
            return item.ValueKind == JsonValueKind.Number ? item.GetDouble() : null;
        }
    }
}
