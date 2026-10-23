using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Applications;
using RekruTime.Api.Features.Auth;
using RekruTime.Api.Features.Availability;
using RekruTime.Api.Features.Booking;
using RekruTime.Api.Features.Email;
using RekruTime.Api.Features.Interviews;
using RekruTime.Api.Features.Recruits;
using RekruTime.Api.Features.Sections;
using RekruTime.Api.Features.Settings;
using RekruTime.Api.Features.Users;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddOpenApi();
builder.Services.Configure<ClubOptions>(builder.Configuration.GetSection("Club"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddSingleton<SmtpMailer>();
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EmailSender>()); // delivers the outbox

// Business logic lives in services; endpoints only translate HTTP ↔ service calls.
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<SectionService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<SlotService>();
builder.Services.AddScoped<BookingLinkService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<PublicBookingService>();
builder.Services.AddScoped<InterviewService>();
builder.Services.AddScoped<NoteService>();
builder.Services.AddScoped<VoteService>();
builder.Services.AddScoped<RecruitService>();
builder.Services.AddScoped<RecruitReviewService>();
builder.Services.AddScoped<InvitationService>();
builder.Services.AddScoped<TemplateService>();
builder.Services.AddScoped<DecisionService>();
builder.Services.AddScoped<BookingEmailService>();
builder.Services.AddScoped<EmailService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddIdentity<AppUser, IdentityRole>(o => o.User.RequireUniqueEmail = true)
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders(); // password reset tokens
// Invite / new-password links (AccountMails) stay valid this long.
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromDays(RekruTime.Api.Features.Email.AccountMails.LinkValidDays));
// Re-read roles/sign-out from the DB every minute, so role changes and deleted users take effect quickly.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));
builder.Services.AddAuthorization(o => o.AddPolicy(Roles.Admin, p => p.RequireRole(Roles.Admin)));
builder.Services.AddValidation(); // DataAnnotations on endpoint parameters → 400 ValidationProblem
builder.Services.AddProblemDetails();
// Login cookies are encrypted with these keys – on a volume they survive restarts/redeploys (Docker image sets the path).
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));
// Public booking endpoints: per-IP limit against token guessing / spam.
// Behind a reverse proxy the client IP comes from X-Forwarded-For: ASPNETCORE_FORWARDEDHEADERS_ENABLED=true (set in the Dockerfile).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(PublicBookingEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});
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
app.UseRateLimiter();

app.MapGet("/api/health", async (AppDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));
app.MapAuthEndpoints();
app.MapSectionEndpoints();
app.MapUserEndpoints();
app.MapAvailabilityEndpoints();
app.MapSettingsEndpoints();
app.MapBookingLinkEndpoints();
app.MapPublicBookingEndpoints();
app.MapInterviewEndpoints();
app.MapRecruitEndpoints();
app.MapApplicationEndpoints();
app.MapEmailEndpoints();

app.MapFallback("/api/{**rest}", () => Results.NotFound()); // unknown API routes stay 404, not index.html
app.MapFallbackToFile("index.html");                        // client-side routes

app.Run();

public partial class Program; // for WebApplicationFactory in tests
