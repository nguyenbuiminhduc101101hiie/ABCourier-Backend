/*
  Allow Users.Department to store multiple roles, e.g. Admin,Staff,Customer
*/
SET NOCOUNT ON;

IF COL_LENGTH('dbo.Users', 'Department') IS NOT NULL
BEGIN
    ALTER TABLE dbo.Users ALTER COLUMN Department varchar(100) NOT NULL;
END
GO
