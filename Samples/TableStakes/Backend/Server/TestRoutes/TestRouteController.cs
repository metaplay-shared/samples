using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Server.PublicWebApi;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Game.Server
{
    /// <summary>
    /// Base class for the controllers that serve the unauthenticated <c>test/</c> routes. Every action answers 404
    /// unless <see cref="TestRoutesOptions"/> enables the routes, so a new test controller cannot expose a route by
    /// forgetting the check (<c>docs/architecture.md</c>, "HTTP endpoints").
    /// </summary>
    public abstract class TestRouteController : PublicWebApiController
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (!RuntimeOptionsRegistry.Instance.GetCurrent<TestRoutesOptions>().Enabled)
                context.Result = NotFound();
        }
    }
}
