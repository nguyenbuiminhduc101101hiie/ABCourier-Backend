SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CourierLocationTracks_ABCourier', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CourierLocationTracks_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CourierLocationTracks_ABCourier PRIMARY KEY,
        PickupTaskId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        Latitude decimal(10,7) NOT NULL,
        Longitude decimal(10,7) NOT NULL,
        RecordedAtUtc datetime2(0) NOT NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_CourierLocationTracks_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_CourierLocationTracks_ABCourier_Tasks
            FOREIGN KEY (PickupTaskId) REFERENCES dbo.PickupTasks_ABCourier(Id),
        CONSTRAINT FK_CourierLocationTracks_ABCourier_Users
            FOREIGN KEY (UserId) REFERENCES dbo.Users_ABCourier(Id)
    );
    CREATE INDEX IX_CourierLocationTracks_ABCourier_Task ON dbo.CourierLocationTracks_ABCourier (PickupTaskId, RecordedAtUtc);
END
GO
