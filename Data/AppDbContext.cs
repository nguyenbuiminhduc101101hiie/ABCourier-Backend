using CourierApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CourierApi.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<RateImportBatch> RateImportBatches => Set<RateImportBatch>();
    public DbSet<RateCard> RateCards => Set<RateCard>();
    public DbSet<RateBand> RateBands => Set<RateBand>();
    public DbSet<CountryZoneMapping> CountryZoneMappings => Set<CountryZoneMapping>();
    public DbSet<RateSurcharge> RateSurcharges => Set<RateSurcharge>();

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingPackage> BookingPackages => Set<BookingPackage>();
    public DbSet<BookingCommodity> BookingCommodities => Set<BookingCommodity>();
    public DbSet<BookingQuote> BookingQuotes => Set<BookingQuote>();
    public DbSet<JobDocument> JobDocuments => Set<JobDocument>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobChargeLine> JobChargeLines => Set<JobChargeLine>();
    public DbSet<TrackingEvent> TrackingEvents => Set<TrackingEvent>();
    public DbSet<PickupTask> PickupTasks => Set<PickupTask>();
    public DbSet<PickupEvidence> PickupEvidence => Set<PickupEvidence>();
    public DbSet<PickupEvidenceBlob> PickupEvidenceBlobs => Set<PickupEvidenceBlob>();
    public DbSet<PickupConfirmationLog> PickupConfirmationLogs => Set<PickupConfirmationLog>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<PasswordResetOtp> PasswordResetOtps => Set<PasswordResetOtp>();
    public DbSet<PackageScaleBatch> PackageScaleBatches => Set<PackageScaleBatch>();
    public DbSet<PackageScaleLine> PackageScaleLines => Set<PackageScaleLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RateImportBatch>(entity =>
        {
            entity.ToTable("RateImportBatches_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CarrierCode).HasMaxLength(20).IsRequired();
            entity.Property(x => x.RateSetCode).HasMaxLength(80);
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.FileHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => new { x.CarrierCode, x.FileHash });
            entity.HasIndex(x => x.RateSetCode);
            entity.HasIndex(x => x.CustomerId);
            entity.HasOne(x => x.Customer)
                .WithMany()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SalesUser)
                .WithMany()
                .HasForeignKey(x => x.SalesUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RateCard>(entity =>
        {
            entity.ToTable("RateCards_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CarrierCode).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Direction).HasMaxLength(10).IsRequired();
            entity.Property(x => x.ServiceCode).HasMaxLength(10).IsRequired();
            entity.Property(x => x.PackageType).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.FuelMultiplier).HasPrecision(18, 6);
            entity.Property(x => x.VatMultiplier).HasPrecision(18, 6);
            entity.Property(x => x.SourceSheet).HasMaxLength(128);
            entity.HasIndex(x => new { x.CarrierCode, x.Direction, x.ServiceCode, x.PackageType, x.EffectiveFrom });
            entity.HasOne(x => x.ImportBatch).WithMany(x => x.RateCards).HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RateBand>(entity =>
        {
            entity.ToTable("RateBands_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ZoneCode).HasMaxLength(10).IsRequired();
            entity.Property(x => x.WeightFrom).HasPrecision(18, 3);
            entity.Property(x => x.WeightTo).HasPrecision(18, 3);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.RateUnit).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => new { x.RateCardId, x.ZoneCode, x.WeightFrom, x.WeightTo }).IsUnique();
            entity.HasOne(x => x.RateCard).WithMany(x => x.Bands).HasForeignKey(x => x.RateCardId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CountryZoneMapping>(entity =>
        {
            entity.ToTable("CountryZoneMappings_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CarrierCode).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Direction).HasMaxLength(10).IsRequired();
            entity.Property(x => x.CountryName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.ServiceCode).HasMaxLength(10).IsRequired();
            entity.Property(x => x.ZoneCode).HasMaxLength(10).IsRequired();
            entity.Property(x => x.SourceSheet).HasMaxLength(128);
            entity.HasIndex(x => new { x.ImportBatchId, x.Direction, x.CountryName, x.ServiceCode }).IsUnique();
            entity.HasOne(x => x.ImportBatch).WithMany(x => x.CountryZones).HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RateSurcharge>(entity =>
        {
            entity.ToTable("RateSurcharges_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CarrierCode).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Direction).HasMaxLength(10).IsRequired();
            entity.Property(x => x.ServiceCode).HasMaxLength(10).IsRequired();
            entity.Property(x => x.ZoneCode).HasMaxLength(10);
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.CalculationType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Multiplier).HasPrecision(18, 6);
            entity.Property(x => x.SourceSheet).HasMaxLength(128);
            entity.Property(x => x.SourceCell).HasMaxLength(20);
            entity.HasOne(x => x.ImportBatch).WithMany(x => x.Surcharges).HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Users_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Department).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CustomerCode).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CompanyName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreditLimit).HasPrecision(18, 2);
            entity.HasIndex(x => x.CustomerCode).IsUnique();
            entity.HasOne(x => x.SalesUser)
                .WithMany()
                .HasForeignKey(x => x.SalesUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CustomerAddress>(entity =>
        {
            entity.ToTable("CustomerAddresses_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.ToTable("Bookings_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BookingNo).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CarrierCode).HasMaxLength(20);
            entity.Property(x => x.FromCountryCode).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ToCountryCode).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => x.BookingNo).IsUnique();
            entity.HasOne(x => x.Customer).WithMany(x => x.Bookings).HasForeignKey(x => x.CustomerId);
            entity.HasOne(x => x.SenderAddress).WithMany().HasForeignKey(x => x.SenderAddressId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.ReceiverAddress).WithMany().HasForeignKey(x => x.ReceiverAddressId).OnDelete(DeleteBehavior.NoAction);
            entity.HasMany(x => x.Packages).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Commodities).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Quotes).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Documents).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Job).WithOne(x => x.Booking).HasForeignKey<Job>(x => x.BookingId);
        });

        modelBuilder.Entity<BookingPackage>(entity =>
        {
            entity.ToTable("BookingPackages_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ActualWeight).HasPrecision(18, 3);
            entity.Property(x => x.DimWeight).HasPrecision(18, 3);
            entity.Property(x => x.ChargeableWeight).HasPrecision(18, 3);
            entity.Property(x => x.LengthCm).HasPrecision(18, 2);
            entity.Property(x => x.WidthCm).HasPrecision(18, 2);
            entity.Property(x => x.HeightCm).HasPrecision(18, 2);
        });

        modelBuilder.Entity<BookingCommodity>(entity =>
        {
            entity.ToTable("BookingCommodities_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.UnitValue).HasPrecision(18, 2);
        });

        modelBuilder.Entity<JobDocument>(entity =>
        {
            entity.ToTable("JobDocuments_ABCourier");
            entity.HasKey(x => x.Id);
        });

        modelBuilder.Entity<BookingQuote>(entity =>
        {
            entity.ToTable("BookingQuotes_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Freight).HasPrecision(18, 2);
            entity.Property(x => x.FuelSurcharge).HasPrecision(18, 2);
            entity.Property(x => x.DemandSurcharge).HasPrecision(18, 2);
            entity.Property(x => x.HandlingCharge).HasPrecision(18, 2);
            entity.Property(x => x.OtherCharge).HasPrecision(18, 2);
            entity.Property(x => x.VatAmount).HasPrecision(18, 2);
            entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
            entity.Property(x => x.ChargeableWeight).HasPrecision(18, 3);
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.ToTable("Jobs_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.JobNo).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => x.JobNo).IsUnique();
            entity.Property(x => x.Awb).HasMaxLength(60);
            entity.HasIndex(x => x.Awb);
            entity.Property(x => x.ActualWeight).HasPrecision(18, 3);
            entity.Property(x => x.ChargeableWeight).HasPrecision(18, 3);
            entity.HasOne(x => x.Customer).WithMany(x => x.Jobs).HasForeignKey(x => x.CustomerId);
            entity.HasMany(x => x.ChargeLines).WithOne().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.TrackingEvents).WithOne().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JobChargeLine>(entity =>
        {
            entity.ToTable("JobChargeLines_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.TaxRate).HasPrecision(9, 4);
        });

        modelBuilder.Entity<TrackingEvent>(entity =>
        {
            entity.ToTable("TrackingEvents_ABCourier");
            entity.HasKey(x => x.Id);
        });

        modelBuilder.Entity<PickupTask>(entity =>
        {
            entity.ToTable("PickupTasks_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PickupNo).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => x.PickupNo).IsUnique();
            entity.Property(x => x.Latitude).HasPrecision(10, 7);
            entity.Property(x => x.Longitude).HasPrecision(10, 7);
            entity.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId);
            entity.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId);
            entity.HasMany(x => x.Evidence).WithOne(x => x.PickupTask).HasForeignKey(x => x.PickupTaskId);
            entity.HasMany(x => x.ConfirmationLogs).WithOne(x => x.PickupTask).HasForeignKey(x => x.PickupTaskId);
        });

        modelBuilder.Entity<PickupEvidence>(entity =>
        {
            entity.ToTable("PickupEvidence_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Kind).HasMaxLength(20).IsRequired();
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.StoredFileName).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => x.PickupTaskId);
            entity.HasOne(x => x.Blob)
                .WithOne(x => x.Evidence)
                .HasForeignKey<PickupEvidenceBlob>(x => x.EvidenceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PickupEvidenceBlob>(entity =>
        {
            entity.ToTable("PickupEvidenceBlobs_ABCourier");
            entity.HasKey(x => x.EvidenceId);
            entity.Property(x => x.EvidenceId).ValueGeneratedNever();
            entity.Property(x => x.FileContent).HasColumnType("varbinary(max)").IsRequired();
        });

        modelBuilder.Entity<PickupConfirmationLog>(entity =>
        {
            entity.ToTable("PickupConfirmationLogs_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Action).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Detail).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.PickupTaskId, x.SequenceNo }).IsUnique();
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("Invoices_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.Property(x => x.VatAmount).HasPrecision(18, 2);
            entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
            entity.Property(x => x.PaidAmount).HasPrecision(18, 2);
            entity.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId);
        });

        modelBuilder.Entity<PasswordResetOtp>(entity =>
        {
            entity.ToTable("PasswordResetOtps_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Department).HasMaxLength(50).IsRequired();
            entity.Property(x => x.OtpHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => new { x.Email, x.Department, x.CreatedAtUtc });
        });

        modelBuilder.Entity<PackageScaleBatch>(entity =>
        {
            entity.ToTable("PackageScaleBatches_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.HasMany(x => x.Lines).WithOne(x => x.Batch).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PackageScaleLine>(entity =>
        {
            entity.ToTable("PackageScaleLines_ABCourier");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Awb).HasMaxLength(40).IsRequired();
            entity.Property(x => x.ScaleTicketNo).HasMaxLength(40);
            entity.Property(x => x.PackageType).HasMaxLength(20).IsRequired();
            entity.Property(x => x.WeighedBy).HasMaxLength(80);
            entity.Property(x => x.Note).HasMaxLength(300);
            entity.Property(x => x.ActualWeight).HasPrecision(18, 3);
            entity.Property(x => x.LengthCm).HasPrecision(18, 2);
            entity.Property(x => x.WidthCm).HasPrecision(18, 2);
            entity.Property(x => x.HeightCm).HasPrecision(18, 2);
            entity.Property(x => x.DimWeight).HasPrecision(18, 3);
            entity.Property(x => x.ChargeableWeight).HasPrecision(18, 3);
            entity.HasIndex(x => x.Awb);
            entity.HasIndex(x => x.JobId);
        });
    }
}
