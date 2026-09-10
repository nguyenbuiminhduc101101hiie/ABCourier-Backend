using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CourierApi.Contracts;
using CourierApi.Data;
using CourierApi.Domain;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;

namespace CourierApi.Services;

public sealed class ScaleImportService(AppDbContext db, RateQuoteService quotes)
{
    public async Task EnsureTablesAsync(CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.PackageScaleBatches_ABCourier', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PackageScaleBatches_ABCourier
                (
                    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PackageScaleBatches_ABCourier PRIMARY KEY,
                    FileName NVARCHAR(260) NOT NULL,
                    ExcelRowCount INT NOT NULL,
                    MatchedAwbCount INT NOT NULL,
                    UnmatchedAwbCount INT NOT NULL,
                    QuoteUpdatedCount INT NOT NULL,
                    ImportedByUserId UNIQUEIDENTIFIER NULL,
                    CreatedAtUtc DATETIME2 NOT NULL
                );
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.PackageScaleLines_ABCourier', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PackageScaleLines_ABCourier
                (
                    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PackageScaleLines_ABCourier PRIMARY KEY,
                    BatchId UNIQUEIDENTIFIER NOT NULL,
                    JobId UNIQUEIDENTIFIER NOT NULL,
                    BookingId UNIQUEIDENTIFIER NOT NULL,
                    DeclaredPackageId UNIQUEIDENTIFIER NULL,
                    Awb NVARCHAR(40) NOT NULL,
                    PieceNo INT NOT NULL,
                    ExcelRow INT NOT NULL,
                    ScaleTicketNo NVARCHAR(40) NULL,
                    ActualWeight DECIMAL(18, 3) NOT NULL,
                    LengthCm DECIMAL(18, 2) NOT NULL,
                    WidthCm DECIMAL(18, 2) NOT NULL,
                    HeightCm DECIMAL(18, 2) NOT NULL,
                    DimWeight DECIMAL(18, 3) NOT NULL,
                    ChargeableWeight DECIMAL(18, 3) NOT NULL,
                    PackageType NVARCHAR(20) NOT NULL,
                    WeighedAt DATETIME2 NULL,
                    WeighedBy NVARCHAR(80) NULL,
                    Note NVARCHAR(300) NULL,
                    CreatedAtUtc DATETIME2 NOT NULL,
                    CONSTRAINT FK_PackageScaleLines_Batch
                        FOREIGN KEY (BatchId) REFERENCES dbo.PackageScaleBatches_ABCourier (Id) ON DELETE CASCADE
                );
                CREATE INDEX IX_PackageScaleLines_ABCourier_Awb ON dbo.PackageScaleLines_ABCourier (Awb);
                CREATE INDEX IX_PackageScaleLines_ABCourier_JobId ON dbo.PackageScaleLines_ABCourier (JobId);
            END
            """, ct);
    }

    public async Task<ScaleImportResult> ImportAsync(
        Stream stream,
        string fileName,
        Guid? importedByUserId,
        CancellationToken ct = default)
    {
        var rows = ReadExcel(stream);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("File Excel không có dòng cân hợp lệ (cần SỐ VẬN ĐƠN, TL CÂN, DÀI, RỘNG, CAO).");
        }

        var jobs = await db.Jobs
            .Include(x => x.Booking).ThenInclude(b => b.Packages)
            .Include(x => x.Booking).ThenInclude(b => b.Quotes)
            .Include(x => x.Booking).ThenInclude(b => b.ReceiverAddress)
            .Include(x => x.ChargeLines)
            .Include(x => x.TrackingEvents)
            .Where(x => x.Awb != null)
            .ToListAsync(ct);

        var jobsByAwb = new Dictionary<string, Job>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in jobs)
        {
            var key = NormalizeAwb(job.Awb!);
            if (key.Length > 0) jobsByAwb.TryAdd(key, job);
        }

        var batch = new PackageScaleBatch
        {
            Id = Guid.NewGuid(),
            FileName = Path.GetFileName(fileName),
            ExcelRowCount = rows.Count,
            ImportedByUserId = importedByUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        var grouped = rows.GroupBy(x => x.Awb, StringComparer.OrdinalIgnoreCase);
        var results = new List<ScaleImportAwbResult>();
        var matchedJobs = new HashSet<Guid>();

        foreach (var group in grouped)
        {
            var awb = group.Key;
            if (!jobsByAwb.TryGetValue(awb, out var job))
            {
                results.Add(new ScaleImportAwbResult(
                    awb, null, null, "Unmatched", group.Count(), null, null, null,
                    "Không tìm thấy job đã duyệt với AWB này."));
                continue;
            }

            matchedJobs.Add(job.Id);
            var oldQuote = job.Booking.Quotes.OrderByDescending(x => x.VersionNo).FirstOrDefault();
            var oldTotal = oldQuote?.TotalAmount;
            var declaredSlots = ExpandDeclaredSlots(job.Booking.Packages);
            var pieceNo = 1;
            var quotePackages = new List<BookingPackageDto>();

            var existing = await db.PackageScaleLines.Where(x => x.JobId == job.Id).ToListAsync(ct);
            if (existing.Count > 0) db.PackageScaleLines.RemoveRange(existing);

            foreach (var row in group.OrderBy(x => x.ExcelRow))
            {
                var slot = pieceNo <= declaredSlots.Count
                    ? declaredSlots[pieceNo - 1]
                    : declaredSlots.LastOrDefault();
                var packageType = slot.Type;
                var dim = Math.Round(row.Length * row.Width * row.Height / 5000m, 3);
                var chargeable = RateQuoteService.RoundChargeable(
                    Math.Max(row.Weight, dim),
                    packageType,
                    job.ServiceCode);

                batch.Lines.Add(new PackageScaleLine
                {
                    Id = Guid.NewGuid(),
                    BatchId = batch.Id,
                    JobId = job.Id,
                    BookingId = job.BookingId,
                    DeclaredPackageId = slot.PackageId,
                    Awb = job.Awb ?? awb,
                    PieceNo = pieceNo,
                    ExcelRow = row.ExcelRow,
                    ScaleTicketNo = row.TicketNo,
                    ActualWeight = row.Weight,
                    LengthCm = row.Length,
                    WidthCm = row.Width,
                    HeightCm = row.Height,
                    DimWeight = dim,
                    ChargeableWeight = chargeable,
                    PackageType = packageType,
                    WeighedAt = row.WeighedAt,
                    WeighedBy = row.WeighedBy,
                    Note = row.Note,
                    CreatedAtUtc = DateTime.UtcNow
                });

                quotePackages.Add(new BookingPackageDto(
                    1, row.Weight, row.Length, row.Width, row.Height, packageType, dim, chargeable));
                pieceNo++;
            }

            try
            {
                var rateQuote = await quotes.QuoteAsync(
                    job.CustomerId,
                    job.Direction,
                    job.ServiceCode,
                    job.Booking.FromCountryCode,
                    job.Booking.ToCountryCode,
                    job.Booking.ShipDate,
                    quotePackages,
                    job.Booking.ReceiverAddress?.IsResidential == true,
                    string.IsNullOrWhiteSpace(job.CarrierCode) ? "FEDEX" : job.CarrierCode,
                    oldQuote?.ZoneCode,
                    ct);

                foreach (var quote in job.Booking.Quotes.Where(x => x.Status == "Active"))
                {
                    quote.Status = "Superseded";
                }

                var snapshot = MergeSnapshot(rateQuote.SnapshotJson, "Scale", batch.Id);
                job.Booking.Quotes.Add(new BookingQuote
                {
                    Id = Guid.NewGuid(),
                    BookingId = job.BookingId,
                    VersionNo = (oldQuote?.VersionNo ?? 0) + 1,
                    RateCardId = rateQuote.RateCardId,
                    ZoneCode = string.IsNullOrWhiteSpace(oldQuote?.ZoneCode) ? rateQuote.Zone : oldQuote!.ZoneCode,
                    ChargeableWeight = rateQuote.ChargeableWeight,
                    Freight = rateQuote.Freight,
                    FuelSurcharge = rateQuote.Fuel,
                    DemandSurcharge = rateQuote.Demand,
                    HandlingCharge = rateQuote.Handling,
                    VatAmount = rateQuote.Vat,
                    TotalAmount = rateQuote.Total,
                    CalculationSnapshot = snapshot,
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow
                });

                job.ActualWeight = quotePackages.Sum(x => x.ActualWeight);
                job.ChargeableWeight = rateQuote.ChargeableWeight;
                job.UpdatedAt = DateTime.UtcNow;
                ApplyQuoteToChargeLines(job, rateQuote, batch.Id);

                db.TrackingEvents.Add(new TrackingEvent
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    EventCode = "SCALE",
                    EventTitle = "Đã nhập bảng cân",
                    EventDescription = $"{quotePackages.Count} kiện từ {batch.FileName}",
                    EventTime = DateTime.UtcNow,
                    SourceType = "ScaleImport",
                    CreatedAt = DateTime.UtcNow
                });

                results.Add(new ScaleImportAwbResult(
                    awb, job.JobNo, job.Booking.BookingNo, "Quoted", quotePackages.Count,
                    oldTotal, rateQuote.Total, rateQuote.ChargeableWeight, null));
            }
            catch (Exception ex)
            {
                results.Add(new ScaleImportAwbResult(
                    awb, job.JobNo, job.Booking.BookingNo, "QuoteFailed", quotePackages.Count,
                    oldTotal, null, null, ex.Message));
            }
        }

        batch.MatchedAwbCount = matchedJobs.Count;
        batch.UnmatchedAwbCount = results.Count(x => x.Status == "Unmatched");
        batch.QuoteUpdatedCount = results.Count(x => x.Status == "Quoted");
        db.PackageScaleBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        return new ScaleImportResult(
            batch.Id,
            batch.FileName,
            batch.ExcelRowCount,
            batch.MatchedAwbCount,
            batch.UnmatchedAwbCount,
            batch.QuoteUpdatedCount,
            results.OrderBy(x => x.Status).ThenBy(x => x.Awb).ToList());
    }

    private static void ApplyQuoteToChargeLines(Job job, BookingRateQuoteDto quote, Guid batchId)
    {
        UpsertDebit(job, "FREIGHT", "Cước vận chuyển (sau cân)", quote.Freight, batchId);
        UpsertDebit(job, "FUEL", "Phụ phí nhiên liệu (sau cân)", quote.Fuel, batchId);
        UpsertDebit(job, "DEMAND", "Phụ phí nhu cầu (sau cân)", quote.Demand, batchId);
        UpsertDebit(job, "HANDLING", "Phí xử lý (sau cân)", quote.Handling, batchId);
    }

    private static void UpsertDebit(Job job, string code, string description, decimal amount, Guid batchId)
    {
        var line = job.ChargeLines.FirstOrDefault(x =>
            x.ChargeKind == "Debit" &&
            x.ChargeCode == code &&
            !x.IsLocked);
        if (line is null)
        {
            job.ChargeLines.Add(new JobChargeLine
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                ChargeKind = "Debit",
                ChargeCode = code,
                Description = description,
                Quantity = 1,
                UnitPrice = amount,
                Amount = amount,
                SourceType = "ScaleImport",
                SourceReference = batchId.ToString(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return;
        }

        line.Description = description;
        line.UnitPrice = amount;
        line.Amount = amount;
        line.SourceType = "ScaleImport";
        line.SourceReference = batchId.ToString();
        line.UpdatedAt = DateTime.UtcNow;
    }

    private static string MergeSnapshot(string? json, string source, Guid batchId)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var map = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                map[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.TryGetDecimal(out var n) ? n : prop.Value.GetRawText(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => prop.Value.GetRawText()
                };
            }
            map["source"] = source;
            map["scaleBatchId"] = batchId;
            return JsonSerializer.Serialize(map);
        }
        catch
        {
            return JsonSerializer.Serialize(new { source, scaleBatchId = batchId });
        }
    }

    private static List<(Guid? PackageId, string Type)> ExpandDeclaredSlots(IEnumerable<BookingPackage> packages)
    {
        var slots = new List<(Guid? PackageId, string Type)>();
        foreach (var pkg in packages.OrderBy(x => x.PackageNo))
        {
            var type = string.IsNullOrWhiteSpace(pkg.PackageType) ? "Package" : pkg.PackageType;
            var pieces = Math.Max(1, pkg.Pieces);
            for (var i = 0; i < pieces; i++) slots.Add((pkg.Id, type));
        }

        if (slots.Count == 0) slots.Add((null, "Package"));
        return slots;
    }

    private static List<ExcelScaleRow> ReadExcel(Stream stream)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var data = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
        });
        if (data.Tables.Count == 0) return [];

        var table = data.Tables[0];
        var headerRow = FindHeaderRow(table);
        if (headerRow < 0) return [];

        var map = MapColumns(table, headerRow);
        ApplyKnownLayoutFallback(table, headerRow, map);
        if (!map.ContainsKey("awb") || !map.ContainsKey("weight") ||
            !map.ContainsKey("length") || !map.ContainsKey("width") || !map.ContainsKey("height"))
        {
            var headers = string.Join(" | ", table.Rows[headerRow].ItemArray.Select((x, i) =>
                $"{(char)('A' + Math.Min(i, 25))}={x}"));
            throw new InvalidOperationException(
                "Không nhận ra cột SỐ VẬN ĐƠN / TL CÂN / DÀI / RỘNG / CAO trong file Excel. " +
                $"Header đọc được: {headers}");
        }

        var rows = new List<ExcelScaleRow>();
        for (var r = headerRow + 1; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            var awb = NormalizeAwb(CellText(row, map["awb"]));
            if (awb.Length == 0 || awb.Contains("CHUA", StringComparison.OrdinalIgnoreCase)) continue;

            var weight = CellDecimal(row, map["weight"]);
            var length = CellDecimal(row, map["length"]);
            var width = CellDecimal(row, map["width"]);
            var height = CellDecimal(row, map["height"]);
            if (weight <= 0 || length <= 0 || width <= 0 || height <= 0) continue;

            rows.Add(new ExcelScaleRow(
                awb,
                r + 1,
                weight,
                length,
                width,
                height,
                map.TryGetValue("ticket", out var ticketCol) ? NullIfEmpty(CellText(row, ticketCol)) : null,
                map.TryGetValue("weighedAt", out var atCol) ? CellDate(row, atCol) : null,
                map.TryGetValue("weighedBy", out var byCol) ? NullIfEmpty(CellText(row, byCol)) : null,
                map.TryGetValue("note", out var noteCol) ? NullIfEmpty(CellText(row, noteCol)) : null));
        }

        return rows;
    }

    private static int FindHeaderRow(DataTable table)
    {
        var limit = Math.Min(table.Rows.Count, 20);
        for (var r = 0; r < limit; r++)
        {
            var values = table.Rows[r].ItemArray.Select(x => NormalizeHeader(x?.ToString() ?? "")).ToList();
            if (values.Any(IsAwbHeader) &&
                values.Any(v => v.Contains("TL CAN") || v.Contains("CAN") || v.Contains("ACTUAL")))
            {
                return r;
            }
        }

        return table.Rows.Count > 0 ? 0 : -1;
    }

    private static Dictionary<string, int> MapColumns(DataTable table, int headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var row = table.Rows[headerRow];
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var header = NormalizeHeader(row[c]?.ToString() ?? "");
            if (header.Length == 0) continue;
            if (IsAwbHeader(header)) map["awb"] = c;
            else if (header.Contains("TL CAN") || header == "KG THUC" || header.Contains("ACTUAL WEIGHT")) map["weight"] = c;
            else if (header == "DAI" || header.Contains("LENGTH")) map["length"] = c;
            else if (header == "RONG" || header.Contains("WIDTH")) map["width"] = c;
            else if (header == "CAO" || header.Contains("HEIGHT")) map["height"] = c;
            else if (header.Contains("SO PHIEU") || header.Contains("TICKET")) map["ticket"] = c;
            else if (header.Contains("NGAY CAN")) map["weighedAt"] = c;
            else if (header.Contains("NHAN VIEN CAN")) map["weighedBy"] = c;
            else if (header.Contains("GHI CHU CAN")) map["note"] = c;
        }

        return map;
    }

    /// <summary>
    /// File cân kiểu 123.xls: STT | SỐ PHIẾU | SỐ VẬN ĐƠN | ... | TL CÂN | TL THỂ TÍCH | DÀI | RỘNG | CAO
    /// </summary>
    private static void ApplyKnownLayoutFallback(DataTable table, int headerRow, Dictionary<string, int> map)
    {
        if (table.Columns.Count < 12) return;
        var first = NormalizeHeader(table.Rows[headerRow][0]?.ToString() ?? "");
        if (first != "STT" && first != "TT" && !first.Contains("STT")) return;
        map.TryAdd("awb", 2);
        map.TryAdd("weight", 7);
        map.TryAdd("length", 9);
        map.TryAdd("width", 10);
        map.TryAdd("height", 11);
        map.TryAdd("ticket", 1);
    }

    private static bool IsAwbHeader(string header) =>
        header.Contains("VAN DON") ||
        header.Contains("SO VAN") ||
        header == "AWB" ||
        header.Contains("TRACKING");

    private static string NormalizeHeader(string value)
    {
        var form = value.Trim().Normalize(NormalizationForm.FormD);
        var chars = form.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray())
            .ToUpperInvariant()
            .Replace('Đ', 'D')
            .Replace('đ', 'D')
            .Replace(".", "")
            .Replace(",", "")
            .Replace("-", " ")
            .Replace("(", " ")
            .Replace(")", " ")
            .Replace("  ", " ")
            .Trim();
    }

    private static string NormalizeAwb(string value)
    {
        var raw = value.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");
        if (raw.EndsWith(".0", StringComparison.Ordinal)) raw = raw[..^2];
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            number == Math.Truncate(number) &&
            number > 0)
        {
            return Math.Truncate(number).ToString("0", CultureInfo.InvariantCulture);
        }

        return raw;
    }

    private static string CellText(DataRow row, int col)
    {
        if (col < 0 || col >= row.ItemArray.Length) return "";
        var value = row[col];
        if (value is null or DBNull) return "";
        if (value is double d)
        {
            return d == Math.Truncate(d)
                ? d.ToString("0", CultureInfo.InvariantCulture)
                : d.ToString("G", CultureInfo.InvariantCulture);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
    }

    private static decimal CellDecimal(DataRow row, int col)
    {
        var text = CellText(row, col).Replace(",", ".");
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static DateTime? CellDate(DataRow row, int col)
    {
        if (col < 0 || col >= row.ItemArray.Length) return null;
        var value = row[col];
        if (value is DateTime dt) return dt;
        if (value is double oa)
        {
            try { return DateTime.FromOADate(oa); }
            catch { return null; }
        }

        return DateTime.TryParse(CellText(row, col), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ExcelScaleRow(
        string Awb,
        int ExcelRow,
        decimal Weight,
        decimal Length,
        decimal Width,
        decimal Height,
        string? TicketNo,
        DateTime? WeighedAt,
        string? WeighedBy,
        string? Note);
}
