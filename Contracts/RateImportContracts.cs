namespace CourierApi.Contracts;

public sealed record RateImportIssueDto(
    string Severity,
    string Code,
    string Message,
    string? Sheet,
    int? Row,
    string? Cell);

public sealed record RateSheetPreviewDto(
    string SheetName,
    string Direction,
    string ServiceCode,
    DateTime EffectiveFrom,
    int RateCardCount,
    int RateBandCount,
    IReadOnlyList<string> PackageTypes);

public sealed record RateImportPreviewResponse(
    string ImportToken,
    string FileName,
    string FileHash,
    string CarrierCode,
    DateTime? EarliestEffectiveFrom,
    DateTime? LatestEffectiveFrom,
    int RateCardCount,
    int RateBandCount,
    int CountryZoneCount,
    int SurchargeCount,
    int WarningCount,
    int ErrorCount,
    IReadOnlyList<RateSheetPreviewDto> Sheets,
    IReadOnlyList<RateImportIssueDto> Issues);

public sealed record RateImportCommitRequest(
    string ImportToken,
    string RateSetCode,
    Guid CustomerId,
    Guid? SalesUserId = null,
    bool Activate = true,
    bool AllowDuplicateFile = false);

public sealed record RateImportCommitResponse(
    Guid BatchId,
    string Status,
    int RateCardCount,
    int RateBandCount,
    int CountryZoneCount,
    int SurchargeCount,
    DateTime CommittedAtUtc);

public sealed record RateImportHistoryDto(
    Guid Id,
    string CarrierCode,
    string? RateSetCode,
    Guid? CustomerId,
    string? CustomerCode,
    string? CustomerName,
    Guid? SalesUserId,
    string? SalesName,
    string FileName,
    string Status,
    DateTime? EffectiveFrom,
    int RateCardCount,
    int RateBandCount,
    int CountryZoneCount,
    int SurchargeCount,
    int WarningCount,
    int ErrorCount,
    DateTime CreatedAtUtc,
    DateTime? CommittedAtUtc);

public sealed record RateImportRenameRequest(string RateSetCode);

public sealed record RateImportRenameResponse(
    string RateSetCode,
    int UpdatedBatchCount,
    int UpdatedRateCardCount,
    int UpdatedRateBandCount,
    int UpdatedCountryZoneCount,
    int UpdatedSurchargeCount);

public sealed record RateSetCodeOptionDto(
    string RateSetCode,
    Guid LatestBatchId,
    string CarrierCode,
    Guid? CustomerId,
    string? CustomerCode,
    string? CustomerName,
    Guid? SalesUserId,
    string? SalesName,
    DateTime? EffectiveFrom,
    int RateBandCount,
    int RateCardCount,
    DateTime CreatedAtUtc);

public sealed record RateImportCloneRequest(
    Guid SourceBatchId,
    string NewRateSetCode,
    Guid CustomerId,
    Guid? SalesUserId = null,
    decimal AdjustmentPercent = 0,
    bool Activate = false);

public sealed record RateImportCloneResponse(
    Guid BatchId,
    string SourceRateSetCode,
    string NewRateSetCode,
    decimal AdjustmentPercent,
    int RateCardCount,
    int RateBandCount,
    int CountryZoneCount,
    int SurchargeCount,
    DateTime CommittedAtUtc);

public sealed record RateImportDeleteResponse(
    Guid BatchId,
    string? RateSetCode,
    int RateCardCount,
    int RateBandCount,
    int UnlinkedQuoteCount);

public sealed record RateSheetOptionDto(
    string Direction,
    string ServiceCode,
    string PackageType,
    string SourceSheet);

public sealed record RateMatrixRowDto(
    decimal WeightFrom,
    decimal WeightTo,
    string RateUnit,
    Dictionary<string, decimal> Amounts);

public sealed record RateMatrixDto(
    string Direction,
    string ServiceCode,
    string PackageType,
    string SourceSheet,
    string Currency,
    decimal FuelMultiplier,
    decimal VatMultiplier,
    DateTime EffectiveFrom,
    IReadOnlyList<string> Zones,
    IReadOnlyList<RateMatrixRowDto> Rows);

public sealed record RateCountryZoneDto(
    string CountryName,
    string ZoneCode,
    string Direction,
    string ServiceCode,
    bool IsSupported);

public sealed record RateBatchViewDto(
    Guid BatchId,
    string CarrierCode,
    string? RateSetCode,
    string FileName,
    string Status,
    DateTime? EffectiveFrom,
    Guid? CustomerId,
    string? CustomerCode,
    string? CustomerName,
    Guid? SalesUserId,
    string? SalesName,
    int RateBandCount,
    int CountryZoneCount,
    int SurchargeCount,
    IReadOnlyList<RateSheetOptionDto> Sheets,
    RateMatrixDto? Matrix,
    IReadOnlyList<RateCountryZoneDto> Countries,
    IReadOnlyList<SurchargeDto> Surcharges);

public sealed record RateLookupResponse(
    string CarrierCode,
    string Direction,
    string ServiceCode,
    string PackageType,
    string ZoneCode,
    decimal InputWeight,
    decimal BillableWeight,
    string RateUnit,
    decimal UnitRate,
    decimal BaseFreight,
    decimal DemandSurcharge,
    decimal FuelMultiplier,
    decimal VatMultiplier,
    decimal EstimatedAllIn,
    DateTime EffectiveFrom,
    Guid ImportBatchId);
