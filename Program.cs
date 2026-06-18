using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Damit es als Windows-Dienst laufen kann

builder.Services.AddCors();
builder.Host.UseWindowsService();
builder.Services.AddHostedService<AutoShutdownService>();

var app = builder.Build();

// CORS erlauben (damit IIS-Seite den API-Call machen darf)
app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

// Das einzige Endpoint
app.MapPost("/api/shutdown", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();

    string? password = null;
    try
    {
        var doc = JsonDocument.Parse(body);
        password = doc.RootElement.GetProperty("password").GetString();
    }
    catch
    {
        return Results.BadRequest("Ungültiger Body.");
    }

    // *** HIER DEIN PASSWORT SETZEN ***
    const string SHUTDOWN_PASSWORD = "mirodin1";

    if (password != SHUTDOWN_PASSWORD)
    {
        await Task.Delay(1000); // Brute-force minimieren
        return Results.Unauthorized();
    }

    // Shutdown in separatem Thread, damit Response noch zurückkommt
    _ = Task.Run(async () =>
    {
        await Task.Delay(500);
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown",
            Arguments = "/s /f /t 0",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    });

    return Results.Ok(new { message = "Shutdown eingeleitet." });
});

app.Run("http://0.0.0.0:9999");


// ---------- Auto-Shutdown taeglich um 00:30 ----------

public class AutoShutdownService : BackgroundService
{
    private static readonly TimeSpan TargetTime = new(0, 30, 0); // 00:30
    private readonly ILogger<AutoShutdownService> _log;

    public AutoShutdownService(ILogger<AutoShutdownService> log) => _log = log;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("AutoShutdownService gestartet. Ziel: {Time}", TargetTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var next = now.Date + TargetTime;
            if (next <= now) next = next.AddDays(1);

            var wait = next - now;
            _log.LogInformation("Naechster Auto-Shutdown um {Next} (in {Wait})", next, wait);

            try { await Task.Delay(wait, stoppingToken); }
            catch (TaskCanceledException) { return; }

            _log.LogInformation("Auto-Shutdown Trigger erreicht - fahre runter.");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/s /f /t 60",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Auto-Shutdown fehlgeschlagen.");
            }

            // 65s warten, falls per "shutdown /a" abgebrochen -> Schleife laeuft weiter
            try { await Task.Delay(TimeSpan.FromSeconds(65), stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }
}