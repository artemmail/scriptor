using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var user = http.User;
        var expired = http.Items.ContainsKey("TokenExpired");

        if (user?.Identity == null || !user.Identity.IsAuthenticated)
        {
            context.Result = new JsonResult(new
            { error = expired ? "TokenExpired" : "Unauthorized" })
            {
                StatusCode = expired ? 419 : StatusCodes.Status401Unauthorized
            };
        }
    }
}
