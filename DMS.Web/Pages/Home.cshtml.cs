using DMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DMS.Web.Pages;

[Authorize(Roles = "User")]
public sealed class HomeModel : PageModel
{
    public string DisplayName => User.Identity?.Name ?? "Developer";
}
