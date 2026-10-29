using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Forms;

public static class FormEndpoints
{
    public static void MapFormEndpoints(this IEndpointRouteBuilder app)
    {
        var forms = app.MapGroup("/api/forms").WithTags("Forms").RequireAuthorization(Roles.Admin);

        forms.MapGet("/", (FormService s) => s.ListAsync());

        forms.MapGet("/{id:int}", async (int id, FormService s) => (await s.GetAsync(id)).ToHttp());

        forms.MapPost("/", async (FormRequest req, FormService s) =>
            (await s.CreateAsync(req)).ToHttp(dto => Results.Created($"/api/forms/{dto.Id}", dto)));

        forms.MapPut("/{id:int}", async (int id, FormRequest req, FormService s) => (await s.UpdateAsync(id, req)).ToHttp());

        forms.MapPatch("/{id:int}", async (int id, SetOpenRequest req, FormService s) => (await s.SetOpenAsync(id, req.IsOpen)).ToHttp());

        forms.MapDelete("/{id:int}", async (int id, FormService s) => (await s.DeleteAsync(id)).ToHttp());
    }
}
