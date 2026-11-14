using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StockDashboard.Components.Shared;
using StockDashboard.Models;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;
using StockDashboard.Services;

namespace StockDashboard.Components.UpdateSources
{
    /// <summary>Yahoo Finance 來源的同步卡片：費半 (SOX)、Brent 期貨 (BZ=F)</summary>
    public partial class YahooFinanceSection : ComponentBase
    {
        [Inject] private IMarketIndexSyncService MarketIndexSync { get; set; } = default!;
        [Inject] private IMarketIndexRepository MarketIndexRepository { get; set; } = default!;
        [Inject] private IOilPriceRepository OilRepository { get; set; } = default!;
        [Inject] private IYahooOilSyncService YahooOilSync { get; set; } = default!;

        public (string? FirstDate, string? LastDate, int Days)? YahooOilSummary { get; set; }
        public bool IsSyncingYahooOil { get; set; } = false;
        public string YahooOilStatusMessage { get; set; } = string.Empty;
        public bool YahooOilIsError { get; set; } = false;

        public (string? FirstDate, string? LastDate, int Days)? SoxSummary { get; set; }
        public bool IsSyncingSox { get; set; } = false;
        public string SoxStatusMessage { get; set; } = string.Empty;
        public bool SoxIsError { get; set; } = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadSoxSummaryAsync();
            await LoadYahooOilSummaryAsync();
        }

        // 舊資料庫沒有這張表時只是不顯示摘要，不影響其他功能
        private async Task LoadSoxSummaryAsync()
        {
            try { SoxSummary = await MarketIndexRepository.GetSummaryAsync(MarketIndexApiClient.SoxCode); }
            catch (Exception) { SoxSummary = null; }
        }

        private async Task LoadYahooOilSummaryAsync()
        {
            try { YahooOilSummary = await OilRepository.GetSummaryAsync(YahooOilSyncService.Code); }
            catch (Exception) { YahooOilSummary = null; }
        }

        /// <summary>下載 Yahoo Brent 期貨 (BZ=F) 全部日線 (一次請求)</summary>
        public async Task SyncYahooOil()
        {
            if (IsSyncingYahooOil) return;
            IsSyncingYahooOil = true;
            YahooOilStatusMessage = "下載中...";
            YahooOilIsError = false;
            try
            {
                var result = await YahooOilSync.SyncAsync(CancellationToken.None);
                await LoadYahooOilSummaryAsync();
                if (result.Error != null)
                {
                    YahooOilStatusMessage = $"Yahoo 原油同步失敗：{result.Error}";
                    YahooOilIsError = true;
                }
                else
                {
                    YahooOilStatusMessage = $"Yahoo 原油同步完成：寫入 {result.RowsWritten} 筆（{result.FirstDate} ~ {result.LastDate}）。";
                }
            }
            finally
            {
                IsSyncingYahooOil = false;
            }
        }

        /// <summary>下載最近一年的費半日線 (一次請求)</summary>
        public async Task SyncSox()
        {
            if (IsSyncingSox) return;
            IsSyncingSox = true;
            SoxStatusMessage = "下載中...";
            SoxIsError = false;
            try
            {
                var result = await MarketIndexSync.SyncSoxAsync(CancellationToken.None);
                await LoadSoxSummaryAsync();
                if (result.Error != null)
                {
                    SoxStatusMessage = $"費半同步失敗：{result.Error}";
                    SoxIsError = true;
                }
                else
                {
                    SoxStatusMessage = $"費半同步完成：寫入 {result.RowsWritten} 筆（{result.FirstDate} ~ {result.LastDate}）。";
                }
            }
            finally
            {
                IsSyncingSox = false;
            }
        }
    }
}
