SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.Customers_ABCourier', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Customers_ABCourier', N'Address') IS NULL
BEGIN
    ALTER TABLE dbo.Customers_ABCourier ADD Address nvarchar(500) NULL;
END
GO
