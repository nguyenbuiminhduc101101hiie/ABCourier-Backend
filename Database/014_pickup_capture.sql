SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.PickupTasks_ABCourier', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.PickupTasks_ABCourier', N'ConfirmationCount') IS NULL
BEGIN
    ALTER TABLE dbo.PickupTasks_ABCourier
        ADD ConfirmationCount int NOT NULL
            CONSTRAINT DF_PickupTasks_ABCourier_ConfirmationCount DEFAULT 0,
            LastConfirmedAt datetime2(0) NULL,
            LastConfirmedByUserId uniqueidentifier NULL;
END
GO

IF OBJECT_ID(N'dbo.PickupEvidence_ABCourier', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PickupEvidence_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_PickupEvidence_ABCourier PRIMARY KEY,
        PickupTaskId uniqueidentifier NOT NULL,
        Kind varchar(20) NOT NULL,
        FileName nvarchar(260) NOT NULL,
        StoredFileName varchar(80) NOT NULL,
        ContentType varchar(80) NOT NULL,
        IsCurrent bit NOT NULL CONSTRAINT DF_PickupEvidence_ABCourier_IsCurrent DEFAULT 1,
        CreatedByUserId uniqueidentifier NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_PickupEvidence_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_PickupEvidence_ABCourier_Tasks
            FOREIGN KEY (PickupTaskId) REFERENCES dbo.PickupTasks_ABCourier(Id)
    );
    CREATE INDEX IX_PickupEvidence_ABCourier_Task ON dbo.PickupEvidence_ABCourier (PickupTaskId);
END
GO

IF OBJECT_ID(N'dbo.PickupConfirmationLogs_ABCourier', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PickupConfirmationLogs_ABCourier (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_PickupConfirmationLogs_ABCourier PRIMARY KEY,
        PickupTaskId uniqueidentifier NOT NULL,
        SequenceNo int NOT NULL,
        Action varchar(40) NOT NULL,
        Detail nvarchar(500) NOT NULL,
        PhotoCountAdded int NOT NULL CONSTRAINT DF_PickupConfirmationLogs_ABCourier_Photos DEFAULT 0,
        SignatureChanged bit NOT NULL CONSTRAINT DF_PickupConfirmationLogs_ABCourier_Sig DEFAULT 0,
        CreatedByUserId uniqueidentifier NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_PickupConfirmationLogs_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_PickupConfirmationLogs_ABCourier_Tasks
            FOREIGN KEY (PickupTaskId) REFERENCES dbo.PickupTasks_ABCourier(Id),
        CONSTRAINT UQ_PickupConfirmationLogs_ABCourier_Seq UNIQUE (PickupTaskId, SequenceNo)
    );
END
GO

IF OBJECT_ID(N'dbo.PickupEvidenceBlobs_ABCourier', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PickupEvidenceBlobs_ABCourier (
        EvidenceId uniqueidentifier NOT NULL CONSTRAINT PK_PickupEvidenceBlobs_ABCourier PRIMARY KEY,
        FileContent varbinary(max) NOT NULL,
        CONSTRAINT FK_PickupEvidenceBlobs_ABCourier_Evidence
            FOREIGN KEY (EvidenceId) REFERENCES dbo.PickupEvidence_ABCourier(Id) ON DELETE CASCADE
    );
END
GO
