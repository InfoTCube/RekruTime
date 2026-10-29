using System.Security.Claims;
using RekruTime.Api.Common;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Booking;

namespace RekruTime.Api.Features.Sections;

public static class SectionEndpoints
{
    public static void MapSectionEndpoints(this IEndpointRouteBuilder app)
    {
        var sections = app.MapGroup("/api/sections").WithTags("Sections").RequireAuthorization();

        sections.MapGet("/", (SectionService s) => s.ListAsync());

        sections.MapGet("/capacity", (SlotService s) => s.CapacityAsync()).RequireAuthorization(Roles.Admin);

        sections.MapPost("/", async (SectionRequest req, SectionService s) =>
            (await s.CreateAsync(req)).ToHttp(dto => Results.Created($"/api/sections/{dto.Id}", dto))).RequireAuthorization(Roles.Admin);

        sections.MapPut("/{id:int}", async (int id, SectionRequest req, SectionService s) =>
            (await s.UpdateAsync(id, req)).ToHttp()).RequireAuthorization(Roles.Admin);

        // Leaders of the section and admins (checked in the service).
        sections.MapGet("/{id:int}/questions", async (int id, ClaimsPrincipal me, SectionService s) =>
            (await s.GetQuestionsAsync(id, me.Id(), me.IsInRole(Roles.Admin))).ToHttp());

        sections.MapPut("/{id:int}/questions", async (int id, QuestionsDto req, ClaimsPrincipal me, SectionService s) =>
            (await s.SetQuestionsAsync(id, req, me.Id(), me.IsInRole(Roles.Admin))).ToHttp());

        sections.MapDelete("/{id:int}", async (int id, SectionService s) =>
            (await s.DeleteAsync(id)).ToHttp()).RequireAuthorization(Roles.Admin);
    }
}
