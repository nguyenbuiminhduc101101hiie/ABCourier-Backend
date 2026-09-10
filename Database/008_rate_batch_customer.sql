/*
  Gắn khách hàng (Customers_ABCourier) vào mỗi bộ giá RateImportBatches_ABCourier.
  Mỗi customer có thể có nhiều bộ giá; nhân bản tạo bộ giá mới cho cùng hoặc khác customer.
*/

IF COL_LENGTH('dbo.RateImportBatches_ABCourier', 'CustomerId') IS NULL
BEGIN
    ALTER TABLE dbo.RateImportBatches_ABCourier
        ADD CustomerId uniqueidentifier NULL;

    CREATE INDEX IX_RateImportBatches_ABCourier_CustomerId
        ON dbo.RateImportBatches_ABCourier(CustomerId);

    IF OBJECT_ID('dbo.Customers_ABCourier', 'U') IS NOT NULL
        AND OBJECT_ID('dbo.FK_RateImportBatches_ABCourier_Customers', 'F') IS NULL
    BEGIN
        ALTER TABLE dbo.RateImportBatches_ABCourier
            ADD CONSTRAINT FK_RateImportBatches_ABCourier_Customers
            FOREIGN KEY (CustomerId) REFERENCES dbo.Customers_ABCourier(Id);
    END
END;
GO
