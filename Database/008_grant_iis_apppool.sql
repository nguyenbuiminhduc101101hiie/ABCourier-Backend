/*
  IIS site apiabcourier dùng Trusted_Connection.
  App pool identity: IIS APPPOOL\apiabcourier
  Chạy trên SQL Server (cùng máy IIS) dưới quyền sysadmin.
*/
USE [master];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'IIS APPPOOL\apiabcourier')
BEGIN
    CREATE LOGIN [IIS APPPOOL\apiabcourier] FROM WINDOWS;
END;
GO

USE [ABfreight];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IIS APPPOOL\apiabcourier')
BEGIN
    CREATE USER [IIS APPPOOL\apiabcourier] FOR LOGIN [IIS APPPOOL\apiabcourier];
END;
GO

ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\apiabcourier];
ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\apiabcourier];
GO
