namespace CourierApi.Contracts;

public sealed record CustomerListItemDto(
    Guid Id,
    string CustomerCode,
    string CompanyName,
    string? TaxCode,
    string? BillingEmail,
    decimal CreditLimit,
    string BillingCycle,
    string? RatePlanCode,
    Guid? SalesUserId,
    string? SalesName,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record UpsertCustomerRequest(
    string CustomerCode,
    string CompanyName,
    string? TaxCode,
    string? BillingEmail,
    decimal CreditLimit,
    string BillingCycle,
    string? RatePlanCode,
    bool IsActive = true);
