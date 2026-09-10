/*
  Thêm cột RateSetCode để người dùng đặt tên/mã bộ giá khi import và tra cứu lại.
  Chạy trên database ABfreight (bảng *_ABCourier).
*/

IF COL_LENGTH('dbo.RateImportBatches_ABCourier', 'RateSetCode') IS NULL
BEGIN
    ALTER TABLE dbo.RateImportBatches_ABCourier
        ADD RateSetCode nvarchar(80) NULL;

    CREATE INDEX IX_RateImportBatches_ABCourier_RateSetCode
        ON dbo.RateImportBatches_ABCourier(RateSetCode);
END;
GO
