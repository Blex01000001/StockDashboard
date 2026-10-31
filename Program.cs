using StockDashboard.Components;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Implements;
using StockDashboard.Repositories.Interfaces;
using StockDashboard.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Blazor (Interactive Server) ──
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── 外部 API：證交所 (TWSE) ──
// 型別化 HttpClient：由框架管理 HttpClient 生命週期，TwseApiClient 透過建構子取得
builder.Services.AddHttpClient<ITwseApiClient, TwseApiClient>();

// ── 資料存取層 (Repository) ──
builder.Services.AddScoped<IStockPriceRepository, StockPriceRepository>();
builder.Services.AddScoped<IStockListRepository, StockListRepository>();

// ── 商業邏輯層 (Service) ──
builder.Services.AddScoped<IStockPerformanceService, StockPerformanceService>();

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
var startupUrl = app.Configuration["StartupUrl"] ?? "http://localhost:8888/stock-query";

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
