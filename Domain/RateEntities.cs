namespace CourierApi.Domain;

public sealed class RateImportBatch
{
    public Guid Id { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string CarrierCode { get; set; } = "FEDEX";
    /// <summary>Mã hoặc tên do người dùng đặt khi import để tra cứu lại sau này.</summary>
    public string? RateSetCode { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public DateTime? EffectiveFrom { get; set; }
    public Guid? SalesUserId { get; set; }
    public AppUser? SalesUser { get; set; }
    public int RateCardCount { get; set; }
    public int RateBandCount { get; set; }
    public int CountryZoneCount { get; set; }
    public int SurchargeCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CommittedAtUtc { get; set; }

    public ICollection<RateCard> RateCards { get; set; } = new List<RateCard>();
    public ICollection<CountryZoneMapping> CountryZones { get; set; } = new List<CountryZoneMapping>();
    public ICollection<RateSurcharge> Surcharges { get; set; } = new List<RateSurcharge>();
}

public sealed class RateCard
{
    public Guid Id { get; set; }
    public Guid ImportBatchId { get; set; }
    public RateImportBatch ImportBatch { get; set; } = null!;

    public string CarrierCode { get; set; } = "FEDEX";
    public string Direction { get; set; } = string.Empty; // EXPORT / IMPORT
    public string ServiceCode { get; set; } = string.Empty; // IP / IE / IPF / IEF
    public string PackageType { get; set; } = string.Empty; // Envelope / Pak / Package / Freight
    public string Currency { get; set; } = "VND";
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public decimal FuelMultiplier { get; set; } = 1m;
    public decimal VatMultiplier { get; set; } = 1m;
    public bool IsActive { get; set; }
    public string SourceSheet { get; set; } = string.Empty;

    public ICollection<RateBand> Bands { get; set; } = new List<RateBand>();
}

public sealed class RateBand
{
    public Guid Id { get; set; }
    public Guid RateCardId { get; set; }
    public RateCard RateCard { get; set; } = null!;

    public string ZoneCode { get; set; } = string.Empty;
    public decimal WeightFrom { get; set; }
    public decimal WeightTo { get; set; }
    public string RateUnit { get; set; } = "Fixed"; // Fixed / PerKg
    public decimal Amount { get; set; }
    public int SourceRow { get; set; }
}

public sealed class CountryZoneMapping
{
    public Guid Id { get; set; }
    public Guid ImportBatchId { get; set; }
    public RateImportBatch ImportBatch { get; set; } = null!;

    public string CarrierCode { get; set; } = "FEDEX";
    public string Direction { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string ServiceCode { get; set; } = string.Empty;
    public string ZoneCode { get; set; } = string.Empty;
    public bool IsSupported { get; set; }
    public string SourceSheet { get; set; } = string.Empty;
    public int SourceRow { get; set; }
}

public sealed class RateSurcharge
{
    public Guid Id { get; set; }
    public Guid ImportBatchId { get; set; }
    public RateImportBatch ImportBatch { get; set; } = null!;

    public string CarrierCode { get; set; } = "FEDEX";
    public string Direction { get; set; } = string.Empty;
    public string ServiceCode { get; set; } = string.Empty;
    public string? ZoneCode { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CalculationType { get; set; } = string.Empty; // PerKg / Fixed / Multiplier / RawParameter
    public decimal Amount { get; set; }
    public decimal? Multiplier { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public bool IsInformational { get; set; }
    public string SourceSheet { get; set; } = string.Empty;
    public string SourceCell { get; set; } = string.Empty;
}
