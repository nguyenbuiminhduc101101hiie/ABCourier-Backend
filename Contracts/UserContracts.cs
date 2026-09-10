namespace CourierApi.Contracts;

public sealed record UserListItemDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Department,
    IReadOnlyList<string> Departments,
    Guid? CustomerId,
    string? CustomerName,
    Guid? SalesUserId,
    string? SalesName,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc);

public sealed record CreateUserRequest(
    string Email,
    string DisplayName,
    string Password,
    string Department,
    Guid? CustomerId,
    Guid? SalesUserId,
    bool IsActive = true);

public sealed record UpdateUserRequest(
    string DisplayName,
    string Department,
    Guid? CustomerId,
    Guid? SalesUserId,
    bool IsActive,
    string? NewPassword);

public sealed record CustomerOptionDto(
    Guid Id,
    string CustomerCode,
    string CompanyName,
    Guid? SalesUserId = null,
    string? SalesName = null);

public sealed record SalesOptionDto(Guid Id, string Email, string DisplayName, bool IsDefault);
