using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StockDashboard.Components.Shared;
using StockDashboard.Models;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;
using StockDashboard.Services;

namespace StockDashboard.Components.UpdateSources
{
    /// <summary>oilpriceapi.com 來源的同步卡片：Brent 原油當日均價、開啟 appsettings.json</summary>
    public partial class OilPriceApiSection : ComponentBase
    {
        [Inject] private IOilPriceSyncService OilSync { get; set; } = default!;
        [Inject] private IOilPriceRepository OilRepository { get; set; } = default!;
        [Inject] private ILocalPathOpener PathOpener { get; set; } = default!;

        public (string? FirstDate, string? LastDate, int Days)? OilSummary { get; set; }
        public bool IsSyncingOil { get; set; } = false;
        public string OilStatusMessage { get; set; } = string.Empty;
        public bool OilIsError { get; set; } = false;

        protected override Task OnInitializedAsync() => LoadOilSummaryAsync();

        // 舊資料庫沒有這張表時只是不顯示摘要，不影響其他功能
        private async Task LoadOilSummaryAsync()
        {
            try { OilSummary = await OilRepository.GetSummaryAsync(OilPriceApiClient.BrentCode); }
            catch (Exception) { OilSummary = null; }
        }

        public void OpenSettingsFile() => RunOpen(PathOpener.OpenSettingsFile, "已用文字編輯器開啟 appsettings.json，改好存檔後需重新啟動網站才會生效。");

        public void OpenSettingsFolder() => RunOpen(PathOpener.OpenSettingsFolder, "已開啟 appsettings.json 所在資料夾。");

        private void RunOpen(Action open, string successMessage)
        {
            try
            {
                open();
                OilStatusMessage = successMessage;
                OilIsError = false;
            }
            catch (Exception ex)
            {
                OilStatusMessage = ex.Message;
                OilIsError = true;
            }
        }

        /// <summary>下載最近一年的 Brent 每日價格 (一次請求)</summary>
        public async Task SyncOil()
        {
            if (IsSyncingOil) return;
            IsSyncingOil = true;
            OilStatusMessage = "下載中...";
            OilIsError = false;
            try
            {
                var result = await OilSync.SyncAsync(CancellationToken.None);
                await LoadOilSummaryAsync();
                if (result.Error != null)
                {
                    OilStatusMessage = $"原油同步失敗：{result.Error}";
                    OilIsError = true;
                }
                else
                {
                    OilStatusMessage = $"原油同步完成：寫入 {result.RowsWritten} 筆（{result.FirstDate} ~ {result.LastDate}）。";
                }
            }
            finally
            {
                IsSyncingOil = false;
            }
        }
    }
}
