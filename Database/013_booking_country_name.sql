/*
  Booking lưu tên quốc gia theo Rate Hub (ZONE sheet), không chỉ ISO 2 ký tự.
*/

IF OBJECT_ID(N'dbo.Bookings_ABCourier', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Bookings_ABCourier', N'FromCountryCode') IS NOT NULL
       AND COL_LENGTH(N'dbo.Bookings_ABCourier', N'FromCountryCode') < 160
    BEGIN
        ALTER TABLE dbo.Bookings_ABCourier ALTER COLUMN FromCountryCode nvarchar(80) NOT NULL;
    END

    IF COL_LENGTH(N'dbo.Bookings_ABCourier', N'ToCountryCode') IS NOT NULL
       AND COL_LENGTH(N'dbo.Bookings_ABCourier', N'ToCountryCode') < 160
    BEGIN
        ALTER TABLE dbo.Bookings_ABCourier ALTER COLUMN ToCountryCode nvarchar(80) NOT NULL;
    END
END
GO
