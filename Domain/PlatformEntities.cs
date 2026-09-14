namespace CourierApi.Domain;

public sealed class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Department { get; set; } = "Admin"; // Roles: Admin, Staff, Customer, Sales, RateHub
    public Guid? CustomerId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    public Customer? Customer { get; set; }
}

public sealed class Customer
{
    public Guid Id { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public string? BillingEmail { get; set; }
    public string? Address { get; set; }
    public decimal CreditLimit { get; set; }
    public string BillingCycle { get; set; } = "Monthly";
    public string? RatePlanCode { get; set; }
    public Guid? SalesUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public AppUser? SalesUser { get; set; }
    public ICollection<Booking> Bookings { get; set; } = [];
    public ICollection<Job> Jobs { get; set; } = [];
}

public sealed class CompanyInformation
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CustomerAddress
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string AddressName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? StateProvince { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "VN";
    public bool IsResidential { get; set; }
    public bool IsDefaultSender { get; set; }
    public bool IsDefaultReceiver { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class Booking
{
    public Guid Id { get; set; }
    public string BookingNo { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = "";
    public string ServiceCode { get; set; } = string.Empty;
    public string FromCountryCode { get; set; } = string.Empty;
    public string ToCountryCode { get; set; } = string.Empty;
    public DateTime ShipDate { get; set; }
    public Guid? SenderAddressId { get; set; }
    public Guid? ReceiverAddressId { get; set; }
    public string Status { get; set; } = "Submitted";
    public string? CustomerNote { get; set; }
    public bool RequiresComplianceReview { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Customer Customer { get; set; } = null!;
    public CustomerAddress? SenderAddress { get; set; }
    public CustomerAddress? ReceiverAddress { get; set; }
    public ICollection<BookingPackage> Packages { get; set; } = [];
    public ICollection<BookingCommodity> Commodities { get; set; } = [];
    public ICollection<BookingQuote> Quotes { get; set; } = [];
    public ICollection<JobDocument> Documents { get; set; } = [];
    public Job? Job { get; set; }
}

public sealed class BookingPackage
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public int PackageNo { get; set; }
    public string PackageType { get; set; } = "Package";
    public int Pieces { get; set; }
    public decimal ActualWeight { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal DimWeight { get; set; }
    public decimal ChargeableWeight { get; set; }
    public bool IsStackable { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public sealed class BookingCommodity
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string DescriptionEnglish { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitValue { get; set; }
    public string Currency { get; set; } = "USD";
    public string? HsCode { get; set; }
    public string? CountryOfOrigin { get; set; }
    public bool ContainsBattery { get; set; }
    public bool ContainsLiquid { get; set; }
    public bool IsFood { get; set; }
    public bool IsDangerousGoods { get; set; }
}

public sealed class JobDocument
{
    public Guid Id { get; set; }
    public Guid? JobId { get; set; }
    public Guid? BookingId { get; set; }
    public string DocumentType { get; set; } = "Attachment";
    public string FileName { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string? FileHash { get; set; }
    public string? UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class BookingQuote
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public int VersionNo { get; set; }
    public Guid? RateCardId { get; set; }
    public string ZoneCode { get; set; } = "A";
    public decimal ChargeableWeight { get; set; }
    public decimal Freight { get; set; }
    public decimal FuelSurcharge { get; set; }
    public decimal DemandSurcharge { get; set; }
    public decimal HandlingCharge { get; set; }
    public decimal OtherCharge { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public string? CalculationSnapshot { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
}

public sealed class Job
{
    public Guid Id { get; set; }
    public string JobNo { get; set; } = string.Empty;
    public Guid BookingId { get; set; }
    public Guid CustomerId { get; set; }
    public string CarrierCode { get; set; } = "FEDEX";
    public string Direction { get; set; } = string.Empty;
    public string ServiceCode { get; set; } = string.Empty;
    public string? Awb { get; set; }
    public string Status { get; set; } = "AwbCreated";
    public string BillingStatus { get; set; } = "Ready";
    public decimal ActualWeight { get; set; }
    public decimal ChargeableWeight { get; set; }
    public DateTime? PickupDate { get; set; }
    public DateTime? DeliveredDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Customer Customer { get; set; } = null!;
    public Booking Booking { get; set; } = null!;
    public ICollection<JobChargeLine> ChargeLines { get; set; } = [];
    public ICollection<TrackingEvent> TrackingEvents { get; set; } = [];
}

public sealed class JobChargeLine
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string ChargeKind { get; set; } = "Debit";
    public string ChargeCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public decimal TaxRate { get; set; } = 0.08m;
    public string ApprovalStatus { get; set; } = "Approved";
    public string? SourceType { get; set; }
    public string? SourceReference { get; set; }
    public Guid? BillingBatchId { get; set; }
    public bool IsLocked { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class TrackingEvent
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string EventCode { get; set; } = string.Empty;
    public string EventTitle { get; set; } = string.Empty;
    public string? EventDescription { get; set; }
    public string? Location { get; set; }
    public DateTime EventTime { get; set; }
    public string SourceType { get; set; } = "System";
    public string? RawPayload { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class PickupTask
{
    public Guid Id { get; set; }
    public string PickupNo { get; set; } = string.Empty;
    public Guid? JobId { get; set; }
    public Guid BookingId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string Status { get; set; } = "Assigned";
    public int ExpectedPieces { get; set; }
    public int? ActualPieces { get; set; }
    public string? SignatureFileUrl { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ConfirmationCount { get; set; }
    public DateTime? LastConfirmedAt { get; set; }
    public Guid? LastConfirmedByUserId { get; set; }

    public Booking Booking { get; set; } = null!;
    public Job? Job { get; set; }
    public ICollection<PickupEvidence> Evidence { get; set; } = [];
    public ICollection<PickupConfirmationLog> ConfirmationLogs { get; set; } = [];
}

public sealed class CourierLocationTrack
{
    public Guid Id { get; set; }
    public Guid PickupTaskId { get; set; }
    public Guid UserId { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }

    public PickupTask PickupTask { get; set; } = null!;
}

public sealed class PickupEvidence
{
    public Guid Id { get; set; }
    public Guid PickupTaskId { get; set; }
    public string Kind { get; set; } = "Photo";
    public string FileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";
    public bool IsCurrent { get; set; } = true;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public PickupTask PickupTask { get; set; } = null!;
    public PickupEvidenceBlob? Blob { get; set; }
}

public sealed class PickupEvidenceBlob
{
    public Guid EvidenceId { get; set; }
    public byte[] FileContent { get; set; } = [];

    public PickupEvidence Evidence { get; set; } = null!;
}

public sealed class BookingPdf
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public string DownloadToken { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public DateTime CreatedAt { get; set; }

    public Booking Booking { get; set; } = null!;
    public BookingPdfBlob? Blob { get; set; }
}

public sealed class BookingPdfBlob
{
    public Guid BookingPdfId { get; set; }
    public byte[] FileContent { get; set; } = [];

    public BookingPdf BookingPdf { get; set; } = null!;
}

public sealed class PickupConfirmationLog
{
    public Guid Id { get; set; }
    public Guid PickupTaskId { get; set; }
    public int SequenceNo { get; set; }
    public string Action { get; set; } = "Confirmed";
    public string Detail { get; set; } = string.Empty;
    public int PhotoCountAdded { get; set; }
    public bool SignatureChanged { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public PickupTask PickupTask { get; set; } = null!;
}

public sealed class Invoice
{
    public Guid Id { get; set; }
    public string? InvoiceNo { get; set; }
    public Guid BillingBatchId { get; set; }
    public Guid CustomerId { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string Status { get; set; } = "Issued";
    public string? ProviderCode { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? PdfFileUrl { get; set; }
    public string? XmlFileUrl { get; set; }
    public DateTime? SignedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Customer Customer { get; set; } = null!;
}

public sealed class PasswordResetOtp
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string OtpHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class PackageScaleBatch
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int ExcelRowCount { get; set; }
    public int MatchedAwbCount { get; set; }
    public int UnmatchedAwbCount { get; set; }
    public int QuoteUpdatedCount { get; set; }
    public Guid? ImportedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<PackageScaleLine> Lines { get; set; } = [];
}

public sealed class PackageScaleLine
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid JobId { get; set; }
    public Guid BookingId { get; set; }
    public Guid? DeclaredPackageId { get; set; }
    public string Awb { get; set; } = string.Empty;
    public int PieceNo { get; set; }
    public int ExcelRow { get; set; }
    public string? ScaleTicketNo { get; set; }
    public decimal ActualWeight { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal DimWeight { get; set; }
    public decimal ChargeableWeight { get; set; }
    public string PackageType { get; set; } = "Package";
    public DateTime? WeighedAt { get; set; }
    public string? WeighedBy { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public PackageScaleBatch Batch { get; set; } = null!;
}
