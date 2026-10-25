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
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        var (success, error) = await _authService.RegisterAsync(dto);
        if (!success) return BadRequest(new { message = error });
        return Ok(new { message = "Registration successful. Please verify your email." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var (success, error, tokens) = await _authService.LoginAsync(dto);
        if (!success || tokens == null) return Unauthorized(new { message = error });

        AppendRefreshTokenCookie(tokens.RefreshToken, tokens.RefreshTokenExpiry);
        var isMobile = Request.Headers["X-Client-Type"] == "mobile";

        // 🔥 Using the explicit constructor parameters to prevent the error
        return Ok(new LoginResponseDto(
            tokens.AccessToken,
            isMobile ? tokens.RefreshToken : null
        ));
    }

    [HttpGet("google-login")]
    public IActionResult GoogleLogin([FromQuery] string? client = null)
    {
        var redirectUrl = Url.Action(nameof(GoogleCallback), "Auth", null, Request.Scheme);
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };

        if (client == "mobile") properties.Items["client"] = "mobile";
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

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
            var baseHandle = new string(normalizedEmail.Split('@')[0].ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
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
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };

            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
                return Redirect($"{frontendUrl}/login?error=google_create_failed");

            await _userManager.AddToRoleAsync(user, "User");
        }

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var tokens = await _authService.IssueNewSessionAsync(user);
        AppendRefreshTokenCookie(tokens.RefreshToken, tokens.RefreshTokenExpiry);

        var isMobile = result.Properties?.Items.TryGetValue("client", out var clientType) == true && clientType == "mobile";

        if (isMobile)
        {
            var refresh = Uri.EscapeDataString(tokens.RefreshToken);
            return Redirect($"orbital://auth?token={Uri.EscapeDataString(tokens.AccessToken)}&refreshToken={refresh}");
        }

        return Redirect($"{frontendUrl}/auth/google/success?token={Uri.EscapeDataString(tokens.AccessToken)}");
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto? request = null)
    {
        var refreshToken = request?.RefreshToken ?? Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(new { message = "No refresh token." });

        var (success, error, tokens) = await _authService.RefreshTokensByValueAsync(refreshToken);
        if (!success || tokens == null) return Unauthorized(new { message = error });

        AppendRefreshTokenCookie(tokens.RefreshToken, tokens.RefreshTokenExpiry);
        var isMobile = Request.Headers["X-Client-Type"] == "mobile";

        return Ok(new LoginResponseDto(
            tokens.AccessToken,
            isMobile ? tokens.RefreshToken : null
        ));
    }

    [HttpPost("save-fcm-token")]
    [Authorize]
    public async Task<IActionResult> SaveFcmToken([FromBody] SaveTokenDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null) return NotFound();

        user.FcmToken = dto.Token;
        await _userManager.UpdateAsync(user);
        return Ok(new { message = "Token saved successfully." });
    }

    [HttpPost("logout")]
    [Authorize]
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
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailDto dto)
    {
        var (success, error) = await _authService.ConfirmEmailAsync(dto);
        if (!success) return BadRequest(new { message = error });
        return Ok(new { message = "Email confirmed successfully." });
    }

    [HttpPost("forget-password")]
    public async Task<IActionResult> ForgetPassword([FromBody] ForgetPasswordDto dto)
    {
        await _authService.ForgotPasswordAsync(dto.Email);
        return Ok(new { message = "If registered, a reset link has been dispatched." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        var (success, error) = await _authService.ResetPasswordAsync(dto);
        if (!success) return BadRequest(new { message = error });
        return Ok(new { message = "Password updated successfully." });
    }

    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation([FromBody] ForgetPasswordDto dto)
    {
        await _authService.ResendConfirmationEmailAsync(dto.Email);
        return Ok(new { message = "If unverified, a verification link has been dispatched." });
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            user = User.Identity?.Name,
            claims = User.Claims.Select(c => new { c.Type, c.Value })
        });
    }

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