using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Sections;

public static class SectionEndpoints
{
    public static void MapSectionEndpoints(this IEndpointRouteBuilder app)
    {
        var sections = app.MapGroup("/api/sections").WithTags("Sections").RequireAuthorization();

        sections.MapGet("/", (SectionService s) => s.ListAsync());

        sections.MapPost("/", async (SectionRequest req, SectionService s) =>
            (await s.CreateAsync(req)).ToHttp(dto => Results.Created($"/api/sections/{dto.Id}", dto))).RequireAuthorization(Roles.Admin);

        sections.MapPut("/{id:int}", async (int id, SectionRequest req, SectionService s) =>
            (await s.UpdateAsync(id, req)).ToHttp()).RequireAuthorization(Roles.Admin);

        sections.MapDelete("/{id:int}", async (int id, SectionService s) =>
            (await s.DeleteAsync(id)).ToHttp()).RequireAuthorization(Roles.Admin);
    }
}
