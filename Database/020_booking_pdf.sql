SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.BookingPdfs_ABCourier', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingPdfs_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_BookingPdfs_ABCourier PRIMARY KEY,
        BookingId uniqueidentifier NOT NULL,
        DownloadToken varchar(64) NOT NULL,
        FileName nvarchar(260) NOT NULL,
        ContentType varchar(80) NOT NULL CONSTRAINT DF_BookingPdfs_ABCourier_ContentType DEFAULT 'application/pdf',
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_BookingPdfs_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_BookingPdfs_ABCourier_Bookings
            FOREIGN KEY (BookingId) REFERENCES dbo.Bookings_ABCourier(Id),
        CONSTRAINT UQ_BookingPdfs_ABCourier_Token UNIQUE (DownloadToken)
    );
    CREATE INDEX IX_BookingPdfs_ABCourier_Booking ON dbo.BookingPdfs_ABCourier (BookingId);
END
GO

IF OBJECT_ID(N'dbo.BookingPdfBlobs_ABCourier', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingPdfBlobs_ABCourier (
        BookingPdfId uniqueidentifier NOT NULL CONSTRAINT PK_BookingPdfBlobs_ABCourier PRIMARY KEY,
        FileContent varbinary(max) NOT NULL,
        CONSTRAINT FK_BookingPdfBlobs_ABCourier_Pdfs
            FOREIGN KEY (BookingPdfId) REFERENCES dbo.BookingPdfs_ABCourier(Id) ON DELETE CASCADE
    );
END
GO
