using CourierApi.Contracts;

namespace CourierApi.Services;

public sealed class ParsedRateWorkbook
{
    public string FileName { get; init; } = string.Empty;
    public string FileHash { get; init; } = string.Empty;
    public string CarrierCode { get; init; } = "FEDEX";
    public List<ParsedRateCard> RateCards { get; } = [];
    public List<ParsedCountryZone> CountryZones { get; } = [];
    public List<ParsedSurcharge> Surcharges { get; } = [];
    public List<RateImportIssueDto> Issues { get; } = [];
    public List<RateSheetPreviewDto> SheetSummaries { get; } = [];

    public int ErrorCount => Issues.Count(x => x.Severity == "Error");
    public int WarningCount => Issues.Count(x => x.Severity == "Warning");
}

public sealed class ParsedRateCard
{
    public string CarrierCode { get; init; } = "FEDEX";
    public string Direction { get; init; } = string.Empty;
    public string ServiceCode { get; init; } = string.Empty;
    public string PackageType { get; init; } = string.Empty;
    public string Currency { get; init; } = "VND";
    public DateTime EffectiveFrom { get; init; }
    public decimal FuelMultiplier { get; set; } = 1m;
    public decimal VatMultiplier { get; set; } = 1m;
    public string SourceSheet { get; init; } = string.Empty;
    public List<ParsedRateBand> Bands { get; } = [];
}

public sealed record ParsedRateBand(
    string ZoneCode,
    decimal WeightFrom,
    decimal WeightTo,
    string RateUnit,
    decimal Amount,
    int SourceRow);

public sealed record ParsedCountryZone(
    string CarrierCode,
    string Direction,
    string CountryName,
    string ServiceCode,
    string ZoneCode,
    bool IsSupported,
    string SourceSheet,
    int SourceRow);

public sealed record ParsedSurcharge(
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
