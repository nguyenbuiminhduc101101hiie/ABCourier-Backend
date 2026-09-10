IF DB_ID(N'CourierRateDb') IS NULL
BEGIN
    CREATE DATABASE CourierRateDb;
    PRINT 'Created CourierRateDb';
END
ELSE
BEGIN
    PRINT 'CourierRateDb already exists';
END
GO
