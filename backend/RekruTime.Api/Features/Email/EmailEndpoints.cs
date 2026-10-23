using System.Security.Claims;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Email;

public static class EmailEndpoints
{
    public static void MapEmailEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/email").WithTags("E-mail").RequireAuthorization(Roles.Admin);

        group.MapGet("/settings", (EmailService s) => s.Settings());

        group.MapPost("/test", async (ClaimsPrincipal me, EmailService s, CancellationToken ct) =>
            (await s.SendTestAsync(me.FindFirstValue(ClaimTypes.Email) ?? me.FindFirstValue(ClaimTypes.Name)!, ct)).ToHttp());

        group.MapGet("/messages", (EmailStatus? status, EmailService s) => s.MessagesAsync(status));

        group.MapPost("/messages/{id:int}/retry", async (int id, EmailService s) => (await s.RetryAsync(id)).ToHttp());

        group.MapGet("/templates", (TemplateService s) => s.ListAsync());

        group.MapPut("/templates/{key}/{language}", async (string key, string language, TemplateRequest req, TemplateService s) =>
            (await s.SaveAsync(key, language, req)).ToHttp());

        group.MapDelete("/templates/{key}/{language}", async (string key, string language, TemplateService s) =>
        {
            await s.ResetAsync(key, language);
            return Results.NoContent(); // back to the built-in default
        });

        group.MapPost("/templates/{key}/{language}/preview", async (string key, string language, TemplateRequest req, TemplateService s) =>
            (await s.PreviewAsync(key, language, req)).ToHttp());

        group.MapPost("/decisions", async (SendDecisionsRequest req, DecisionService s) => (await s.SendAsync(req)).ToHttp());
    }
}
