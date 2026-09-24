using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DMS.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DMS.Web.Pages;

public sealed class LoginModel : PageModel
{
    private readonly DmsApiClient _apiClient;

    public LoginModel(DmsApiClient apiClient) => _apiClient = apiClient;

    [BindProperty]
    [Required(ErrorMessage = "Enter your username.")]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Enter your password.")]
    public string Password { get; set; } = string.Empty;

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            var login = await _apiClient.LoginAsync(Username.Trim(), Password, cancellationToken);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, login.UserId),
                new(ClaimTypes.Name, login.Username ?? Username.Trim()),
                new(ClaimTypes.Role, login.Role),
                new("api_token", login.Token)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return string.Equals(login.Role, "Admin", StringComparison.OrdinalIgnoreCase)
                ? RedirectToPage("/Admin/DailyWork")
                : RedirectToPage("/Home");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }
}
