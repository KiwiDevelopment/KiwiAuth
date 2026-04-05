using KiwiAuth.Endpoints;
using Microsoft.AspNetCore.Routing;

namespace KiwiAuth.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Maps all KiwiAuth endpoints under /auth.
    /// Call after UseAuthentication() and UseAuthorization() in your middleware pipeline.
    /// </summary>
    public static IEndpointRouteBuilder MapKiwiAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints();
        app.MapMfaEndpoints();
        return app;
    }
}
