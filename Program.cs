using StockDashboard.Components;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Implements;
using StockDashboard.Repositories.Interfaces;
using StockDashboard.Services;
using StockDashboard.Services.Patterns;

var builder = WebApplication.CreateBuilder(args);

// 讓「直接執行 bin 資料夾中的 StockDashboard.exe」(非 Development 環境) 時，
// 也能找到專案的 wwwroot 與 scoped CSS 打包檔；dotnet publish 後的正式部署不受影響
builder.WebHost.UseStaticWebAssets();

// ── Blazor (Interactive Server) ──
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── 外部 API：證交所 (TWSE) ──
// 型別化 HttpClient：由框架管理 HttpClient 生命週期，TwseApiClient 透過建構子取得
builder.Services.AddHttpClient<ITwseApiClient, TwseApiClient>(client =>
{
    // 部分網站會拒絕沒有 User-Agent 的程式請求，這裡模擬一般瀏覽器
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ── 外部 API：FinMind (個股多年三大法人；匿名使用，不帶 Token) ──
builder.Services.AddHttpClient<IFinMindApiClient, FinMindApiClient>(client =>
{
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.Timeout = TimeSpan.FromSeconds(60);   // 一次回傳十幾年資料，給長一點
});

// ── 外部 API：FinMind 除權除息結果表 (還原股價用；匿名使用) ──
builder.Services.AddHttpClient<IStockCorporateActionApiClient, FinMindCorporateActionApiClient>(client =>
{
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.Timeout = TimeSpan.FromSeconds(60);
});

// ── 外部 API：oilpriceapi.com (Brent 原油；API Key 在設定 OilPrice:ApiKey) ──
builder.Services.AddHttpClient<IOilPriceApiClient, OilPriceApiClient>(client =>
{
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.Timeout = TimeSpan.FromSeconds(60);
});

// ── 外部 API：Yahoo Finance chart (美股指數日線，例如費半 SOX；不需金鑰，要帶瀏覽器 User-Agent) ──
builder.Services.AddHttpClient<IMarketIndexApiClient, MarketIndexApiClient>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ── 資料存取層 (Repository) ──
builder.Services.AddScoped<IStockPriceRepository, StockPriceRepository>();
builder.Services.AddScoped<IStockNameRepository, StockNameRepository>();
builder.Services.AddScoped<IInstitutionalRepository, InstitutionalRepository>();
builder.Services.AddScoped<IOilPriceRepository, OilPriceRepository>();
builder.Services.AddScoped<IMarketIndexRepository, MarketIndexRepository>();
builder.Services.AddScoped<IStockCorporateActionRepository, StockCorporateActionRepository>();

// ── 商業邏輯層 (Service) ──
builder.Services.AddScoped<IStockPerformanceService, StockPerformanceService>();
builder.Services.AddScoped<IPatternScanService, PatternScanService>();
builder.Services.AddScoped<IStockChartService, StockChartService>();
builder.Services.AddScoped<IInstitutionalSyncService, InstitutionalSyncService>();
builder.Services.AddScoped<IPriceSyncService, PriceSyncService>();
builder.Services.AddScoped<IFinMindSyncService, FinMindSyncService>();
builder.Services.AddScoped<IOilPriceSyncService, OilPriceSyncService>();
builder.Services.AddScoped<IMarketIndexSyncService, MarketIndexSyncService>();
builder.Services.AddScoped<IYahooOilSyncService, YahooOilSyncService>();
builder.Services.AddScoped<IStockCorporateActionSyncService, StockCorporateActionSyncService>();
builder.Services.AddScoped<IStockPriceService, StockPriceService>();   // 還原股價 (尚無頁面使用)
builder.Services.AddSingleton<ILocalPathOpener, LocalPathOpener>();

// ── K 線型態：新增型態時在這裡註冊，型態掃描頁會自動列出 (顯示順序 = 註冊順序) ──
builder.Services.AddSingleton<ICandlePattern, BullishEngulfingPattern>();
builder.Services.AddSingleton<ICandlePattern, MorningStarPattern>();
builder.Services.AddSingleton<ICandlePattern, ThreeWhiteSoldiersPattern>();
builder.Services.AddSingleton<ICandlePattern, ThreeBlackCrowsPattern>();

// 型態掃描頁的畫面狀態 (每個瀏覽器分頁一份，切換頁面後保留)
builder.Services.AddScoped<PatternScanState>();
builder.Services.AddScoped<StockChartState>();

var app = builder.Build();

// 確保 SQLite 資料表存在 (只會建立缺少的表，不會動到既有資料)
await DatabaseInitializer.InitializeAsync(app.Configuration, app.Logger);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

#region 讓系統啟動後，自動用預設瀏覽器打開本機網頁
// 可在 appsettings.json 以 "StartupUrl" 覆寫；預設對應 launchSettings.json 的 http profile (port 8888)
var startupUrl = app.Configuration["StartupUrl"] ?? "http://localhost:8888/stock-update";

// 等伺服器真正開始監聽後才開瀏覽器，避免瀏覽器先開卻連不上
app.Lifetime.ApplicationStarted.Register(() =>
{
    try
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = startupUrl,
            UseShellExecute = true
        });
    }
    catch (Exception ex)
    {
        // 無桌面環境 (伺服器、容器) 時無法開瀏覽器，只記錄警告，不讓整個程式崩潰
        app.Logger.LogWarning("無法自動開啟瀏覽器 ({Url}): {Message}", startupUrl, ex.Message);
    }
});
#endregion

app.Run();
