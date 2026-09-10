/* LMS Courier Platform - SQL Server operational schema
   Rate import tables are in 001_rate_import_tables.sql.
*/
SET NOCOUNT ON;

IF OBJECT_ID('dbo.Customers', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Customers (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Customers PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerCode varchar(30) NOT NULL,
        CompanyName nvarchar(200) NOT NULL,
        TaxCode varchar(30) NULL,
        BillingEmail varchar(200) NULL,
        CreditLimit decimal(18,2) NOT NULL CONSTRAINT DF_Customers_CreditLimit DEFAULT 0,
        BillingCycle varchar(30) NOT NULL CONSTRAINT DF_Customers_BillingCycle DEFAULT 'Monthly',
        RatePlanCode varchar(50) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Customers_IsActive DEFAULT 1,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_Customers_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_Customers_CustomerCode UNIQUE(CustomerCode)
    );
END;

IF OBJECT_ID('dbo.CustomerUsers', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerUsers (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerUsers PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL,
        Email varchar(200) NOT NULL,
        FullName nvarchar(160) NOT NULL,
        Phone varchar(40) NULL,
        RoleCode varchar(40) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_CustomerUsers_IsActive DEFAULT 1,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_CustomerUsers_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_CustomerUsers_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT UQ_CustomerUsers_Email UNIQUE(Email)
    );
END;

IF OBJECT_ID('dbo.CustomerAddresses', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerAddresses (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerAddresses PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
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
        IsResidential bit NOT NULL CONSTRAINT DF_CustomerAddresses_IsResidential DEFAULT 0,
        IsDefaultSender bit NOT NULL CONSTRAINT DF_CustomerAddresses_DefaultSender DEFAULT 0,
        IsDefaultReceiver bit NOT NULL CONSTRAINT DF_CustomerAddresses_DefaultReceiver DEFAULT 0,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_CustomerAddresses_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_CustomerAddresses_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id)
    );
    CREATE INDEX IX_CustomerAddresses_CustomerId ON dbo.CustomerAddresses(CustomerId);
END;

IF OBJECT_ID('dbo.Bookings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Bookings (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Bookings PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
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
        RequiresComplianceReview bit NOT NULL CONSTRAINT DF_Bookings_Compliance DEFAULT 0,
        CreatedByUserId uniqueidentifier NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Bookings_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_Bookings_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_Bookings_BookingNo UNIQUE(BookingNo),
        CONSTRAINT FK_Bookings_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT FK_Bookings_SenderAddress FOREIGN KEY(SenderAddressId) REFERENCES dbo.CustomerAddresses(Id),
        CONSTRAINT FK_Bookings_ReceiverAddress FOREIGN KEY(ReceiverAddressId) REFERENCES dbo.CustomerAddresses(Id)
    );
    CREATE INDEX IX_Bookings_CustomerDate ON dbo.Bookings(CustomerId, ShipDate DESC);
    CREATE INDEX IX_Bookings_Status ON dbo.Bookings(Status, CreatedAt DESC);
END;

IF OBJECT_ID('dbo.BookingPackages', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingPackages (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_BookingPackages PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
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
        IsStackable bit NOT NULL CONSTRAINT DF_BookingPackages_Stackable DEFAULT 1,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_BookingPackages_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_BookingPackages_Bookings FOREIGN KEY(BookingId) REFERENCES dbo.Bookings(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_BookingPackages_BookingNo UNIQUE(BookingId, PackageNo)
    );
END;

IF OBJECT_ID('dbo.BookingCommodities', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingCommodities (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_BookingCommodities PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        BookingId uniqueidentifier NOT NULL,
        ItemName nvarchar(200) NOT NULL,
        DescriptionEnglish nvarchar(500) NOT NULL,
        Quantity decimal(18,3) NOT NULL,
        UnitValue decimal(18,2) NOT NULL,
        Currency char(3) NOT NULL,
        HsCode varchar(30) NULL,
        CountryOfOrigin char(2) NULL,
        ContainsBattery bit NOT NULL CONSTRAINT DF_Commodities_Battery DEFAULT 0,
        ContainsLiquid bit NOT NULL CONSTRAINT DF_Commodities_Liquid DEFAULT 0,
        IsFood bit NOT NULL CONSTRAINT DF_Commodities_Food DEFAULT 0,
        IsDangerousGoods bit NOT NULL CONSTRAINT DF_Commodities_DG DEFAULT 0,
        CONSTRAINT FK_BookingCommodities_Bookings FOREIGN KEY(BookingId) REFERENCES dbo.Bookings(Id) ON DELETE CASCADE
    );
END;

IF OBJECT_ID('dbo.BookingQuotes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingQuotes (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_BookingQuotes PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        BookingId uniqueidentifier NOT NULL,
        VersionNo int NOT NULL,
        RateCardId uniqueidentifier NULL,
        ZoneCode varchar(10) NOT NULL,
        ChargeableWeight decimal(18,3) NOT NULL,
        Freight decimal(18,2) NOT NULL,
        FuelSurcharge decimal(18,2) NOT NULL,
        DemandSurcharge decimal(18,2) NOT NULL,
        HandlingCharge decimal(18,2) NOT NULL,
        OtherCharge decimal(18,2) NOT NULL CONSTRAINT DF_BookingQuotes_Other DEFAULT 0,
        VatAmount decimal(18,2) NOT NULL,
        TotalAmount decimal(18,2) NOT NULL,
        Currency char(3) NOT NULL CONSTRAINT DF_BookingQuotes_Currency DEFAULT 'VND',
        CalculationSnapshot nvarchar(max) NULL,
        Status varchar(30) NOT NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_BookingQuotes_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_BookingQuotes_Bookings FOREIGN KEY(BookingId) REFERENCES dbo.Bookings(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_BookingQuotes_Version UNIQUE(BookingId, VersionNo)
    );
END;

IF OBJECT_ID('dbo.Jobs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Jobs (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Jobs PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        JobNo varchar(30) NOT NULL,
        BookingId uniqueidentifier NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        CarrierCode varchar(20) NOT NULL CONSTRAINT DF_Jobs_Carrier DEFAULT 'FEDEX',
        Direction varchar(20) NOT NULL,
        ServiceCode varchar(10) NOT NULL,
        Awb varchar(60) NULL,
        Status varchar(30) NOT NULL,
        BillingStatus varchar(30) NOT NULL CONSTRAINT DF_Jobs_BillingStatus DEFAULT 'NotReady',
        ActualWeight decimal(18,3) NOT NULL CONSTRAINT DF_Jobs_ActualWeight DEFAULT 0,
        ChargeableWeight decimal(18,3) NOT NULL CONSTRAINT DF_Jobs_ChargeableWeight DEFAULT 0,
        PickupDate datetime2(0) NULL,
        DeliveredDate datetime2(0) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Jobs_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_Jobs_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_Jobs_JobNo UNIQUE(JobNo),
        CONSTRAINT UQ_Jobs_BookingId UNIQUE(BookingId),
        CONSTRAINT FK_Jobs_Bookings FOREIGN KEY(BookingId) REFERENCES dbo.Bookings(Id),
        CONSTRAINT FK_Jobs_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id)
    );
    CREATE INDEX IX_Jobs_Awb ON dbo.Jobs(Awb);
    CREATE INDEX IX_Jobs_Status ON dbo.Jobs(Status, CreatedAt DESC);
END;

IF OBJECT_ID('dbo.TrackingEvents', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TrackingEvents (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_TrackingEvents PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        JobId uniqueidentifier NOT NULL,
        EventCode varchar(40) NOT NULL,
        EventTitle nvarchar(200) NOT NULL,
        EventDescription nvarchar(1000) NULL,
        Location nvarchar(200) NULL,
        EventTime datetime2(0) NOT NULL,
        SourceType varchar(30) NOT NULL,
        RawPayload nvarchar(max) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_TrackingEvents_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_TrackingEvents_Jobs FOREIGN KEY(JobId) REFERENCES dbo.Jobs(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_TrackingEvents_JobTime ON dbo.TrackingEvents(JobId, EventTime DESC);
END;

IF OBJECT_ID('dbo.PickupTasks', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PickupTasks (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_PickupTasks PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
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
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_PickupTasks_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_PickupTasks_PickupNo UNIQUE(PickupNo),
        CONSTRAINT FK_PickupTasks_Bookings FOREIGN KEY(BookingId) REFERENCES dbo.Bookings(Id),
        CONSTRAINT FK_PickupTasks_Jobs FOREIGN KEY(JobId) REFERENCES dbo.Jobs(Id)
    );
END;

IF OBJECT_ID('dbo.JobDocuments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobDocuments (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_JobDocuments PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        JobId uniqueidentifier NULL,
        BookingId uniqueidentifier NULL,
        DocumentType varchar(40) NOT NULL,
        FileName nvarchar(260) NOT NULL,
        FileUrl nvarchar(1000) NOT NULL,
        FileHash varchar(64) NULL,
        UploadedBy varchar(200) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_JobDocuments_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_JobDocuments_Jobs FOREIGN KEY(JobId) REFERENCES dbo.Jobs(Id),
        CONSTRAINT FK_JobDocuments_Bookings FOREIGN KEY(BookingId) REFERENCES dbo.Bookings(Id)
    );
END;

IF OBJECT_ID('dbo.JobChargeLines', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobChargeLines (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_JobChargeLines PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        JobId uniqueidentifier NOT NULL,
        ChargeKind varchar(10) NOT NULL, /* Debit / Credit */
        ChargeCode varchar(40) NOT NULL,
        Description nvarchar(300) NOT NULL,
        Quantity decimal(18,3) NOT NULL CONSTRAINT DF_JobChargeLines_Quantity DEFAULT 1,
        UnitPrice decimal(18,2) NOT NULL,
        Amount decimal(18,2) NOT NULL,
        Currency char(3) NOT NULL CONSTRAINT DF_JobChargeLines_Currency DEFAULT 'VND',
        TaxRate decimal(9,4) NOT NULL CONSTRAINT DF_JobChargeLines_TaxRate DEFAULT 0.08,
        ApprovalStatus varchar(30) NOT NULL CONSTRAINT DF_JobChargeLines_Approval DEFAULT 'Approved',
        SourceType varchar(40) NULL,
        SourceReference varchar(100) NULL,
        BillingBatchId uniqueidentifier NULL,
        IsLocked bit NOT NULL CONSTRAINT DF_JobChargeLines_IsLocked DEFAULT 0,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_JobChargeLines_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_JobChargeLines_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_JobChargeLines_Kind CHECK(ChargeKind IN ('Debit','Credit')),
        CONSTRAINT FK_JobChargeLines_Jobs FOREIGN KEY(JobId) REFERENCES dbo.Jobs(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_JobChargeLines_JobKind ON dbo.JobChargeLines(JobId, ChargeKind);
    CREATE INDEX IX_JobChargeLines_BillingBatch ON dbo.JobChargeLines(BillingBatchId);
END;

IF OBJECT_ID('dbo.BillingBatches', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BillingBatches (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_BillingBatches PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        BatchNo varchar(30) NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        PeriodFrom date NOT NULL,
        PeriodTo date NOT NULL,
        Currency char(3) NOT NULL CONSTRAINT DF_BillingBatches_Currency DEFAULT 'VND',
        Subtotal decimal(18,2) NOT NULL,
        VatAmount decimal(18,2) NOT NULL,
        TotalAmount decimal(18,2) NOT NULL,
        Status varchar(30) NOT NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_BillingBatches_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_BillingBatches_BatchNo UNIQUE(BatchNo),
        CONSTRAINT FK_BillingBatches_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_JobChargeLines_BillingBatches')
BEGIN
    ALTER TABLE dbo.JobChargeLines
        ADD CONSTRAINT FK_JobChargeLines_BillingBatches
        FOREIGN KEY(BillingBatchId) REFERENCES dbo.BillingBatches(Id);
END;

IF OBJECT_ID('dbo.Invoices', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Invoices (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Invoices PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        InvoiceNo varchar(50) NULL,
        BillingBatchId uniqueidentifier NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        InvoiceDate date NOT NULL,
        DueDate date NULL,
        Subtotal decimal(18,2) NOT NULL,
        VatAmount decimal(18,2) NOT NULL,
        TotalAmount decimal(18,2) NOT NULL,
        PaidAmount decimal(18,2) NOT NULL CONSTRAINT DF_Invoices_Paid DEFAULT 0,
        Status varchar(30) NOT NULL,
        ProviderCode varchar(40) NULL,
        ProviderTransactionId varchar(100) NULL,
        PdfFileUrl nvarchar(1000) NULL,
        XmlFileUrl nvarchar(1000) NULL,
        SignedAt datetime2(0) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Invoices_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Invoices_BillingBatches FOREIGN KEY(BillingBatchId) REFERENCES dbo.BillingBatches(Id),
        CONSTRAINT FK_Invoices_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id)
    );
    CREATE INDEX IX_Invoices_CustomerStatus ON dbo.Invoices(CustomerId, Status, DueDate);
END;

IF OBJECT_ID('dbo.Payments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Payments (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Payments PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        PaymentNo varchar(50) NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        PaymentDate date NOT NULL,
        Amount decimal(18,2) NOT NULL,
        Currency char(3) NOT NULL CONSTRAINT DF_Payments_Currency DEFAULT 'VND',
        Method varchar(30) NULL,
        BankReference varchar(100) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Payments_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_Payments_PaymentNo UNIQUE(PaymentNo),
        CONSTRAINT FK_Payments_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id)
    );
END;

IF OBJECT_ID('dbo.PaymentAllocations', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentAllocations (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_PaymentAllocations PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        PaymentId uniqueidentifier NOT NULL,
        InvoiceId uniqueidentifier NOT NULL,
        AllocatedAmount decimal(18,2) NOT NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_PaymentAllocations_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_PaymentAllocations_Payments FOREIGN KEY(PaymentId) REFERENCES dbo.Payments(Id) ON DELETE CASCADE,
        CONSTRAINT FK_PaymentAllocations_Invoices FOREIGN KEY(InvoiceId) REFERENCES dbo.Invoices(Id)
    );
END;

IF OBJECT_ID('dbo.ApprovalRequests', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApprovalRequests (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ApprovalRequests PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        EntityType varchar(40) NOT NULL,
        EntityId uniqueidentifier NOT NULL,
        ApprovalType varchar(40) NOT NULL,
        Reason nvarchar(1000) NULL,
        RequestedBy varchar(200) NOT NULL,
        RequestedAt datetime2(0) NOT NULL CONSTRAINT DF_ApprovalRequests_RequestedAt DEFAULT SYSUTCDATETIME(),
        Status varchar(30) NOT NULL,
        ApprovedBy varchar(200) NULL,
        ApprovedAt datetime2(0) NULL,
        DecisionNote nvarchar(1000) NULL
    );
END;

IF OBJECT_ID('dbo.AuditLogs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
        UserId varchar(200) NULL,
        ActionCode varchar(50) NOT NULL,
        EntityType varchar(50) NOT NULL,
        EntityId varchar(100) NOT NULL,
        BeforeJson nvarchar(max) NULL,
        AfterJson nvarchar(max) NULL,
        IpAddress varchar(64) NULL,
        CorrelationId varchar(100) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_AuditLogs_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_AuditLogs_Entity ON dbo.AuditLogs(EntityType, EntityId, CreatedAt DESC);
END;

IF OBJECT_ID('dbo.IntegrationLogs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.IntegrationLogs (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_IntegrationLogs PRIMARY KEY,
        IntegrationCode varchar(50) NOT NULL,
        OperationCode varchar(50) NOT NULL,
        CorrelationId varchar(100) NOT NULL,
        RequestBody nvarchar(max) NULL,
        ResponseBody nvarchar(max) NULL,
        HttpStatus int NULL,
        Status varchar(30) NOT NULL,
        ErrorMessage nvarchar(2000) NULL,
        RetryCount int NOT NULL CONSTRAINT DF_IntegrationLogs_Retry DEFAULT 0,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_IntegrationLogs_CreatedAt DEFAULT SYSUTCDATETIME(),
        CompletedAt datetime2(0) NULL
    );
    CREATE INDEX IX_IntegrationLogs_Correlation ON dbo.IntegrationLogs(CorrelationId);
END;
GO
