SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.RateImportBatches_ABCourier', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.RateImportBatches_ABCourier', N'SalesUserId') IS NULL
BEGIN
    ALTER TABLE dbo.RateImportBatches_ABCourier ADD SalesUserId uniqueidentifier NULL;
END
GO

IF OBJECT_ID(N'dbo.RateImportBatches_ABCourier', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.RateImportBatches_ABCourier', N'SalesUserId') IS NOT NULL
   AND OBJECT_ID(N'dbo.Users_ABCourier', N'U') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_RateImportBatches_ABCourier_SalesUser'
   )
BEGIN
    ALTER TABLE dbo.RateImportBatches_ABCourier
        ADD CONSTRAINT FK_RateImportBatches_ABCourier_SalesUser
            FOREIGN KEY (SalesUserId) REFERENCES dbo.Users_ABCourier(Id);
END
GO

IF OBJECT_ID(N'dbo.RateImportBatches_ABCourier', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.RateImportBatches_ABCourier', N'SalesUserId') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1 FROM sys.indexes WHERE name = N'IX_RateImportBatches_ABCourier_SalesUserId'
   )
BEGIN
    CREATE INDEX IX_RateImportBatches_ABCourier_SalesUserId
        ON dbo.RateImportBatches_ABCourier (SalesUserId);
END
GO
