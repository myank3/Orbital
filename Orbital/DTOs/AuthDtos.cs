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

// 🔥 UPDATED: Changed 'Token' to 'AccessToken' and added 'RefreshToken'
public record LoginResponseDto(
    string AccessToken,
    string? RefreshToken = null
);

// Backward-compatibility alias
public record AuthResponseDto(
    string AccessToken
);

public record RefreshTokenRequestDto(
    string AccessToken,
    string RefreshToken
);

// 🔥 NEW: Needed for the mobile refresh logic
public record RefreshRequestDto(
    string? RefreshToken
);

// 🔥 NEW: Needed to receive the Push Notification token
public record SaveTokenDto(
    string Token
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