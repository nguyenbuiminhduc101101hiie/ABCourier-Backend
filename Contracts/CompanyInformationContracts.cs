namespace CourierApi.Contracts;

public sealed record CompanyInformationDto(
    Guid? Id,
    string CompanyName,
    string? TaxCode,
    string? Address,
    string? Phone,
    string? Email,
    DateTime? UpdatedAt);

public sealed record UpsertCompanyInformationRequest(
    string CompanyName,
    string? TaxCode,
    string? Address,
    string? Phone,
    string? Email);
