using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors();
builder.Host.UseWindowsService(); // <- das hinzufügen
var app = builder.Build();

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
            Arguments = "/s /t 0",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    });

    return Results.Ok(new { message = "Shutdown eingeleitet." });
});

// Nur auf localhost lauschen (Port 5050) - oder 0.0.0.0 wenn von außen
app.Run("http://0.0.0.0:9999");