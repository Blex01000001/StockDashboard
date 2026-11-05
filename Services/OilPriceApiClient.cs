using System.Globalization;
using System.Text.Json;
using StockDashboard.Models.Entities;

namespace StockDashboard.Services
{
    public interface IOilPriceApiClient
    {
        // 最近一年每日原油價格 (past_year，每天一筆當日均價)
        Task<List<OilPriceEntity>> GetPastYearAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// oilpriceapi.com：Brent 原油。past_year 只回最近一年，且是「當日均價」(daily_average)；
    /// 免費方案每天 50 次請求。API Key 來自設定 OilPrice:ApiKey，不寫在程式裡。
    /// </summary>
    public class OilPriceApiClient : IOilPriceApiClient
    {
        public const string DefaultPastYearUrl = "https://api.oilpriceapi.com/v1/prices/past_year";
        public const string BrentCode = "BRENT_CRUDE_USD";

        private readonly HttpClient _http;
        private readonly string _url;
        private readonly string? _apiKey;

        public OilPriceApiClient(HttpClient http, IConfiguration? configuration = null)
        {
            _http = http;
            _url = configuration?["OilPrice:PastYearUrl"] ?? DefaultPastYearUrl;
            _apiKey = configuration?["OilPrice:ApiKey"];
        }

        public async Task<List<OilPriceEntity>> GetPastYearAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("尚未設定 OilPrice:ApiKey（請在 appsettings.json 加入 oilpriceapi.com 的 API Key）");

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_url}?by_code={BrentCode}");
            request.Headers.TryAddWithoutValidation("Authorization", $"Token {_apiKey.Trim()}");
            using var response = await _http.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseResponse(body, (int)response.StatusCode);
        }

        /// <summary>
        /// 解析 {"status":"success","data":{"prices":[...]}}：只取 type = daily_average 的列，日期取 created_at 的 UTC 日期，
        /// 同一天多筆以最後一筆為準；由舊到新。錯誤一律丟 InvalidOperationException (訊息不含 API Key)。
        /// </summary>
        public static List<OilPriceEntity> ParseResponse(string body, int httpStatus)
        {
            if (httpStatus == 401 || httpStatus == 403)
                throw new InvalidOperationException($"API Key 無效或沒有權限 (HTTP {httpStatus})，請檢查 OilPrice:ApiKey");
            if (httpStatus == 429)
                throw new InvalidOperationException("已達 oilpriceapi 請求上限 (免費方案每天 50 次)，請明天再試");

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException($"oilpriceapi 回應不是 JSON (HTTP {httpStatus})");
            }

            using (doc)
            {
                var root = doc.RootElement;
                string? status = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("status", out var s) ? s.GetString() : null;
                if (httpStatus >= 400 || status != "success")
                    throw new InvalidOperationException($"oilpriceapi 回應錯誤 (HTTP {httpStatus}{(status != null ? $"，{status}" : "")})");
                if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("prices", out var prices) || prices.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("oilpriceapi 回應格式不符 (找不到 data.prices)");

                var byDate = new SortedDictionary<string, OilPriceEntity>(StringComparer.Ordinal);
                foreach (var item in prices.EnumerateArray())
                {
                    if (Str(item, "type") != "daily_average") continue;
                    string? created = Str(item, "created_at");
                    string? code = Str(item, "code");
                    if (created == null || created.Length < 10 || code == null) continue;
                    string date = created[..10];
                    if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) continue;
                    if (!item.TryGetProperty("price", out var p) || p.ValueKind != JsonValueKind.Number) continue;
                    byDate[date] = new OilPriceEntity { Code = code, TradeDate = date, Price = p.GetDouble() };
                }
                return byDate.Values.ToList();
            }
        }

        private static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
