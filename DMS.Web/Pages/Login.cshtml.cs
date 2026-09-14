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
            if (!string.Equals(login.Role, "User", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, "The mobile daily update page is available for developer accounts only.");
                return Page();
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, login.UserId),
                new(ClaimTypes.Name, login.Username ?? Username.Trim()),
                new(ClaimTypes.Role, login.Role),
                new("api_token", login.Token)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return RedirectToPage("/Index");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }
}
