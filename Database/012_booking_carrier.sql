/*
  Lưu đơn vị vận chuyển đã chọn / hệ thống chọn khi tạo booking.
*/

IF OBJECT_ID(N'dbo.Bookings_ABCourier', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Bookings_ABCourier', N'CarrierCode') IS NULL
BEGIN
    ALTER TABLE dbo.Bookings_ABCourier
        ADD CarrierCode varchar(20) NOT NULL
            CONSTRAINT DF_Bookings_ABCourier_CarrierCode DEFAULT '';
END;
GO
