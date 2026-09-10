using System.ComponentModel.DataAnnotations;

namespace CourierApi.Contracts;

public sealed record SurchargeDto(
    Guid Id,
    Guid ImportBatchId,
    string CarrierCode,
    string Direction,
    string ServiceCode,
    string? ZoneCode,
    string Code,
    string Name,
    string CalculationType,
    decimal Amount,
    decimal? Multiplier,
    DateTime EffectiveFrom,
    bool IsInformational,
    string SourceSheet,
    string SourceCell);

public sealed class UpsertSurchargeRequest
{
    [Required]
    public Guid ImportBatchId { get; set; }

    [MaxLength(20)]
    public string CarrierCode { get; set; } = "FEDEX";

    [Required, MaxLength(10)]
    public string Direction { get; set; } = string.Empty;

    [Required, MaxLength(10)]
    public string ServiceCode { get; set; } = string.Empty;

    [MaxLength(10)]
    public string? ZoneCode { get; set; }

    [Required, MaxLength(40)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string CalculationType { get; set; } = "Fixed";

    public decimal Amount { get; set; }
    public decimal? Multiplier { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public bool IsInformational { get; set; }
}

public sealed record CountryOptionDto(
    string CountryName,
    string ZoneCode,
    string Direction,
    string ServiceCode);

public sealed record RateBatchOptionDto(
    Guid Id,
    string FileName,
    DateTime? EffectiveFrom,
    DateTime? CommittedAtUtc,
    int SurchargeCount);
