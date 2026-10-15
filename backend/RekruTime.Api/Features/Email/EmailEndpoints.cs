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

        group.MapGet("/templates", (InvitationService s) => s.ListTemplatesAsync());

        group.MapPut("/templates/{language}", async (string language, TemplateRequest req, InvitationService s) =>
            (await s.SaveTemplateAsync(language, req)).ToHttp());

        group.MapDelete("/templates/{language}", async (string language, InvitationService s) =>
        {
            await s.ResetTemplateAsync(language);
            return Results.NoContent(); // back to the built-in default
        });

        group.MapPost("/templates/{language}/preview", async (string language, TemplateRequest req, InvitationService s) =>
            (await s.PreviewAsync(language, req)).ToHttp());
    }
}
