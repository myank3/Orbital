namespace Orbital.DTOs;

public record RegisterDto(
    string UserHandle,
    string DisplayName,
    string Email,
    string Password
);

public record LoginDto(
    string Identifier,
    string Password
);

public record LoginResponseDto(
    string Token
);

// Backward-compatibility alias for Swagger / older references
public record AuthResponseDto(
    string Token
);

public record RefreshTokenRequestDto(
    string AccessToken,
    string RefreshToken
);

public record InternalTokens(
    string AccessToken,
    string RefreshToken,
    DateTime RefreshTokenExpiry
);

public record ConfirmEmailDto(
    string Email,
    string Token
);

public record ForgetPasswordDto(
    string Email
);

public record ResetPasswordDto(
    string Email,
    string Token,
    string NewPassword
);