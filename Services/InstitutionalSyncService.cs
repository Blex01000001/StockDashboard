using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Services
{
    /// <summary>同步進度 (資料更新頁顯示用)</summary>
    public record InstitutionalSyncProgress(int Done, int Total, string Date, string Message);

    /// <summary>同步結果</summary>
    public class InstitutionalSyncResult
    {
        public int DaysWithData { get; set; }      // 這次新抓到資料的交易日數 (略過的已下載日期不算)
        public int RowsWritten { get; set; }       // 寫入的資料筆數 (兩個來源合計)
        public int Requests { get; set; }          // 向證交所發出的請求數
        public int SkippedChecks { get; set; }     // 因為已查過而略過的「日期 × 來源」數
        public bool Cancelled { get; set; }
        public string? Error { get; set; }         // 中途失敗的原因 (例如被證交所暫時封鎖)
    }

    public interface IInstitutionalSyncService
    {
        Task<InstitutionalSyncResult> SyncAsync(DateTime from, DateTime to, IProgress<InstitutionalSyncProgress>? progress, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 三大法人同步：證交所 T86 與 MI_QFIIS 都是「一次一天、全部上市股票」，因此逐日下載。
    /// - 由新到舊，週末直接略過；每完成一天就寫入資料庫 (中途停止也不會白抓)
    /// - 已有資料的日期略過；查過無資料的日期 (假日) 若是在之後的日子查的，也略過
    /// - 每次請求間隔 Twse:RequestDelayMs (預設 2500 毫秒)，避免被證交所封鎖
    /// - 遇到 HTTP 錯誤或回傳非 JSON (多半是被封鎖) 立即停止，不再繼續請求
    /// </summary>
    public class InstitutionalSyncService : IInstitutionalSyncService
    {
        public const string T86Source = "T86";
        public const string QfiisSource = "MI_QFIIS";

        private readonly ITwseApiClient _twse;
        private readonly IInstitutionalRepository _repository;
        private readonly int _delayMs;
        private readonly Func<DateTime> _now;

        public InstitutionalSyncService(ITwseApiClient twse, IInstitutionalRepository repository, IConfiguration configuration)
            : this(twse, repository, int.TryParse(configuration["Twse:RequestDelayMs"], out int ms) ? ms : 2500, () => DateTime.Now) { }

        // 測試用：可指定請求間隔與「現在時間」
        public InstitutionalSyncService(ITwseApiClient twse, IInstitutionalRepository repository, int delayMs, Func<DateTime> now)
        {
            _twse = twse;
            _repository = repository;
            _delayMs = delayMs;
            _now = now;
        }

        /// <summary>
        /// from ~ to 之間 (含) 的平日，由新到舊。
        /// </summary>
        public static List<DateTime> Weekdays(DateTime from, DateTime to)
        {
            var days = new List<DateTime>();
            for (var d = to.Date; d >= from.Date; d = d.AddDays(-1))
            {
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days.Add(d);
            }
            return days;
        }

        /// <summary>
        /// 這一天、這個來源是否需要 (再) 查詢：沒查過 → 要；查過有資料 → 不要；
        /// 查過沒資料 → 若是在那天之後才查的 (確定是假日) 不要，當天查的 (可能尚未公布) 要。
        /// </summary>
        public static bool NeedsCheck(DateTime date, IReadOnlyDictionary<string, (bool HasData, DateTime CheckedAt)> checkedDates)
        {
            if (!checkedDates.TryGetValue(date.ToString("yyyy-MM-dd"), out var log)) return true;
            if (log.HasData) return false;
            return log.CheckedAt.Date <= date.Date;
        }

        public async Task<InstitutionalSyncResult> SyncAsync(DateTime from, DateTime to, IProgress<InstitutionalSyncProgress>? progress, CancellationToken cancellationToken)
        {
            var result = new InstitutionalSyncResult();
            var days = Weekdays(from, to);
            var t86Checked = await _repository.GetCheckedDatesAsync(T86Source);
            var qfiisChecked = await _repository.GetCheckedDatesAsync(QfiisSource);

            for (int i = 0; i < days.Count; i++)
            {
                var day = days[i];
                string date = day.ToString("yyyy-MM-dd");
                bool dayHasData = false;

                foreach (var (source, checkedDates) in new[] { (T86Source, t86Checked), (QfiisSource, qfiisChecked) })
                {
                    if (cancellationToken.IsCancellationRequested) { result.Cancelled = true; return result; }

                    if (!NeedsCheck(day, checkedDates))
                    {
                        result.SkippedChecks++;
                        continue;
                    }

                    progress?.Report(new InstitutionalSyncProgress(i, days.Count, date, $"下載 {date} {(source == T86Source ? "三大法人買賣超" : "外資持股")}"));

                    // 每次請求前等待 (第一次除外)，避免被證交所封鎖
                    if (result.Requests > 0 && _delayMs > 0)
                    {
                        try { await Task.Delay(_delayMs, cancellationToken); }
                        catch (OperationCanceledException) { result.Cancelled = true; return result; }
                    }

                    try
                    {
                        result.Requests++;
                        var rows = source == T86Source
                            ? await _twse.GetInstitutionalTradingAsync(day, cancellationToken)
                            : await _twse.GetForeignHoldingAsync(day, cancellationToken);

                        if (rows.Count > 0)
                        {
                            result.RowsWritten += source == T86Source
                                ? await _repository.UpsertTradingAsync(rows)
                                : await _repository.UpsertHoldingAsync(rows);
                            dayHasData = true;
                        }
                        await _repository.LogCheckAsync(date, source, rows.Count > 0, _now());
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        result.Cancelled = true;
                        return result;
                    }
                    catch (Exception ex)
                    {
                        // 多半是被封鎖或網路問題：立即停止，已完成的日期都已寫入
                        result.Error = $"{date}：{ex.Message}";
                        return result;
                    }
                }

                if (dayHasData) result.DaysWithData++;
                progress?.Report(new InstitutionalSyncProgress(i + 1, days.Count, date, $"完成 {date}"));
            }

            return result;
        }
    }
}
