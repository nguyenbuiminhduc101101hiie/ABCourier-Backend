/*
  Optional: copy existing rows from ABCourierRateDb -> ABfreight (*_ABCourier).
  Run AFTER 005_create_ABCourier_tables_on_ABfreight.sql on the same server.
  Skips insert when target already has rows for that table.
*/
SET NOCOUNT ON;
GO

USE ABfreight;
GO

IF DB_ID('ABCourierRateDb') IS NULL
BEGIN
    RAISERROR('Source database ABCourierRateDb not found on this server.', 16, 1);
    RETURN;
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Customers_ABCourier)
    INSERT INTO dbo.Customers_ABCourier SELECT * FROM ABCourierRateDb.dbo.Customers;

IF NOT EXISTS (SELECT 1 FROM dbo.CustomerUsers_ABCourier)
    AND OBJECT_ID('ABCourierRateDb.dbo.CustomerUsers', 'U') IS NOT NULL
    INSERT INTO dbo.CustomerUsers_ABCourier SELECT * FROM ABCourierRateDb.dbo.CustomerUsers;

IF NOT EXISTS (SELECT 1 FROM dbo.CustomerAddresses_ABCourier)
    INSERT INTO dbo.CustomerAddresses_ABCourier SELECT * FROM ABCourierRateDb.dbo.CustomerAddresses;

IF NOT EXISTS (SELECT 1 FROM dbo.Users_ABCourier)
    INSERT INTO dbo.Users_ABCourier (Id, Email, DisplayName, PasswordHash, Department, CustomerId, IsActive, CreatedAtUtc, LastLoginAtUtc)
    SELECT Id, Email, DisplayName, PasswordHash, Department, CustomerId, IsActive, CreatedAtUtc, LastLoginAtUtc
    FROM ABCourierRateDb.dbo.Users;

IF NOT EXISTS (SELECT 1 FROM dbo.RateImportBatches_ABCourier)
    INSERT INTO dbo.RateImportBatches_ABCourier SELECT * FROM ABCourierRateDb.dbo.RateImportBatches;

IF NOT EXISTS (SELECT 1 FROM dbo.RateCards_ABCourier)
    INSERT INTO dbo.RateCards_ABCourier SELECT * FROM ABCourierRateDb.dbo.RateCards;

IF NOT EXISTS (SELECT 1 FROM dbo.RateBands_ABCourier)
    INSERT INTO dbo.RateBands_ABCourier SELECT * FROM ABCourierRateDb.dbo.RateBands;

IF NOT EXISTS (SELECT 1 FROM dbo.CountryZoneMappings_ABCourier)
    INSERT INTO dbo.CountryZoneMappings_ABCourier SELECT * FROM ABCourierRateDb.dbo.CountryZoneMappings;

IF NOT EXISTS (SELECT 1 FROM dbo.RateSurcharges_ABCourier)
    INSERT INTO dbo.RateSurcharges_ABCourier SELECT * FROM ABCourierRateDb.dbo.RateSurcharges;

IF NOT EXISTS (SELECT 1 FROM dbo.Bookings_ABCourier)
    INSERT INTO dbo.Bookings_ABCourier SELECT * FROM ABCourierRateDb.dbo.Bookings;

IF NOT EXISTS (SELECT 1 FROM dbo.BookingPackages_ABCourier)
    INSERT INTO dbo.BookingPackages_ABCourier SELECT * FROM ABCourierRateDb.dbo.BookingPackages;

IF NOT EXISTS (SELECT 1 FROM dbo.BookingCommodities_ABCourier)
    AND OBJECT_ID('ABCourierRateDb.dbo.BookingCommodities', 'U') IS NOT NULL
    INSERT INTO dbo.BookingCommodities_ABCourier SELECT * FROM ABCourierRateDb.dbo.BookingCommodities;

IF NOT EXISTS (SELECT 1 FROM dbo.BookingQuotes_ABCourier)
    INSERT INTO dbo.BookingQuotes_ABCourier SELECT * FROM ABCourierRateDb.dbo.BookingQuotes;

IF NOT EXISTS (SELECT 1 FROM dbo.Jobs_ABCourier)
    INSERT INTO dbo.Jobs_ABCourier SELECT * FROM ABCourierRateDb.dbo.Jobs;

IF NOT EXISTS (SELECT 1 FROM dbo.TrackingEvents_ABCourier)
    INSERT INTO dbo.TrackingEvents_ABCourier SELECT * FROM ABCourierRateDb.dbo.TrackingEvents;

IF NOT EXISTS (SELECT 1 FROM dbo.PickupTasks_ABCourier)
    INSERT INTO dbo.PickupTasks_ABCourier SELECT * FROM ABCourierRateDb.dbo.PickupTasks;

IF NOT EXISTS (SELECT 1 FROM dbo.JobDocuments_ABCourier)
    AND OBJECT_ID('ABCourierRateDb.dbo.JobDocuments', 'U') IS NOT NULL
    INSERT INTO dbo.JobDocuments_ABCourier SELECT * FROM ABCourierRateDb.dbo.JobDocuments;

IF NOT EXISTS (SELECT 1 FROM dbo.BillingBatches_ABCourier)
    AND OBJECT_ID('ABCourierRateDb.dbo.BillingBatches', 'U') IS NOT NULL
    INSERT INTO dbo.BillingBatches_ABCourier SELECT * FROM ABCourierRateDb.dbo.BillingBatches;

IF NOT EXISTS (SELECT 1 FROM dbo.JobChargeLines_ABCourier)
    INSERT INTO dbo.JobChargeLines_ABCourier SELECT * FROM ABCourierRateDb.dbo.JobChargeLines;

IF NOT EXISTS (SELECT 1 FROM dbo.Invoices_ABCourier)
    INSERT INTO dbo.Invoices_ABCourier SELECT * FROM ABCourierRateDb.dbo.Invoices;

IF NOT EXISTS (SELECT 1 FROM dbo.Payments_ABCourier)
    AND OBJECT_ID('ABCourierRateDb.dbo.Payments', 'U') IS NOT NULL
    INSERT INTO dbo.Payments_ABCourier SELECT * FROM ABCourierRateDb.dbo.Payments;

IF NOT EXISTS (SELECT 1 FROM dbo.PaymentAllocations_ABCourier)
    AND OBJECT_ID('ABCourierRateDb.dbo.PaymentAllocations', 'U') IS NOT NULL
    INSERT INTO dbo.PaymentAllocations_ABCourier SELECT * FROM ABCourierRateDb.dbo.PaymentAllocations;

IF NOT EXISTS (SELECT 1 FROM dbo.ApprovalRequests_ABCourier)
    AND OBJECT_ID('ABCourierRateDb.dbo.ApprovalRequests', 'U') IS NOT NULL
    INSERT INTO dbo.ApprovalRequests_ABCourier SELECT * FROM ABCourierRateDb.dbo.ApprovalRequests;

PRINT '006_copy_data_ABCourier_to_ABfreight: done';
GO
