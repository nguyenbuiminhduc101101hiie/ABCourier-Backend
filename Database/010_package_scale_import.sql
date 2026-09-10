IF OBJECT_ID(N'dbo.PackageScaleBatches_ABCourier', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PackageScaleBatches_ABCourier
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PackageScaleBatches_ABCourier PRIMARY KEY,
        FileName NVARCHAR(260) NOT NULL,
        ExcelRowCount INT NOT NULL,
        MatchedAwbCount INT NOT NULL,
        UnmatchedAwbCount INT NOT NULL,
        QuoteUpdatedCount INT NOT NULL,
        ImportedByUserId UNIQUEIDENTIFIER NULL,
        CreatedAtUtc DATETIME2 NOT NULL
    );
END
GO

IF OBJECT_ID(N'dbo.PackageScaleLines_ABCourier', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PackageScaleLines_ABCourier
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PackageScaleLines_ABCourier PRIMARY KEY,
        BatchId UNIQUEIDENTIFIER NOT NULL,
        JobId UNIQUEIDENTIFIER NOT NULL,
        BookingId UNIQUEIDENTIFIER NOT NULL,
        DeclaredPackageId UNIQUEIDENTIFIER NULL,
        Awb NVARCHAR(40) NOT NULL,
        PieceNo INT NOT NULL,
        ExcelRow INT NOT NULL,
        ScaleTicketNo NVARCHAR(40) NULL,
        ActualWeight DECIMAL(18, 3) NOT NULL,
        LengthCm DECIMAL(18, 2) NOT NULL,
        WidthCm DECIMAL(18, 2) NOT NULL,
        HeightCm DECIMAL(18, 2) NOT NULL,
        DimWeight DECIMAL(18, 3) NOT NULL,
        ChargeableWeight DECIMAL(18, 3) NOT NULL,
        PackageType NVARCHAR(20) NOT NULL,
        WeighedAt DATETIME2 NULL,
        WeighedBy NVARCHAR(80) NULL,
        Note NVARCHAR(300) NULL,
        CreatedAtUtc DATETIME2 NOT NULL,
        CONSTRAINT FK_PackageScaleLines_Batch
            FOREIGN KEY (BatchId) REFERENCES dbo.PackageScaleBatches_ABCourier (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_PackageScaleLines_ABCourier_Awb ON dbo.PackageScaleLines_ABCourier (Awb);
    CREATE INDEX IX_PackageScaleLines_ABCourier_JobId ON dbo.PackageScaleLines_ABCourier (JobId);
END
GO
