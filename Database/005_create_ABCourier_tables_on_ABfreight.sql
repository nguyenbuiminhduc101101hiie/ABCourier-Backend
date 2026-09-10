/*
  Copy LMS Courier schema into ABfreight with table suffix _ABCourier.
  Target: Database=ABfreight (same server as ABFreight.sln).
  Run once against ABfreight. Idempotent (IF OBJECT_ID ... IS NULL).
*/
SET NOCOUNT ON;
GO

/* ========== Rate import ========== */

IF OBJECT_ID('dbo.RateImportBatches_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RateImportBatches_ABCourier
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_RateImportBatches_ABCourier PRIMARY KEY,
        CarrierCode varchar(20) NOT NULL,
        FileName nvarchar(260) NOT NULL,
        FileHash varchar(64) NOT NULL,
        Status varchar(30) NOT NULL,
        EffectiveFrom datetime2(0) NULL,
        RateCardCount int NOT NULL CONSTRAINT DF_RateImportBatches_ABCourier_RateCardCount DEFAULT 0,
        RateBandCount int NOT NULL CONSTRAINT DF_RateImportBatches_ABCourier_RateBandCount DEFAULT 0,
        CountryZoneCount int NOT NULL CONSTRAINT DF_RateImportBatches_ABCourier_CountryZoneCount DEFAULT 0,
        SurchargeCount int NOT NULL CONSTRAINT DF_RateImportBatches_ABCourier_SurchargeCount DEFAULT 0,
        WarningCount int NOT NULL CONSTRAINT DF_RateImportBatches_ABCourier_WarningCount DEFAULT 0,
        ErrorCount int NOT NULL CONSTRAINT DF_RateImportBatches_ABCourier_ErrorCount DEFAULT 0,
        CreatedAtUtc datetime2(0) NOT NULL,
        CommittedAtUtc datetime2(0) NULL
    );
    CREATE INDEX IX_RateImportBatches_ABCourier_Carrier_FileHash
        ON dbo.RateImportBatches_ABCourier(CarrierCode, FileHash);
END;
GO

IF OBJECT_ID('dbo.RateCards_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RateCards_ABCourier
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_RateCards_ABCourier PRIMARY KEY,
        ImportBatchId uniqueidentifier NOT NULL,
        CarrierCode varchar(20) NOT NULL,
        Direction varchar(10) NOT NULL,
        ServiceCode varchar(10) NOT NULL,
        PackageType varchar(20) NOT NULL,
        Currency char(3) NOT NULL CONSTRAINT DF_RateCards_ABCourier_Currency DEFAULT 'VND',
        EffectiveFrom datetime2(0) NOT NULL,
        EffectiveTo datetime2(0) NULL,
        FuelMultiplier decimal(18,6) NOT NULL CONSTRAINT DF_RateCards_ABCourier_Fuel DEFAULT 1,
        VatMultiplier decimal(18,6) NOT NULL CONSTRAINT DF_RateCards_ABCourier_Vat DEFAULT 1,
        IsActive bit NOT NULL CONSTRAINT DF_RateCards_ABCourier_IsActive DEFAULT 0,
        SourceSheet nvarchar(128) NOT NULL,
        CONSTRAINT FK_RateCards_ABCourier_Batches FOREIGN KEY (ImportBatchId)
            REFERENCES dbo.RateImportBatches_ABCourier(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_RateCards_ABCourier_Lookup
        ON dbo.RateCards_ABCourier(CarrierCode, Direction, ServiceCode, PackageType, EffectiveFrom);
END;
GO

IF OBJECT_ID('dbo.RateBands_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RateBands_ABCourier
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_RateBands_ABCourier PRIMARY KEY,
        RateCardId uniqueidentifier NOT NULL,
        ZoneCode varchar(10) NOT NULL,
        WeightFrom decimal(18,3) NOT NULL,
        WeightTo decimal(18,3) NOT NULL,
        RateUnit varchar(20) NOT NULL,
        Amount decimal(18,2) NOT NULL,
        SourceRow int NOT NULL,
        CONSTRAINT FK_RateBands_ABCourier_Cards FOREIGN KEY (RateCardId)
            REFERENCES dbo.RateCards_ABCourier(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_RateBands_ABCourier UNIQUE(RateCardId, ZoneCode, WeightFrom, WeightTo)
    );
END;
GO

IF OBJECT_ID('dbo.CountryZoneMappings_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CountryZoneMappings_ABCourier
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CountryZoneMappings_ABCourier PRIMARY KEY,
        ImportBatchId uniqueidentifier NOT NULL,
        CarrierCode varchar(20) NOT NULL,
        Direction varchar(10) NOT NULL,
        CountryName nvarchar(160) NOT NULL,
        ServiceCode varchar(10) NOT NULL,
        ZoneCode varchar(10) NOT NULL,
        IsSupported bit NOT NULL,
        SourceSheet nvarchar(128) NOT NULL,
        SourceRow int NOT NULL,
        CONSTRAINT FK_CountryZoneMappings_ABCourier_Batches FOREIGN KEY (ImportBatchId)
            REFERENCES dbo.RateImportBatches_ABCourier(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_CountryZoneMappings_ABCourier UNIQUE(ImportBatchId, Direction, CountryName, ServiceCode)
    );
END;
GO

IF OBJECT_ID('dbo.RateSurcharges_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RateSurcharges_ABCourier
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_RateSurcharges_ABCourier PRIMARY KEY,
        ImportBatchId uniqueidentifier NOT NULL,
        CarrierCode varchar(20) NOT NULL,
        Direction varchar(10) NOT NULL,
        ServiceCode varchar(10) NOT NULL,
        ZoneCode varchar(10) NULL,
        Code varchar(40) NOT NULL,
        Name nvarchar(160) NOT NULL,
        CalculationType varchar(30) NOT NULL,
        Amount decimal(18,2) NOT NULL,
        Multiplier decimal(18,6) NULL,
        EffectiveFrom datetime2(0) NOT NULL,
        IsInformational bit NOT NULL CONSTRAINT DF_RateSurcharges_ABCourier_Info DEFAULT 0,
        SourceSheet nvarchar(128) NOT NULL,
        SourceCell varchar(20) NOT NULL,
        CONSTRAINT FK_RateSurcharges_ABCourier_Batches FOREIGN KEY (ImportBatchId)
            REFERENCES dbo.RateImportBatches_ABCourier(Id) ON DELETE CASCADE
    );
END;
GO

/* ========== Platform ========== */

IF OBJECT_ID('dbo.Customers_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Customers_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Customers_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerCode varchar(30) NOT NULL,
        CompanyName nvarchar(200) NOT NULL,
        TaxCode varchar(30) NULL,
        BillingEmail varchar(200) NULL,
        CreditLimit decimal(18,2) NOT NULL CONSTRAINT DF_Customers_ABCourier_CreditLimit DEFAULT 0,
        BillingCycle varchar(30) NOT NULL CONSTRAINT DF_Customers_ABCourier_BillingCycle DEFAULT 'Monthly',
        RatePlanCode varchar(50) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Customers_ABCourier_IsActive DEFAULT 1,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Customers_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_Customers_ABCourier_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_Customers_ABCourier_CustomerCode UNIQUE(CustomerCode)
    );
END;
GO

IF OBJECT_ID('dbo.CustomerUsers_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerUsers_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerUsers_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL,
        Email varchar(200) NOT NULL,
        FullName nvarchar(160) NOT NULL,
        Phone varchar(40) NULL,
        RoleCode varchar(40) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_CustomerUsers_ABCourier_IsActive DEFAULT 1,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_CustomerUsers_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_CustomerUsers_ABCourier_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers_ABCourier(Id),
        CONSTRAINT UQ_CustomerUsers_ABCourier_Email UNIQUE(Email)
    );
END;
GO

IF OBJECT_ID('dbo.CustomerAddresses_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerAddresses_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerAddresses_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL,
        AddressName nvarchar(120) NOT NULL,
        ContactName nvarchar(160) NOT NULL,
        CompanyName nvarchar(200) NULL,
        Phone varchar(40) NULL,
        Email varchar(200) NULL,
        AddressLine1 nvarchar(300) NOT NULL,
        AddressLine2 nvarchar(300) NULL,
        City nvarchar(120) NOT NULL,
        StateProvince nvarchar(120) NULL,
        PostalCode varchar(30) NULL,
        CountryCode char(2) NOT NULL,
        IsResidential bit NOT NULL CONSTRAINT DF_CustomerAddresses_ABCourier_IsResidential DEFAULT 0,
        IsDefaultSender bit NOT NULL CONSTRAINT DF_CustomerAddresses_ABCourier_DefaultSender DEFAULT 0,
        IsDefaultReceiver bit NOT NULL CONSTRAINT DF_CustomerAddresses_ABCourier_DefaultReceiver DEFAULT 0,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_CustomerAddresses_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_CustomerAddresses_ABCourier_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers_ABCourier(Id)
    );
    CREATE INDEX IX_CustomerAddresses_ABCourier_CustomerId ON dbo.CustomerAddresses_ABCourier(CustomerId);
END;
GO

IF OBJECT_ID('dbo.Users_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Users_ABCourier PRIMARY KEY,
        Email nvarchar(256) NOT NULL,
        DisplayName nvarchar(120) NOT NULL,
        PasswordHash nvarchar(512) NOT NULL,
        Department varchar(100) NOT NULL CONSTRAINT DF_Users_ABCourier_Department DEFAULT 'Admin',
        CustomerId uniqueidentifier NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Users_ABCourier_IsActive DEFAULT 1,
        CreatedAtUtc datetime2(0) NOT NULL,
        LastLoginAtUtc datetime2(0) NULL,
        CONSTRAINT FK_Users_ABCourier_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers_ABCourier(Id),
        CONSTRAINT UQ_Users_ABCourier_Email UNIQUE(Email)
    );
    CREATE INDEX IX_Users_ABCourier_Department ON dbo.Users_ABCourier(Department, IsActive);
END;
GO

IF OBJECT_ID('dbo.Bookings_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Bookings_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Bookings_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        BookingNo varchar(30) NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        Direction varchar(20) NOT NULL,
        ServiceCode varchar(10) NOT NULL,
        FromCountryCode char(2) NOT NULL,
        ToCountryCode char(2) NOT NULL,
        ShipDate date NOT NULL,
        SenderAddressId uniqueidentifier NULL,
        ReceiverAddressId uniqueidentifier NULL,
        Status varchar(30) NOT NULL,
        CustomerNote nvarchar(1000) NULL,
        RequiresComplianceReview bit NOT NULL CONSTRAINT DF_Bookings_ABCourier_Compliance DEFAULT 0,
        CreatedByUserId uniqueidentifier NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Bookings_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_Bookings_ABCourier_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_Bookings_ABCourier_BookingNo UNIQUE(BookingNo),
        CONSTRAINT FK_Bookings_ABCourier_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers_ABCourier(Id),
        CONSTRAINT FK_Bookings_ABCourier_SenderAddress FOREIGN KEY(SenderAddressId) REFERENCES dbo.CustomerAddresses_ABCourier(Id),
        CONSTRAINT FK_Bookings_ABCourier_ReceiverAddress FOREIGN KEY(ReceiverAddressId) REFERENCES dbo.CustomerAddresses_ABCourier(Id)
    );
    CREATE INDEX IX_Bookings_ABCourier_CustomerDate ON dbo.Bookings_ABCourier(CustomerId, ShipDate DESC);
    CREATE INDEX IX_Bookings_ABCourier_Status ON dbo.Bookings_ABCourier(Status, CreatedAt DESC);
END;
GO

IF OBJECT_ID('dbo.BookingPackages_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingPackages_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_BookingPackages_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        BookingId uniqueidentifier NOT NULL,
        PackageNo int NOT NULL,
        PackageType varchar(30) NOT NULL,
        Pieces int NOT NULL,
        ActualWeight decimal(18,3) NOT NULL,
        LengthCm decimal(18,2) NOT NULL,
        WidthCm decimal(18,2) NOT NULL,
        HeightCm decimal(18,2) NOT NULL,
        DimWeight decimal(18,3) NOT NULL,
        ChargeableWeight decimal(18,3) NOT NULL,
        IsStackable bit NOT NULL CONSTRAINT DF_BookingPackages_ABCourier_Stackable DEFAULT 1,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_BookingPackages_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_BookingPackages_ABCourier_Bookings FOREIGN KEY(BookingId) REFERENCES dbo.Bookings_ABCourier(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_BookingPackages_ABCourier_BookingNo UNIQUE(BookingId, PackageNo)
    );
END;
GO

IF OBJECT_ID('dbo.BookingCommodities_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingCommodities_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_BookingCommodities_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        BookingId uniqueidentifier NOT NULL,
        ItemName nvarchar(200) NOT NULL,
        DescriptionEnglish nvarchar(500) NOT NULL,
        Quantity decimal(18,3) NOT NULL,
        UnitValue decimal(18,2) NOT NULL,
        Currency char(3) NOT NULL,
        HsCode varchar(30) NULL,
        CountryOfOrigin char(2) NULL,
        ContainsBattery bit NOT NULL CONSTRAINT DF_Commodities_ABCourier_Battery DEFAULT 0,
        ContainsLiquid bit NOT NULL CONSTRAINT DF_Commodities_ABCourier_Liquid DEFAULT 0,
        IsFood bit NOT NULL CONSTRAINT DF_Commodities_ABCourier_Food DEFAULT 0,
        IsDangerousGoods bit NOT NULL CONSTRAINT DF_Commodities_ABCourier_DG DEFAULT 0,
        CONSTRAINT FK_BookingCommodities_ABCourier_Bookings FOREIGN KEY(BookingId) REFERENCES dbo.Bookings_ABCourier(Id) ON DELETE CASCADE
    );
END;
GO

IF OBJECT_ID('dbo.BookingQuotes_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingQuotes_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_BookingQuotes_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        BookingId uniqueidentifier NOT NULL,
        VersionNo int NOT NULL,
        RateCardId uniqueidentifier NULL,
        ZoneCode varchar(10) NOT NULL,
        ChargeableWeight decimal(18,3) NOT NULL,
        Freight decimal(18,2) NOT NULL,
        FuelSurcharge decimal(18,2) NOT NULL,
        DemandSurcharge decimal(18,2) NOT NULL,
        HandlingCharge decimal(18,2) NOT NULL,
        OtherCharge decimal(18,2) NOT NULL CONSTRAINT DF_BookingQuotes_ABCourier_Other DEFAULT 0,
        VatAmount decimal(18,2) NOT NULL,
        TotalAmount decimal(18,2) NOT NULL,
        Currency char(3) NOT NULL CONSTRAINT DF_BookingQuotes_ABCourier_Currency DEFAULT 'VND',
        CalculationSnapshot nvarchar(max) NULL,
        Status varchar(30) NOT NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_BookingQuotes_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_BookingQuotes_ABCourier_Bookings FOREIGN KEY(BookingId) REFERENCES dbo.Bookings_ABCourier(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_BookingQuotes_ABCourier_Version UNIQUE(BookingId, VersionNo)
    );
END;
GO

IF OBJECT_ID('dbo.Jobs_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Jobs_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Jobs_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        JobNo varchar(30) NOT NULL,
        BookingId uniqueidentifier NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        CarrierCode varchar(20) NOT NULL CONSTRAINT DF_Jobs_ABCourier_Carrier DEFAULT 'FEDEX',
        Direction varchar(20) NOT NULL,
        ServiceCode varchar(10) NOT NULL,
        Awb varchar(60) NULL,
        Status varchar(30) NOT NULL,
        BillingStatus varchar(30) NOT NULL CONSTRAINT DF_Jobs_ABCourier_BillingStatus DEFAULT 'NotReady',
        ActualWeight decimal(18,3) NOT NULL CONSTRAINT DF_Jobs_ABCourier_ActualWeight DEFAULT 0,
        ChargeableWeight decimal(18,3) NOT NULL CONSTRAINT DF_Jobs_ABCourier_ChargeableWeight DEFAULT 0,
        PickupDate datetime2(0) NULL,
        DeliveredDate datetime2(0) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Jobs_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_Jobs_ABCourier_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_Jobs_ABCourier_JobNo UNIQUE(JobNo),
        CONSTRAINT UQ_Jobs_ABCourier_BookingId UNIQUE(BookingId),
        CONSTRAINT FK_Jobs_ABCourier_Bookings FOREIGN KEY(BookingId) REFERENCES dbo.Bookings_ABCourier(Id),
        CONSTRAINT FK_Jobs_ABCourier_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers_ABCourier(Id)
    );
    CREATE INDEX IX_Jobs_ABCourier_Awb ON dbo.Jobs_ABCourier(Awb);
    CREATE INDEX IX_Jobs_ABCourier_Status ON dbo.Jobs_ABCourier(Status, CreatedAt DESC);
END;
GO

IF OBJECT_ID('dbo.TrackingEvents_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TrackingEvents_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_TrackingEvents_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        JobId uniqueidentifier NOT NULL,
        EventCode varchar(40) NOT NULL,
        EventTitle nvarchar(200) NOT NULL,
        EventDescription nvarchar(1000) NULL,
        Location nvarchar(200) NULL,
        EventTime datetime2(0) NOT NULL,
        SourceType varchar(30) NOT NULL,
        RawPayload nvarchar(max) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_TrackingEvents_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_TrackingEvents_ABCourier_Jobs FOREIGN KEY(JobId) REFERENCES dbo.Jobs_ABCourier(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_TrackingEvents_ABCourier_JobTime ON dbo.TrackingEvents_ABCourier(JobId, EventTime DESC);
END;
GO

IF OBJECT_ID('dbo.PickupTasks_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PickupTasks_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_PickupTasks_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        PickupNo varchar(30) NOT NULL,
        JobId uniqueidentifier NULL,
        BookingId uniqueidentifier NOT NULL,
        AssignedUserId uniqueidentifier NULL,
        ScheduledAt datetime2(0) NOT NULL,
        Status varchar(30) NOT NULL,
        ExpectedPieces int NOT NULL,
        ActualPieces int NULL,
        SignatureFileUrl nvarchar(1000) NULL,
        Latitude decimal(10,7) NULL,
        Longitude decimal(10,7) NULL,
        CompletedAt datetime2(0) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_PickupTasks_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_PickupTasks_ABCourier_PickupNo UNIQUE(PickupNo),
        CONSTRAINT FK_PickupTasks_ABCourier_Bookings FOREIGN KEY(BookingId) REFERENCES dbo.Bookings_ABCourier(Id),
        CONSTRAINT FK_PickupTasks_ABCourier_Jobs FOREIGN KEY(JobId) REFERENCES dbo.Jobs_ABCourier(Id)
    );
END;
GO

IF OBJECT_ID('dbo.JobDocuments_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobDocuments_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_JobDocuments_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        JobId uniqueidentifier NULL,
        BookingId uniqueidentifier NULL,
        DocumentType varchar(40) NOT NULL,
        FileName nvarchar(260) NOT NULL,
        FileUrl nvarchar(1000) NOT NULL,
        FileHash varchar(64) NULL,
        UploadedBy varchar(200) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_JobDocuments_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_JobDocuments_ABCourier_Jobs FOREIGN KEY(JobId) REFERENCES dbo.Jobs_ABCourier(Id),
        CONSTRAINT FK_JobDocuments_ABCourier_Bookings FOREIGN KEY(BookingId) REFERENCES dbo.Bookings_ABCourier(Id)
    );
END;
GO

IF OBJECT_ID('dbo.BillingBatches_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BillingBatches_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_BillingBatches_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        BatchNo varchar(30) NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        PeriodFrom date NOT NULL,
        PeriodTo date NOT NULL,
        Currency char(3) NOT NULL CONSTRAINT DF_BillingBatches_ABCourier_Currency DEFAULT 'VND',
        Subtotal decimal(18,2) NOT NULL,
        VatAmount decimal(18,2) NOT NULL,
        TotalAmount decimal(18,2) NOT NULL,
        Status varchar(30) NOT NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_BillingBatches_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_BillingBatches_ABCourier_BatchNo UNIQUE(BatchNo),
        CONSTRAINT FK_BillingBatches_ABCourier_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers_ABCourier(Id)
    );
END;
GO

IF OBJECT_ID('dbo.JobChargeLines_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobChargeLines_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_JobChargeLines_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        JobId uniqueidentifier NOT NULL,
        ChargeKind varchar(10) NOT NULL,
        ChargeCode varchar(40) NOT NULL,
        Description nvarchar(300) NOT NULL,
        Quantity decimal(18,3) NOT NULL CONSTRAINT DF_JobChargeLines_ABCourier_Quantity DEFAULT 1,
        UnitPrice decimal(18,2) NOT NULL,
        Amount decimal(18,2) NOT NULL,
        Currency char(3) NOT NULL CONSTRAINT DF_JobChargeLines_ABCourier_Currency DEFAULT 'VND',
        TaxRate decimal(9,4) NOT NULL CONSTRAINT DF_JobChargeLines_ABCourier_TaxRate DEFAULT 0.08,
        ApprovalStatus varchar(30) NOT NULL CONSTRAINT DF_JobChargeLines_ABCourier_Approval DEFAULT 'Approved',
        SourceType varchar(40) NULL,
        SourceReference varchar(100) NULL,
        BillingBatchId uniqueidentifier NULL,
        IsLocked bit NOT NULL CONSTRAINT DF_JobChargeLines_ABCourier_IsLocked DEFAULT 0,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_JobChargeLines_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_JobChargeLines_ABCourier_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_JobChargeLines_ABCourier_Kind CHECK(ChargeKind IN ('Debit','Credit')),
        CONSTRAINT FK_JobChargeLines_ABCourier_Jobs FOREIGN KEY(JobId) REFERENCES dbo.Jobs_ABCourier(Id) ON DELETE CASCADE,
        CONSTRAINT FK_JobChargeLines_ABCourier_Batches FOREIGN KEY(BillingBatchId) REFERENCES dbo.BillingBatches_ABCourier(Id)
    );
    CREATE INDEX IX_JobChargeLines_ABCourier_JobKind ON dbo.JobChargeLines_ABCourier(JobId, ChargeKind);
    CREATE INDEX IX_JobChargeLines_ABCourier_BillingBatch ON dbo.JobChargeLines_ABCourier(BillingBatchId);
END;
GO

IF OBJECT_ID('dbo.Invoices_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Invoices_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Invoices_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        InvoiceNo varchar(50) NULL,
        BillingBatchId uniqueidentifier NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        InvoiceDate date NOT NULL,
        DueDate date NULL,
        Subtotal decimal(18,2) NOT NULL,
        VatAmount decimal(18,2) NOT NULL,
        TotalAmount decimal(18,2) NOT NULL,
        PaidAmount decimal(18,2) NOT NULL CONSTRAINT DF_Invoices_ABCourier_Paid DEFAULT 0,
        Status varchar(30) NOT NULL,
        ProviderCode varchar(40) NULL,
        ProviderTransactionId varchar(100) NULL,
        PdfFileUrl nvarchar(1000) NULL,
        XmlFileUrl nvarchar(1000) NULL,
        SignedAt datetime2(0) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Invoices_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Invoices_ABCourier_Batches FOREIGN KEY(BillingBatchId) REFERENCES dbo.BillingBatches_ABCourier(Id),
        CONSTRAINT FK_Invoices_ABCourier_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers_ABCourier(Id)
    );
    CREATE INDEX IX_Invoices_ABCourier_CustomerStatus ON dbo.Invoices_ABCourier(CustomerId, Status, DueDate);
END;
GO

IF OBJECT_ID('dbo.Payments_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Payments_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Payments_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        PaymentNo varchar(50) NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        PaymentDate date NOT NULL,
        Amount decimal(18,2) NOT NULL,
        Currency char(3) NOT NULL CONSTRAINT DF_Payments_ABCourier_Currency DEFAULT 'VND',
        Method varchar(30) NULL,
        BankReference varchar(100) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Payments_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_Payments_ABCourier_PaymentNo UNIQUE(PaymentNo),
        CONSTRAINT FK_Payments_ABCourier_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers_ABCourier(Id)
    );
END;
GO

IF OBJECT_ID('dbo.PaymentAllocations_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentAllocations_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_PaymentAllocations_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        PaymentId uniqueidentifier NOT NULL,
        InvoiceId uniqueidentifier NOT NULL,
        AllocatedAmount decimal(18,2) NOT NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_PaymentAllocations_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_PaymentAllocations_ABCourier_Payments FOREIGN KEY(PaymentId) REFERENCES dbo.Payments_ABCourier(Id) ON DELETE CASCADE,
        CONSTRAINT FK_PaymentAllocations_ABCourier_Invoices FOREIGN KEY(InvoiceId) REFERENCES dbo.Invoices_ABCourier(Id)
    );
END;
GO

IF OBJECT_ID('dbo.ApprovalRequests_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApprovalRequests_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ApprovalRequests_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        EntityType varchar(40) NOT NULL,
        EntityId uniqueidentifier NOT NULL,
        ApprovalType varchar(40) NOT NULL,
        Reason nvarchar(1000) NULL,
        RequestedBy varchar(200) NOT NULL,
        RequestedAt datetime2(0) NOT NULL CONSTRAINT DF_ApprovalRequests_ABCourier_RequestedAt DEFAULT SYSUTCDATETIME(),
        Status varchar(30) NOT NULL,
        ApprovedBy varchar(200) NULL,
        ApprovedAt datetime2(0) NULL,
        DecisionNote nvarchar(1000) NULL
    );
END;
GO

IF OBJECT_ID('dbo.AuditLogs_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs_ABCourier (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs_ABCourier PRIMARY KEY,
        UserId varchar(200) NULL,
        ActionCode varchar(50) NOT NULL,
        EntityType varchar(50) NOT NULL,
        EntityId varchar(100) NOT NULL,
        BeforeJson nvarchar(max) NULL,
        AfterJson nvarchar(max) NULL,
        IpAddress varchar(64) NULL,
        CorrelationId varchar(100) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_AuditLogs_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_AuditLogs_ABCourier_Entity ON dbo.AuditLogs_ABCourier(EntityType, EntityId, CreatedAt DESC);
END;
GO

IF OBJECT_ID('dbo.IntegrationLogs_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.IntegrationLogs_ABCourier (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_IntegrationLogs_ABCourier PRIMARY KEY,
        IntegrationCode varchar(50) NOT NULL,
        OperationCode varchar(50) NOT NULL,
        CorrelationId varchar(100) NOT NULL,
        RequestBody nvarchar(max) NULL,
        ResponseBody nvarchar(max) NULL,
        HttpStatus int NULL,
        Status varchar(30) NOT NULL,
        ErrorMessage nvarchar(2000) NULL,
        RetryCount int NOT NULL CONSTRAINT DF_IntegrationLogs_ABCourier_Retry DEFAULT 0,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_IntegrationLogs_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CompletedAt datetime2(0) NULL
    );
    CREATE INDEX IX_IntegrationLogs_ABCourier_Correlation ON dbo.IntegrationLogs_ABCourier(CorrelationId);
END;
GO

PRINT '005_create_ABCourier_tables_on_ABfreight: done';
GO
