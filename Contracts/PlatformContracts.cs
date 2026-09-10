namespace CourierApi.Contracts;

public sealed record DashboardDto(
    int NewBookings,
    int PendingQuotes,
    int Exceptions,
    decimal Unbilled,
    decimal Revenue,
    decimal Cost,
    decimal GrossProfit);

public sealed record BookingSummaryDto(
    string Id,
    string CustomerName,
    string Direction,
    string Service,
    string Route,
    decimal ChargeableWeight,
    decimal QuoteTotal,
    string Status,
    int DocumentCount,
    string Note,
    DateTime CreatedAt,
    DateTime ShipDate,
    decimal? ScaleQuoteTotal = null,
    string? CarrierCode = null);

public sealed record BookingAddressDto(
    string Name,
    string? Company,
    string? Phone,
    string? Email,
    string Line1,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode,
    bool Residential);

public sealed record BookingPackageDto(
    int Pieces,
    decimal ActualWeight,
    decimal Length,
    decimal Width,
    decimal Height,
    string? PackageType,
    decimal DimWeight = 0,
    decimal ChargeableWeight = 0,
    bool IsStackable = true);

public sealed record BookingCommodityDto(
    string ItemName,
    string DescriptionEnglish,
    decimal Quantity,
    decimal UnitValue,
    string? Currency,
    string? HsCode,
    bool ContainsBattery,
    bool ContainsLiquid,
    bool IsFood,
    bool IsDangerousGoods,
    string? CountryOfOrigin = null);

public sealed record BookingQuoteDto(
    string Zone,
    decimal ChargeableWeight,
    decimal Freight,
    decimal Fuel,
    decimal Demand,
    decimal Handling,
    decimal Vat,
    decimal Total,
    string? RateSetCode = null,
    string? RateSource = null,
    string? CarrierCode = null);

public sealed record BookingQuotePreviewRequest(
    string Direction,
    string Service,
    string FromCountry,
    string ToCountry,
    DateTime ShipDate,
    decimal ActualWeight,
    decimal Length,
    decimal Width,
    decimal Height,
    string? PackageType = null,
    bool Residential = false,
    string? CarrierCode = null,
    IReadOnlyList<BookingPackageDto>? Packages = null);

public sealed record BookingRateQuoteDto(
    string Zone,
    decimal ChargeableWeight,
    decimal Freight,
    decimal Fuel,
    decimal Demand,
    decimal Handling,
    decimal Vat,
    decimal Total,
    Guid? RateCardId,
    Guid? ImportBatchId,
    string? RateSetCode,
    string RateSource,
    string CarrierCode,
    DateTime EffectiveFrom,
    string? SnapshotJson = null,
    string RateUnit = "Fixed",
    decimal UnitRate = 0,
    decimal FuelMultiplier = 1,
    decimal VatMultiplier = 1,
    string ServiceCode = "");

public sealed record BookingDetailDto(
    string Id,
    string CustomerName,
    string Direction,
    string Service,
    string FromCountry,
    string ToCountry,
    DateTime ShipDate,
    string Status,
    string Note,
    bool RequiresComplianceReview,
    bool CanEdit,
    BookingAddressDto? Sender,
    BookingAddressDto? Receiver,
    IReadOnlyList<BookingPackageDto> Packages,
    IReadOnlyList<BookingCommodityDto> Commodities,
    IReadOnlyList<string> Documents,
    BookingQuoteDto? Quote,
    DateTime CreatedAt,
    BookingQuoteDto? ScaleQuote = null,
    string? CarrierCode = null);

public sealed record UpdateBookingDetailsRequest(
    string? Note,
    BookingAddressDto? Sender,
    BookingAddressDto? Receiver,
    BookingCommodityDto? Commodity,
    IReadOnlyList<string>? Documents);

public sealed record JobSummaryDto(
    string Id,
    string BookingId,
    string CustomerName,
    string Awb,
    string Direction,
    string Service,
    string Status,
    decimal Revenue,
    decimal Cost,
    decimal GrossProfit,
    decimal Margin,
    string BillingStatus);

public sealed record CreateBookingRequest(
    string CustomerName,
    string Direction,
    string Service,
    string FromCountry,
    string ToCountry,
    DateTime ShipDate,
    decimal ActualWeight,
    decimal Length,
    decimal Width,
    decimal Height,
    string CommodityName,
    bool RequiresComplianceReview,
    string? Note,
    BookingAddressDto? Sender,
    BookingAddressDto? Receiver,
    IReadOnlyList<BookingPackageDto>? Packages,
    IReadOnlyList<BookingCommodityDto>? Commodities,
    IReadOnlyList<string>? Documents,
    string? CarrierCode = null);

public sealed record CreateBookingResult(
    string BookingId,
    decimal DimWeight,
    decimal ChargeableWeight,
    string Zone,
    decimal QuoteTotal,
    string Status);

public sealed record CreateJobRequest(string BookingId, Guid? AssignedUserId);
public sealed record CreateJobResult(string JobId, string Awb, string Status);
public sealed record UpdateJobAwbRequest(string Awb);

public sealed record CreateInvoiceRequest(string CustomerName, IReadOnlyList<string> JobIds);
public sealed record CreateInvoiceResult(
    string InvoiceId,
    decimal Subtotal,
    decimal Vat,
    decimal Total,
    string PdfFile,
    string XmlFile,
    string Status);

public sealed record PickupEvidenceDto(
    string Id,
    string Kind,
    string FileName,
    string Url,
    DateTime CreatedAt,
    bool IsCurrent);

public sealed record PickupConfirmationLogDto(
    int SequenceNo,
    string Action,
    string Detail,
    DateTime CreatedAt,
    string? CreatedByName,
    int PhotoCountAdded,
    bool SignatureChanged);

public sealed record PickupTaskDto(
    string Id,
    string BookingId,
    string CustomerName,
    string Route,
    int ExpectedPieces,
    DateTime ScheduledAt,
    string Status,
    string? JobId,
    string? Awb,
    string? AssignedUserId,
    string? AssignedUserName,
    string? PickupAddress,
    IReadOnlyList<BookingPackageDto> Packages,
    int ConfirmationCount,
    DateTime? LastConfirmedAt,
    PickupEvidenceDto? Signature,
    IReadOnlyList<PickupEvidenceDto> Photos,
    IReadOnlyList<PickupConfirmationLogDto> ConfirmationLogs);

public sealed record UpdateBookingStatusRequest(string Status, string? Note);

public sealed record CustomerProfileDto(
    Guid CustomerId,
    string CustomerCode,
    string CompanyName,
    string? TaxCode,
    string? BillingEmail,
    decimal CreditLimit,
    string BillingCycle,
    string? RatePlanCode);

public sealed record InvoiceSummaryDto(
    string Id,
    string CustomerName,
    string Period,
    decimal Subtotal,
    decimal Vat,
    decimal Total,
    string Status,
    DateTime IssueDate,
    DateTime? DueDate,
    string? PdfUrl,
    string? XmlUrl);

public sealed record UpdatePickupTaskRequest(string Status, int? ActualPieces);

public sealed record ScaleImportAwbResult(
    string Awb,
    string? JobNo,
    string? BookingNo,
    string Status,
    int PieceCount,
    decimal? OldQuoteTotal,
    decimal? NewQuoteTotal,
    decimal? NewChargeableWeight,
    string? Message);

public sealed record ScaleImportResult(
    Guid BatchId,
    string FileName,
    int ExcelRowCount,
    int MatchedAwbCount,
    int UnmatchedAwbCount,
    int QuoteUpdatedCount,
    IReadOnlyList<ScaleImportAwbResult> Items);
