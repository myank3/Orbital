using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Orbital.DTOs;
using Orbital.Models;
using Orbital.Service;
using System.Security.Claims;

namespace Orbital.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _config;

    public AuthController(
        AuthService authService,
        UserManager<ApplicationUser> userManager,
        IConfiguration config)
    {
        _authService = authService;
        _userManager = userManager;
        _config = config;
    }

    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        var (success, error) = await _authService.RegisterAsync(dto);
        if (!success)
            return BadRequest(new { message = error });

        return Ok(new { message = "Registration successful. Please verify your email before logging in." });
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var (success, error, tokens) = await _authService.LoginAsync(dto);
        if (!success || tokens == null)
            return Unauthorized(new { message = error });

        AppendRefreshTokenCookie(tokens.RefreshToken, tokens.RefreshTokenExpiry);

        return Ok(new LoginResponseDto(tokens.AccessToken));
    }

    // ─────────────────────────────────────────────────────────
    // GOOGLE OAUTH
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the Google OAuth challenge. The browser is redirected to Google,
    /// then back to <see cref="GoogleCallback"/>.
    /// </summary>
    [HttpGet("google-login")]
    public IActionResult GoogleLogin()
    {
        var redirectUrl = Url.Action(nameof(GoogleCallback), "Auth", null, Request.Scheme);
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Handles the redirect back from Google. Finds or creates the local user,
    /// issues our own JWT, sets the refresh cookie, and hands off to the SPA.
    /// </summary>
    [HttpGet("google-callback")]
    public async Task<IActionResult> GoogleCallback()
    {
        var frontendUrl = _config["Client:BaseUrl"] ?? "http://localhost:3000";

        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (!result.Succeeded || result.Principal == null)
            return Redirect($"{frontendUrl}/login?error=google_failed");

        var email = result.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
            return Redirect($"{frontendUrl}/login?error=google_no_email");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _userManager.FindByEmailAsync(normalizedEmail);

        if (user == null)
        {
            // Generate a unique handle from the email prefix
            var baseHandle = new string(
                normalizedEmail.Split('@')[0]
                    .ToLowerInvariant()
                    .Where(c => char.IsLetterOrDigit(c) || c == '_')
                    .ToArray()
            );
            if (baseHandle.Length < 3) baseHandle = "user";

            var handle = baseHandle;
            var suffix = 0;
            while (await _userManager.FindByNameAsync($"{handle}@orbit") != null)
            {
                suffix++;
                handle = $"{baseHandle}{suffix}";
            }

            user = new ApplicationUser
            {
                UserName = $"{handle}@orbit",
                Email = normalizedEmail,
                UserHandle = handle,
                Realm = "orbit",
                DisplayName = result.Principal.FindFirstValue(ClaimTypes.Name) ?? handle,
                EmailConfirmed = true,   // Google already verified the email
                CreatedAt = DateTime.UtcNow
            };

            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
                return Redirect($"{frontendUrl}/login?error=google_create_failed");

            await _userManager.AddToRoleAsync(user, "User");
        }

        // Clean up the temporary external-login cookie set by ASP.NET Identity
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        // Issue our own JWT
        var tokens = await _authService.GenerateAuthTokensAsync(user);

        // Same refresh cookie path as password login
        AppendRefreshTokenCookie(tokens.RefreshToken, tokens.RefreshTokenExpiry);

        return Redirect(
            $"{frontendUrl}/auth/google/success?token={Uri.EscapeDataString(tokens.AccessToken)}"
        );
    }

    // ─────────────────────────────────────────────────────────
    // REFRESH / LOGOUT / EMAIL / PASSWORD
    // ─────────────────────────────────────────────────────────

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh()
    {
        var refreshToken = Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(new { message = "No refresh token." });

        var (success, error, tokens) = await _authService.RefreshTokensByValueAsync(refreshToken);
        if (!success || tokens == null)
            return Unauthorized(new { message = error });

        AppendRefreshTokenCookie(tokens.RefreshToken, tokens.RefreshTokenExpiry);

        return Ok(new LoginResponseDto(tokens.AccessToken));
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("refreshToken", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/",
        });

        return Ok(new { message = "Logged out successfully." });
    }

    [HttpPost("confirm-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailDto dto)
    {
        var (success, error) = await _authService.ConfirmEmailAsync(dto);
        if (!success)
            return BadRequest(new { message = error });

        return Ok(new { message = "Email confirmed successfully. You can now log in." });
    }

    [HttpPost("forget-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgetPassword([FromBody] ForgetPasswordDto dto)
    {
        await _authService.ForgotPasswordAsync(dto.Email);
        return Ok(new { message = "If registered, a reset link has been dispatched." });
    }

    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        var (success, error) = await _authService.ResetPasswordAsync(dto);
        if (!success)
            return BadRequest(new { message = error });

        return Ok(new { message = "Password updated successfully." });
    }

    [HttpPost("resend-confirmation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendConfirmation([FromBody] ForgetPasswordDto dto)
    {
        await _authService.ResendConfirmationEmailAsync(dto.Email);
        return Ok(new { message = "If unverified, a verification link has been dispatched." });
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me()
    {
        return Ok(new
        {
            user = User.Identity?.Name,
            claims = User.Claims.Select(c => new { c.Type, c.Value })
        });
    }

    /// <summary>
    /// Cross-domain refresh cookie.
    ///
    /// SameSite=None is required because the frontend lives on
    /// orbitalhq.vercel.app and this API lives on orbital.runasp.net.
    /// Strict/Lax cookies are never sent on cross-site requests, which
    /// would break silent token refresh.
    ///
    /// `Secure=true` is mandatory when SameSite=None, and the API is
    /// always served over HTTPS, so that's satisfied.
    ///
    /// `Path=/` ensures the cookie is sent on every API endpoint.
    /// </summary>
    private void AppendRefreshTokenCookie(string token, DateTime expires)
    {
        Response.Cookies.Append("refreshToken", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/",
            Expires = expires,
        });
    }
}