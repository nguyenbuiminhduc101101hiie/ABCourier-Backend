/*
  Extend Users with Department for Customer / Staff / Admin login.
  Run against ABCourierRateDb (or target database).
*/
SET NOCOUNT ON;

IF COL_LENGTH('dbo.Users', 'Department') IS NULL
BEGIN
    ALTER TABLE dbo.Users ADD Department varchar(20) NOT NULL
        CONSTRAINT DF_Users_Department DEFAULT 'Admin';
END
GO

IF COL_LENGTH('dbo.Users', 'CustomerId') IS NULL
BEGIN
    ALTER TABLE dbo.Users ADD CustomerId uniqueidentifier NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE name = 'UQ_Users_Email' AND object_id = OBJECT_ID('dbo.Users')
)
BEGIN
    CREATE UNIQUE INDEX UQ_Users_Email ON dbo.Users(Email);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE name = 'IX_Users_Department' AND object_id = OBJECT_ID('dbo.Users')
)
BEGIN
    CREATE INDEX IX_Users_Department ON dbo.Users(Department, IsActive);
END
GO
