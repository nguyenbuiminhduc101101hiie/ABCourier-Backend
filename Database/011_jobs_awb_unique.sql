IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_Jobs_ABCourier_Awb'
      AND object_id = OBJECT_ID(N'dbo.Jobs_ABCourier')
)
AND NOT EXISTS (
    SELECT 1 FROM dbo.Jobs_ABCourier
    WHERE Awb IS NOT NULL AND Awb <> ''
    GROUP BY Awb
    HAVING COUNT(*) > 1
)
BEGIN
    CREATE UNIQUE INDEX UX_Jobs_ABCourier_Awb
        ON dbo.Jobs_ABCourier(Awb)
        WHERE Awb IS NOT NULL AND Awb <> '';
END
GO
