IF DB_ID(N'ABCourierRateDb') IS NULL
BEGIN
    CREATE DATABASE ABCourierRateDb;
    PRINT 'Created ABCourierRateDb';
END
ELSE
BEGIN
    PRINT 'ABCourierRateDb already exists';
END
GO
