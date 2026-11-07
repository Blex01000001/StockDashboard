using StockDashboard.Models;
using StockDashboard.Models.Entities;

namespace StockDashboard.Services
{
    /// <summary>
    /// 還原股價（比例還原）純計算，不碰資料庫與網路。
    /// 每個除權息事件的係數 = 參考價 / 前收盤；除權息日「之前」的價格乘上之後所有事件係數的連乘積。
    /// 除權息日當天與之後的價格不變，所以最新的價格永遠等於原始價。
    /// 只調整價格 (開高低收與漲跌價差)，成交量、金額不動。
    /// </summary>
    public static class PriceAdjuster
    {
        /// <summary>回傳還原後的新清單 (順序同輸入)；不修改輸入物件。</summary>
        public static List<StockDailyPriceEntity> Apply(
            IEnumerable<StockDailyPriceEntity> prices,
            IEnumerable<StockCorporateActionEntity> actions)
        {
            var factors = BuildFactors(actions);

            var result = new List<StockDailyPriceEntity>();
            foreach (var price in prices)
            {
                double factor = FactorFor(factors, price.TradeDate);
                result.Add(factor == 1.0 ? Clone(price) : Scale(price, factor));
            }
            return result;
        }

        /// <summary>K 線 (型態掃描、技術圖表用) 的還原，規則同上；成交量不動。</summary>
        public static List<StockDataPoint> ApplyToCandles(
            IEnumerable<StockDataPoint> candles,
            IEnumerable<StockCorporateActionEntity> actions)
        {
            var factors = BuildFactors(actions);
            return candles.Select(c =>
            {
                double factor = FactorFor(factors, c.Date);
                return new StockDataPoint
                {
                    Date = c.Date,
                    Volume = c.Volume,
                    Open = factor == 1.0 ? c.Open : Math.Round(c.Open * factor, 2),
                    High = factor == 1.0 ? c.High : Math.Round(c.High * factor, 2),
                    Low = factor == 1.0 ? c.Low : Math.Round(c.Low * factor, 2),
                    Close = factor == 1.0 ? c.Close : Math.Round(c.Close * factor, 2),
                };
            }).ToList();
        }

        // 由新到舊排好有效事件 (前收盤或參考價 <= 0 視為壞資料略過)，累乘係數
        private static List<(string ExDate, double CumulativeFactor)> BuildFactors(IEnumerable<StockCorporateActionEntity> actions)
        {
            var factors = new List<(string ExDate, double CumulativeFactor)>();
            double cumulative = 1.0;
            foreach (var action in actions
                .Where(a => a.PreClose > 0 && a.RefPrice > 0 && !string.IsNullOrEmpty(a.ExDate))
                .OrderByDescending(a => a.ExDate, StringComparer.Ordinal))
            {
                cumulative *= action.RefPrice / action.PreClose;
                // 比這個除權息日更早的價格，要乘上到這一筆為止的累積係數
                factors.Add((action.ExDate, cumulative));
            }
            return factors;
        }

        // 同一天有多筆事件時 factors 是由新到舊累乘；日期嚴格早於該筆除權息日才套用
        private static double FactorFor(List<(string ExDate, double CumulativeFactor)> factors, string tradeDate)
        {
            double factor = 1.0;
            foreach (var (exDate, cumulative) in factors)
            {
                if (string.CompareOrdinal(tradeDate, exDate) < 0) factor = cumulative;
                else break;   // 由新到舊，遇到不早於它的就不用再看更舊的
            }
            return factor;
        }

        private static StockDailyPriceEntity Clone(StockDailyPriceEntity p) => Scale(p, 1.0, round: false);

        private static StockDailyPriceEntity Scale(StockDailyPriceEntity p, double factor, bool round = true)
        {
            double? S(double? v) => v is null ? null : (round ? Math.Round(v.Value * factor, 2) : v);
            return new StockDailyPriceEntity
            {
                StockId = p.StockId,
                TradeDate = p.TradeDate,
                Volume = p.Volume,
                Amount = p.Amount,
                OpenPrice = S(p.OpenPrice),
                HighPrice = S(p.HighPrice),
                LowPrice = S(p.LowPrice),
                ClosePrice = S(p.ClosePrice),
                PriceChange = S(p.PriceChange),
                TradeCount = p.TradeCount,
                Note = p.Note,
            };
        }
    }
}
