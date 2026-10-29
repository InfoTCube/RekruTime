using RekruTime.Api.Common;
using RekruTime.Api.Features.Booking;

namespace RekruTime.Api.Features.Forms;

// No auth – anyone with the link applies while the form is open. Rate limited per IP like booking.
public static class PublicFormEndpoints
{
    public static void MapPublicFormEndpoints(this IEndpointRouteBuilder app)
    {
        var forms = app.MapGroup("/api/public/forms/{id:int}").WithTags("Public forms").RequireRateLimiting(PublicBookingEndpoints.RateLimitPolicy);

        forms.MapGet("/", async (int id, PublicFormService s) => (await s.GetAsync(id)).ToHttp());

        forms.MapPost("/responses", async (int id, SubmitRequest req, PublicFormService s) => (await s.SubmitAsync(id, req)).ToHttp());
    }
}
