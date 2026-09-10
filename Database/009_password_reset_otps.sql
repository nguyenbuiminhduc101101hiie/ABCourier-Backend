IF OBJECT_ID(N'dbo.PasswordResetOtps_ABCourier', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PasswordResetOtps_ABCourier
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PasswordResetOtps_ABCourier PRIMARY KEY,
        Email NVARCHAR(256) NOT NULL,
        Department NVARCHAR(50) NOT NULL,
        OtpHash NVARCHAR(128) NOT NULL,
        ExpiresAtUtc DATETIME2 NOT NULL,
        AttemptCount INT NOT NULL CONSTRAINT DF_PasswordResetOtps_AttemptCount DEFAULT(0),
        UsedAtUtc DATETIME2 NULL,
        CreatedAtUtc DATETIME2 NOT NULL
    );
    CREATE INDEX IX_PasswordResetOtps_ABCourier_EmailDept
        ON dbo.PasswordResetOtps_ABCourier (Email, Department, CreatedAtUtc);
END
GO
