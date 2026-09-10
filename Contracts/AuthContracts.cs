namespace CourierApi.Contracts;

public sealed record LoginRequest(string Email, string Password, string Department);

public sealed record LoginResponse(
    string Token,
    Guid UserId,
    string Email,
    string DisplayName,
    string Department,
    IReadOnlyList<string> Departments,
    Guid? CustomerId,
    string? CustomerName,
    DateTime ExpiresAtUtc);

public sealed record ForgotPasswordRequest(string Email, string Department);

public sealed record ResetPasswordRequest(
    string Email,
    string Department,
    string Otp,
    string NewPassword);

public sealed record AuthUserDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string Department,
    IReadOnlyList<string> Departments,
    Guid? CustomerId,
    string? CustomerName);

public sealed record LoginAccountDto(
    string Email,
    string DisplayName,
    string? CustomerName);
