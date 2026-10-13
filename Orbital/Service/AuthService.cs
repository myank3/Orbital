using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Orbital.Data;
using Orbital.DTOs;
using Orbital.Models;

namespace Orbital.Service;

public class AuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly EmailService _emailService;
    private readonly EmailTemplateService _emailTemplate;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ApplicationDbContext db,
        IConfiguration config,
        EmailService emailService,
        EmailTemplateService emailTemplate)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
        _config = config;
        _emailService = emailService;
        _emailTemplate = emailTemplate;
    }

    // =========================================================
    // 1. REGISTER
    // =========================================================
    public async Task<(bool Success, string? Error)> RegisterAsync(RegisterDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || !IsValidEmail(dto.Email))
            return (false, "Invalid email address format.");

        if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6)
            return (false, "Password must be at least 6 characters long.");

        var cleanHandle = dto.UserHandle.Trim().ToLowerInvariant().Replace("@orbit", "");
        if (cleanHandle.Length < 3 || !cleanHandle.All(c => char.IsLetterOrDigit(c) || c == '_'))
            return (false, "Handle must be at least 3 characters and contain only letters, numbers, or underscores.");

        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.UserHandle == cleanHandle && u.Realm == "orbit"))
            return (false, "Handle is already claimed.");

        if (await _userManager.FindByEmailAsync(normalizedEmail) != null)
            return (false, "An account with this email address already exists.");

        var user = new ApplicationUser
        {
            UserName = $"{cleanHandle}@orbit",
            Email = normalizedEmail,
            UserHandle = cleanHandle,
            Realm = "orbit",
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? cleanHandle : dto.DisplayName.Trim(),
            EmailConfirmed = false,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return (false, string.Join("; ", result.Errors.Select(e => e.Description)));

        if (!await _roleManager.RoleExistsAsync("User"))
        {
            await _roleManager.CreateAsync(new ApplicationRole { Name = "User" });
        }

        await _userManager.AddToRoleAsync(user, "User");

        try
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var link = GenerateEmailConfirmationLink(user.Email!, token);
            var html = _emailTemplate.GenerateEmailConfirmationTemplate(user.UserHandle, link);
            await _emailService.SendEmailAsync(user.Email!, "Verify your Orbit handle", html);
        }
        catch { }

        return (true, null);
    }

    // =========================================================
    // 2. LOGIN
    // =========================================================
    public async Task<(bool Success, string? Error, InternalTokens? Tokens)> LoginAsync(LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Identifier) || string.IsNullOrWhiteSpace(dto.Password))
            return (false, "Identifier and password are required.", null);

        var identifier = dto.Identifier.Trim().ToLowerInvariant();
        ApplicationUser? user;

        if (identifier.Contains('@') && !identifier.EndsWith("@orbit"))
        {
            user = await _userManager.FindByEmailAsync(identifier);
        }
        else
        {
            var handlePrefix = identifier.Replace("@orbit", "");
            user = await _db.Users.FirstOrDefaultAsync(u => u.UserHandle == handlePrefix && u.Realm == "orbit");
        }

        if (user == null || !await _userManager.CheckPasswordAsync(user, dto.Password))
            return (false, "Invalid credentials.", null);

        if (!user.EmailConfirmed)
            return (false, "Email is not verified. Please verify your email before logging in.", null);

        // Rotate session: this login invalidates any other device
        var tokens = await IssueNewSessionAsync(user);
        return (true, null, tokens);
    }

    // =========================================================
    // 3. REFRESH TOKENS (requires expired access token)
    // =========================================================
    public async Task<(bool Success, string? Error, InternalTokens? Tokens)> RefreshTokensAsync(RefreshTokenRequestDto dto)
    {
        var principal = GetPrincipalFromExpiredToken(dto.AccessToken);
        if (principal == null)
            return (false, "Invalid access token.", null);

        var jti = principal.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(jti) || string.IsNullOrEmpty(userId))
            return (false, "Token missing required claims.", null);

        var storedToken = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == dto.RefreshToken);

        if (storedToken == null || storedToken.IsUsed || storedToken.IsRevoked ||
            storedToken.ExpiryDate < DateTime.UtcNow || storedToken.JwtId != jti || storedToken.UserId != userId)
        {
            return (false, "Refresh token is invalid or expired.", null);
        }

        storedToken.IsUsed = true;
        await _db.SaveChangesAsync();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return (false, "User associated with token no longer exists.", null);

        // Refresh does NOT rotate the session — it's the same login continuing.
        var tokens = await ContinueSessionAsync(user);
        return (true, null, tokens);
    }

    // =========================================================
    // 3b. REFRESH TOKENS (cookie-only, no access token)
    // =========================================================
    public async Task<(bool Success, string? Error, InternalTokens? Tokens)> RefreshTokensByValueAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return (false, "Missing refresh token.", null);

        var storedToken = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        if (storedToken == null ||
            storedToken.IsUsed ||
            storedToken.IsRevoked ||
            storedToken.ExpiryDate < DateTime.UtcNow)
        {
            return (false, "Refresh token is invalid or expired.", null);
        }

        var user = await _userManager.FindByIdAsync(storedToken.UserId);
        if (user == null)
            return (false, "User associated with token no longer exists.", null);

        storedToken.IsUsed = true;
        await _db.SaveChangesAsync();

        // Refresh does NOT rotate the session — it's the same login continuing.
        var tokens = await ContinueSessionAsync(user);
        return (true, null, tokens);
    }

    // =========================================================
    // 4. CONFIRM EMAIL
    // =========================================================
    public async Task<(bool Success, string? Error)> ConfirmEmailAsync(ConfirmEmailDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Token))
            return (false, "Missing email or confirmation token.");

        var user = await _userManager.FindByEmailAsync(dto.Email.Trim().ToLowerInvariant());
        if (user == null)
            return (false, "Invalid verification request.");

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(dto.Token));
        }
        catch
        {
            return (false, "Malformed confirmation token.");
        }

        var result = await _userManager.ConfirmEmailAsync(user, decodedToken);
        if (!result.Succeeded)
            return (false, string.Join("; ", result.Errors.Select(e => e.Description)));

        return (true, null);
    }

    // =========================================================
    // 5. FORGOT PASSWORD
    // =========================================================
    public async Task ForgotPasswordAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return;

        var user = await _userManager.FindByEmailAsync(email.Trim().ToLowerInvariant());
        if (user == null) return;

        try
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var link = GeneratePasswordResetLink(user.Email!, token);
            var html = _emailTemplate.GeneratePasswordResetTemplate(user.UserHandle, link);
            await _emailService.SendEmailAsync(user.Email!, "Reset your Orbit password", html);
        }
        catch { }
    }

    // =========================================================
    // 6. RESET PASSWORD
    // =========================================================
    public async Task<(bool Success, string? Error)> ResetPasswordAsync(ResetPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.NewPassword))
            return (false, "All fields are required.");

        var user = await _userManager.FindByEmailAsync(dto.Email.Trim().ToLowerInvariant());
        if (user == null)
            return (false, "Invalid request.");

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(dto.Token));
        }
        catch
        {
            return (false, "Malformed password reset token.");
        }

        var result = await _userManager.ResetPasswordAsync(user, decodedToken, dto.NewPassword);
        if (!result.Succeeded)
            return (false, string.Join("; ", result.Errors.Select(e => e.Description)));

        await _userManager.UpdateSecurityStampAsync(user);
        return (true, null);
    }

    // =========================================================
    // 7. RESEND CONFIRMATION
    // =========================================================
    public async Task ResendConfirmationEmailAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return;

        var user = await _userManager.FindByEmailAsync(email.Trim().ToLowerInvariant());
        if (user == null || user.EmailConfirmed) return;

        try
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var link = GenerateEmailConfirmationLink(user.Email!, token);
            var html = _emailTemplate.GenerateEmailConfirmationTemplate(user.UserHandle, link);
            await _emailService.SendEmailAsync(user.Email!, "Verify your Orbit handle", html);
        }
        catch { }
    }

    // =========================================================
    // PUBLIC — Issue a fresh session (used by LoginAsync and Google callback)
    // Rotates CurrentSessionId, which invalidates any other device currently
    // holding a token with the old session ID.
    // =========================================================
    public async Task<InternalTokens> IssueNewSessionAsync(ApplicationUser user)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        user.CurrentSessionId = sessionId;
        await _userManager.UpdateAsync(user);
        return await GenerateAuthTokensAsync(user, sessionId);
    }

    // =========================================================
    // PRIVATE — Continue an existing session on token refresh.
    // Does NOT rotate the session ID — refresh is the same login continuing.
    // If a legacy user has no session yet, one is created now.
    // =========================================================
    private async Task<InternalTokens> ContinueSessionAsync(ApplicationUser user)
    {
        var sessionId = user.CurrentSessionId;
        if (string.IsNullOrEmpty(sessionId))
        {
            sessionId = Guid.NewGuid().ToString("N");
            user.CurrentSessionId = sessionId;
            await _userManager.UpdateAsync(user);
        }
        return await GenerateAuthTokensAsync(user, sessionId);
    }

    // =========================================================
    // PRIVATE HELPERS & TOKEN GENERATION
    // =========================================================
    private static bool IsValidEmail(string email)
    {
        try { return new MailAddress(email).Address.Equals(email, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private string GenerateEmailConfirmationLink(string email, string token)
    {
        var frontendUrl = _config["Client:BaseUrl"] ?? "http://localhost:7167";
        var encodedEmail = Uri.EscapeDataString(email);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return $"{frontendUrl}/confirm-email?email={encodedEmail}&token={encodedToken}";
    }

    private string GeneratePasswordResetLink(string email, string token)
    {
        var frontendUrl = _config["Client:BaseUrl"] ?? "http://localhost:7167";
        var encodedEmail = Uri.EscapeDataString(email);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return $"{frontendUrl}/reset-password?email={encodedEmail}&token={encodedToken}";
    }

    private async Task<InternalTokens> GenerateAuthTokensAsync(ApplicationUser user, string sessionId)
    {
        var jwtId = Guid.NewGuid().ToString();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var tokenExpiry = DateTime.UtcNow.AddMinutes(15);

        var roles = await _userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, jwtId),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, $"{user.UserHandle}@{user.Realm}"),
            new("handle", user.UserHandle),
            new("realm", user.Realm),
            new("session_id", sessionId)
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = tokenExpiry,
            Issuer = _config["Jwt:Issuer"],
            Audience = _config["Jwt:Audience"],
            SigningCredentials = creds
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var securityToken = tokenHandler.CreateToken(tokenDescriptor);
        var accessToken = tokenHandler.WriteToken(securityToken);

        // Refresh token: 90 days so the user stays logged in until they log out
        var refreshTokenExpiry = DateTime.UtcNow.AddDays(90);
        var refreshTokenString = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        var refreshToken = new RefreshToken
        {
            Token = refreshTokenString,
            JwtId = jwtId,
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiryDate = refreshTokenExpiry
        };

        await _db.RefreshTokens.AddAsync(refreshToken);
        await _db.SaveChangesAsync();

        return new InternalTokens(accessToken, refreshTokenString, refreshTokenExpiry);
    }

    private ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _config["Jwt:Issuer"],
            ValidAudience = _config["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!)),
            ValidateLifetime = false
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);
            if (securityToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                return null;
            }
            return principal;
        }
        catch
        {
            return null;
        }
    }
}