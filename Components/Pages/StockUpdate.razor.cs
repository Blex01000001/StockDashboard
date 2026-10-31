using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using Dapper;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http.Json;

namespace StockDashboard.Components.Pages
{
    public partial class StockUpdate : ComponentBase
    {
        private readonly string _connectionString = "Data Source=E:\\Code\\C#\\StockDashboard\\stock.db";

        // 注入 HttpClient (若 Blazor 專案尚未註冊，可在 Program.cs 加入 builder.Services.AddHttpClient();)
        [Inject] private HttpClient Http { get; set; } = default!;

        public StockDailyPriceEntity InputPrice { get; set; } = new();
        public string BatchStockId { get; set; } = "2330";
        public string StatusMessage { get; set; } = string.Empty;
        public bool IsError { get; set; } = false;
        public bool IsProcessing { get; set; } = false;
        public string SelectedRange { get; set; } = "1";

        protected override void OnInitialized()
        {
            InputPrice.TradeDate = DateTime.Today.ToString("yyyy-MM-dd");
        }
        /// <summary>
        /// 儲存單筆手動輸入的股價資料
        /// </summary>
        public async Task SaveSinglePrice()
        {
            if (string.IsNullOrWhiteSpace(InputPrice.StockId) || string.IsNullOrWhiteSpace(InputPrice.TradeDate))
            {
                SetStatus("股票代號與交易日期不可為空！", true);
                return;
            }

            IsProcessing = true;
            SetStatus("正在寫入資料庫...", false);

            try
            {
                // 使用 SQLite 的 INSERT OR REPLACE 語法
                // 如果 PRIMARY KEY (StockId, TradeDate) 已存在就直接 UPDATE，不存在就 INSERT
                string sql = @"
                    INSERT OR REPLACE INTO StockDailyPrice 
                    (StockId, TradeDate, Volume, Amount, OpenPrice, HighPrice, LowPrice, ClosePrice)
                    VALUES 
                    (@StockId, @TradeDate, @Volume, @Amount, @OpenPrice, @HighPrice, @LowPrice, @ClosePrice);";

                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.ExecuteAsync(sql, InputPrice);
                }

                SetStatus($"成功！股票 {InputPrice.StockId} 在 {InputPrice.TradeDate} 的資料已成功同步更新。", false);
            }
            catch (Exception ex)
            {
                SetStatus($"寫入資料庫失敗: {ex.Message}", true);
            }
            finally
            {
                IsProcessing = false;
            }
        }

        /// <summary>
        /// ✨ 修正版：由近至遠更新，且連續 5 次無資料自動中斷
        /// </summary>
        public async Task SyncFromApi()
        {
            if (string.IsNullOrWhiteSpace(BatchStockId))
            {
                SetStatus("請指定要從 API 同步的股票代號！", true);
                return;
            }

            IsProcessing = true;

            if (!int.TryParse(SelectedRange, out int totalMonthsToSync))
            {
                totalMonthsToSync = 1;
            }

            SetStatus($"開始同步代號 {BatchStockId} 的歷史數據 (設定範圍: {totalMonthsToSync} 個月)...", false);

            try
            {
                // 1. 動態計算出需要查詢的月份清單 (維持預設：由近至遠，不調用 Reverse)
                var targetMonths = new List<DateTime>();
                DateTime currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

                for (int i = 0; i < totalMonthsToSync; i++)
                {
                    targetMonths.Add(currentMonth.AddMonths(-i));
                }

                var allCleanedData = new List<StockDailyPriceEntity>();
                int successCount = 0;
                int consecutiveEmptyCount = 0; // 🎯 連續無資料計數器
                bool isBreaked = false;

                // 2. 進入迴圈逐月向證交所請求
                for (int i = 0; i < targetMonths.Count; i++)
                {
                    var monthDate = targetMonths[i];
                    string queryDateStr = monthDate.ToString("yyyyMM01");

                    SetStatus($"[{i + 1}/{totalMonthsToSync}] 正在下載 {monthDate:yyyy年MM月} 的資料... (連續無資料: {consecutiveEmptyCount}/3)", false);

                    string url = $"https://www.twse.com.tw/exchangeReport/STOCK_DAY?response=json&date={queryDateStr}&stockNo={BatchStockId}";

                    try
                    {
                        var response = await Http.GetFromJsonAsync<TwseStockResponse>(url);

                        // 判斷證交所是否回應有效交易資料
                        if (response != null && response.Stat == "OK" && response.Data != null && response.Data.Any())
                        {
                            var monthCleanedData = ParseTwseData(BatchStockId, response.Data);
                            allCleanedData.AddRange(monthCleanedData);
                            successCount++;
                            consecutiveEmptyCount = 0; // ⚡ 只要成功一次，連續無資料計數器立刻歸零
                        }
                        else
                        {
                            // 證交所回傳 "很抱歉，沒有符合條件的資料!" 或 Data 為空
                            consecutiveEmptyCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"{monthDate:yyyyMM} 下載失敗: {ex.Message}");
                        consecutiveEmptyCount++; // 網路異常或 API 報錯也計入失敗
                    }

                    // 🚨 觸發熔斷機制：連續 3 次沒有資料就中斷
                    if (consecutiveEmptyCount >= 3)
                    {
                        isBreaked = true;
                        break;
                    }

                    // 防封鎖機制：如果還需要查下一個月，強迫延遲 2.5 秒
                    if (i < targetMonths.Count - 1)
                    {
                        await Task.Delay(2500);
                    }
                }

                if (!allCleanedData.Any())
                {
                    string alertMsg = isBreaked
                        ? $"同步中斷！由於連續 {consecutiveEmptyCount} 次未能從 API 取得資料（可能已達該股上市極限或遭頻率限制）。"
                        : "資料同步失敗，未撈取到任何有效數據。";
                    SetStatus(alertMsg, true);
                    return;
                }

                // 3. 批次寫入 SQLite 
                string sql = @"
                    INSERT OR REPLACE INTO StockDailyPrice 
                    (StockId, TradeDate, Volume, Amount, OpenPrice, HighPrice, LowPrice, ClosePrice)
                    VALUES 
                    (@StockId, @TradeDate, @Volume, @Amount, @OpenPrice, @HighPrice, @LowPrice, @ClosePrice);";

                int affectedRows = 0;
                using (var connection = new SqliteConnection(_connectionString))
                {
                    affectedRows = await connection.ExecuteAsync(sql, allCleanedData);
                }

                string finalStatus = $"全部更新程序結束。成功撈取 {successCount} 個月份，共更新 {affectedRows} 筆 K 線資料至 SQLite。";
                if (isBreaked)
                {
                    finalStatus += $" (註: 因連續 3 次無資料，已提前終止歷史追溯)";
                }

                SetStatus(finalStatus, false);
            }
            catch (Exception ex)
            {
                SetStatus($"大批次同步失敗: {ex.Message}", true);
            }
            finally
            {
                IsProcessing = false;
            }
        }

        /// <summary>
        /// ✨ 核心邏輯：將證交所原始文字陣列清洗為標準型態
        /// </summary>
        private List<StockDailyPriceEntity> ParseTwseData(string stockId, List<List<string>> rawData)
        {
            var entities = new List<StockDailyPriceEntity>();

            foreach (var row in rawData)
            {
                // 證交所 fields 欄位依序為: 
                // 0:日期, 1:成交股數, 2:成交金額, 3:開盤價, 4:最高價, 5:最低價, 6:收盤價, 7:漲跌價差, 8:成交筆數
                if (row.Count < 7) continue;

                // A. 處理日期: "115/05/04" -> 民國年轉西元年 -> "2026-05-04"
                string rawDate = row[0];
                string cleanDate = string.Empty;
                var dateParts = rawDate.Split('/');
                if (dateParts.Length == 3 && int.TryParse(dateParts[0], out int rocYear))
                {
                    int adYear = rocYear + 1911; // 民國轉西元
                    cleanDate = $"{adYear}-{dateParts[1]}-{dateParts[2]}";
                }
                else
                {
                    continue; // 日期格式異常則跳過
                }

                // B. 輔助方法：移除千分號(,)並嘗試轉換型態
                long? ParseLong(string input) => long.TryParse(input.Replace(",", ""), out long val) ? val : null;
                double? ParseDouble(string input) => double.TryParse(input.Replace(",", ""), out double val) ? val : null;

                entities.Add(new StockDailyPriceEntity
                {
                    StockId = stockId,
                    TradeDate = cleanDate,
                    Volume = ParseLong(row[1]),
                    Amount = ParseLong(row[2]),
                    OpenPrice = ParseDouble(row[3]),
                    HighPrice = ParseDouble(row[4]),
                    LowPrice = ParseDouble(row[5]),
                    ClosePrice = ParseDouble(row[6])
                });
            }

            return entities;
        }

        private void SetStatus(string msg, bool isError)
        {
            StatusMessage = msg;
            IsError = isError;

            // 💡 關鍵：強制 Blazor 重新渲染 UI 畫面
            // 這樣在 for 迴圈每跑一次時，前端才能即時看到「正在下載 2026年05月...」的文字變化
            InvokeAsync(StateHasChanged);
        }
        /// <summary>
        /// 資料庫結構對應的 Entity Class
        /// </summary>
        public class StockDailyPriceEntity
        {
            public string StockId { get; set; } = string.Empty;
            public string TradeDate { get; set; } = string.Empty;
            public long? Volume { get; set; }
            public long? Amount { get; set; }
            public double? OpenPrice { get; set; }
            public double? HighPrice { get; set; }
            public double? LowPrice { get; set; }
            public double? ClosePrice { get; set; }
        }
        /// <summary>
        /// 證交所 API 回傳的根結構
        /// </summary>
        public class TwseStockResponse
        {
            public string Stat { get; set; } = string.Empty;
            public List<List<string>> Data { get; set; } = new();
        }
    }
}