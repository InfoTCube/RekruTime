using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();          // /openapi/v1.json
    app.MapScalarApiReference(); // /scalar
}

// SPA from wwwroot (copied in by the Dockerfile; absent in dev, where Vite serves it)
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", async (AppDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));

app.MapFallback("/api/{**rest}", () => Results.NotFound()); // unknown API routes stay 404, not index.html
app.MapFallbackToFile("index.html");                        // client-side routes

app.Run();
