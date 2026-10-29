using System.Security.Claims;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Forms;

public static class PreselectionEndpoints
{
    public static void MapPreselectionEndpoints(this IEndpointRouteBuilder app)
    {
        // Admins and section leaders (checked in the service).
        var group = app.MapGroup("/api/preselection").WithTags("Preselection").RequireAuthorization();
        static PreselectionService.Viewer Viewer(ClaimsPrincipal me) => new(me.Id(), me.IsInRole(Roles.Admin));

        group.MapGet("/", async (int? sectionId, ClaimsPrincipal me, PreselectionService s) => (await s.ListAsync(Viewer(me), sectionId)).ToHttp());

        group.MapPost("/", async (PreselectRequest req, ClaimsPrincipal me, PreselectionService s) => (await s.SetAsync(Viewer(me), req)).ToHttp());

        // Invitations go per recruit across all sections, so admins send them.
        group.MapGet("/invitations", (InterviewInviteService s) => s.CandidatesAsync()).RequireAuthorization(Roles.Admin);

        group.MapPost("/invitations", async (InviteRequest req, ClaimsPrincipal me, InterviewInviteService s) =>
            (await s.InviteAsync(req, me.Id())).ToHttp()).RequireAuthorization(Roles.Admin);
    }
}
