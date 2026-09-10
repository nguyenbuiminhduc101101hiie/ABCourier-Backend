/*
  Optional SQL Server script.
  The sample API calls Database.EnsureCreated(), so this script is mainly for merging
  the module into an existing database where automatic schema creation is disabled.
*/

IF OBJECT_ID('dbo.RateImportBatches', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RateImportBatches
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_RateImportBatches PRIMARY KEY,
        CarrierCode varchar(20) NOT NULL,
        FileName nvarchar(260) NOT NULL,
        FileHash varchar(64) NOT NULL,
        Status varchar(30) NOT NULL,
        EffectiveFrom datetime2(0) NULL,
        RateCardCount int NOT NULL CONSTRAINT DF_RateImportBatches_RateCardCount DEFAULT 0,
        RateBandCount int NOT NULL CONSTRAINT DF_RateImportBatches_RateBandCount DEFAULT 0,
        CountryZoneCount int NOT NULL CONSTRAINT DF_RateImportBatches_CountryZoneCount DEFAULT 0,
        SurchargeCount int NOT NULL CONSTRAINT DF_RateImportBatches_SurchargeCount DEFAULT 0,
        WarningCount int NOT NULL CONSTRAINT DF_RateImportBatches_WarningCount DEFAULT 0,
        ErrorCount int NOT NULL CONSTRAINT DF_RateImportBatches_ErrorCount DEFAULT 0,
        CreatedAtUtc datetime2(0) NOT NULL,
        CommittedAtUtc datetime2(0) NULL
    );
    CREATE INDEX IX_RateImportBatches_Carrier_FileHash
        ON dbo.RateImportBatches(CarrierCode, FileHash);
END;
GO

IF OBJECT_ID('dbo.RateCards', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RateCards
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_RateCards PRIMARY KEY,
        ImportBatchId uniqueidentifier NOT NULL,
        CarrierCode varchar(20) NOT NULL,
        Direction varchar(10) NOT NULL,
        ServiceCode varchar(10) NOT NULL,
        PackageType varchar(20) NOT NULL,
        Currency char(3) NOT NULL CONSTRAINT DF_RateCards_Currency DEFAULT 'VND',
        EffectiveFrom datetime2(0) NOT NULL,
        EffectiveTo datetime2(0) NULL,
        FuelMultiplier decimal(18,6) NOT NULL CONSTRAINT DF_RateCards_Fuel DEFAULT 1,
        VatMultiplier decimal(18,6) NOT NULL CONSTRAINT DF_RateCards_Vat DEFAULT 1,
        IsActive bit NOT NULL CONSTRAINT DF_RateCards_IsActive DEFAULT 0,
        SourceSheet nvarchar(128) NOT NULL,
        CONSTRAINT FK_RateCards_RateImportBatches FOREIGN KEY (ImportBatchId)
            REFERENCES dbo.RateImportBatches(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_RateCards_Lookup
        ON dbo.RateCards(CarrierCode, Direction, ServiceCode, PackageType, EffectiveFrom);
END;
GO

IF OBJECT_ID('dbo.RateBands', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RateBands
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_RateBands PRIMARY KEY,
        RateCardId uniqueidentifier NOT NULL,
        ZoneCode varchar(10) NOT NULL,
        WeightFrom decimal(18,3) NOT NULL,
        WeightTo decimal(18,3) NOT NULL,
        RateUnit varchar(20) NOT NULL,
        Amount decimal(18,2) NOT NULL,
        SourceRow int NOT NULL,
        CONSTRAINT FK_RateBands_RateCards FOREIGN KEY (RateCardId)
            REFERENCES dbo.RateCards(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_RateBands UNIQUE(RateCardId, ZoneCode, WeightFrom, WeightTo)
    );
END;
GO

IF OBJECT_ID('dbo.CountryZoneMappings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CountryZoneMappings
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CountryZoneMappings PRIMARY KEY,
        ImportBatchId uniqueidentifier NOT NULL,
        CarrierCode varchar(20) NOT NULL,
        Direction varchar(10) NOT NULL,
        CountryName nvarchar(160) NOT NULL,
        ServiceCode varchar(10) NOT NULL,
        ZoneCode varchar(10) NOT NULL,
        IsSupported bit NOT NULL,
        SourceSheet nvarchar(128) NOT NULL,
        SourceRow int NOT NULL,
        CONSTRAINT FK_CountryZoneMappings_RateImportBatches FOREIGN KEY (ImportBatchId)
            REFERENCES dbo.RateImportBatches(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_CountryZoneMappings UNIQUE(ImportBatchId, Direction, CountryName, ServiceCode)
    );
END;
GO

IF OBJECT_ID('dbo.RateSurcharges', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RateSurcharges
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_RateSurcharges PRIMARY KEY,
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
        IsInformational bit NOT NULL CONSTRAINT DF_RateSurcharges_Info DEFAULT 0,
        SourceSheet nvarchar(128) NOT NULL,
        SourceCell varchar(20) NOT NULL,
        CONSTRAINT FK_RateSurcharges_RateImportBatches FOREIGN KEY (ImportBatchId)
            REFERENCES dbo.RateImportBatches(Id) ON DELETE CASCADE
    );
END;
GO
