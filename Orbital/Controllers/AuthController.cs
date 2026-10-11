using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbital.DTOs;
using Orbital.Service;

namespace Orbital.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
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

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh()
    {
        var refreshToken = Request.Cookies["refreshToken"];

        var authHeader = Request.Headers.Authorization.ToString();
        var accessToken = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..].Trim()
            : null;

        if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(accessToken))
            return Unauthorized(new { message = "Missing access or refresh token." });

        var (success, error, tokens) = await _authService.RefreshTokensAsync(new RefreshTokenRequestDto(accessToken, refreshToken));
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
            SameSite = SameSiteMode.Strict
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

    private void AppendRefreshTokenCookie(string token, DateTime expires)
    {
        Response.Cookies.Append("refreshToken", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = expires
        });
    }
}