SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.CompanyInformation_ABCourier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompanyInformation_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CompanyInformation_ABCourier PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CompanyName nvarchar(200) NOT NULL,
        TaxCode varchar(30) NULL,
        Address nvarchar(500) NULL,
        Phone varchar(40) NULL,
        Email varchar(200) NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_CompanyInformation_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_CompanyInformation_ABCourier_UpdatedAt DEFAULT SYSUTCDATETIME()
    );
END;
GO
