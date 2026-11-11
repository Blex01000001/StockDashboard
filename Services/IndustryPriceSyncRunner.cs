namespace StockDashboard.Services
{
    public interface IIndustryPriceSyncRunner
    {
        bool IsRunning { get; }
        string IndustryCode { get; }
        int Months { get; }   // 這次同步每檔最多往前幾個月 (IndustryPriceSyncService.MaxMonths = 全部歷史)
        IndustryPriceSyncProgress? Progress { get; }
        IndustryPriceSyncResult? LastResult { get; }

        // 狀態改變時觸發 (頁面據此重繪；可能在背景執行緒呼叫)
        event Action? Changed;

        // 開始依產業同步 (背景執行，立即返回)；已在執行中則忽略
        void Start(string industryCode, int months = IndustryPriceSyncService.MaxMonths);
        void Stop();
    }

    /// <summary>
    /// 依產業批次同步的背景執行者 (單例)：同步要跑好幾個小時，所以不綁在頁面或連線上，
    /// 關閉分頁、切換頁面都會繼續跑 (網站程式本身關掉才會中止)，再開啟資料更新頁就看得到進度。
    /// </summary>
    public class IndustryPriceSyncRunner : IIndustryPriceSyncRunner
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;

        public IndustryPriceSyncRunner(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

        public bool IsRunning { get; private set; }
        public string IndustryCode { get; private set; } = string.Empty;
        public int Months { get; private set; } = IndustryPriceSyncService.MaxMonths;
        public IndustryPriceSyncProgress? Progress { get; private set; }
        public IndustryPriceSyncResult? LastResult { get; private set; }
        public event Action? Changed;

        public void Start(string industryCode, int months = IndustryPriceSyncService.MaxMonths)
        {
            lock (_lock)
            {
                if (IsRunning) return;
                IsRunning = true;
                IndustryCode = industryCode.Trim();
                Months = months;
                Progress = null;
                LastResult = null;
                _cts = new CancellationTokenSource();
            }
            Notify();
            var token = _cts!.Token;
            string code = IndustryCode;
            _ = Task.Run(() => RunAsync(code, months, token));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); }
            catch (ObjectDisposedException) { /* 剛好跑完 */ }
        }

        private async Task RunAsync(string code, int months, CancellationToken token)
        {
            IndustryPriceSyncResult result;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IIndustryPriceSyncService>();
                // 不用 Progress<T>：它會用 SynchronizationContext 轉送，背景執行緒上直接更新即可
                result = await service.SyncAsync(code, new DirectProgress(p => { Progress = p; Notify(); }), token, months);
            }
            catch (Exception ex)
            {
                result = new IndustryPriceSyncResult { Error = ex.Message };
            }
            lock (_lock)
            {
                LastResult = result;
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
            Notify();
        }

        private void Notify()
        {
            try { Changed?.Invoke(); }
            catch (Exception) { /* 頁面已關閉等情況，不影響同步 */ }
        }

        private sealed class DirectProgress : IProgress<IndustryPriceSyncProgress>
        {
            private readonly Action<IndustryPriceSyncProgress> _action;
            public DirectProgress(Action<IndustryPriceSyncProgress> action) => _action = action;
            public void Report(IndustryPriceSyncProgress value) => _action(value);
        }
    }
}
