using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Auth;
using RekruTime.Api.Features.Sections;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddOpenApi();

builder.Services.AddIdentity<AppUser, IdentityRole>(o => o.User.RequireUniqueEmail = true)
    .AddEntityFrameworkStores<AppDbContext>();
builder.Services.AddAuthorization(o => o.AddPolicy(Roles.Admin, p => p.RequireRole(Roles.Admin)));
builder.Services.AddValidation(); // DataAnnotations on endpoint parameters → 400 ValidationProblem
builder.Services.AddProblemDetails();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "rekrutime.auth";
    o.Cookie.SameSite = SameSiteMode.Strict; // SPA is same-origin; blocks cross-site requests carrying the cookie (CSRF)
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    // API: answer 401/403 instead of redirecting to a login page
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});

var app = builder.Build();

await Seed.RunAsync(app.Services, app.Configuration);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();          // /openapi/v1.json
    app.MapScalarApiReference(); // /scalar
}

// SPA from wwwroot (copied in by the Dockerfile; absent in dev, where Vite serves it)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", async (AppDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));
app.MapAuthEndpoints();
app.MapSectionEndpoints();

app.MapFallback("/api/{**rest}", () => Results.NotFound()); // unknown API routes stay 404, not index.html
app.MapFallbackToFile("index.html");                        // client-side routes

app.Run();
