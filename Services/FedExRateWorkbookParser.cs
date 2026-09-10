using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using CourierApi.Contracts;

namespace CourierApi.Services;

public sealed partial class FedExRateWorkbookParser : IRateWorkbookParser
{
    private static readonly string[] ZoneServices = ["IP", "IE", "IPF", "IEF"];

    public async Task<ParsedRateWorkbook> ParseAsync(
        Stream source,
        string fileName,
        string carrierCode,
        DateTime? fallbackEffectiveFrom,
        CancellationToken cancellationToken)
    {
        await using var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        var result = new ParsedRateWorkbook
        {
            FileName = fileName,
            FileHash = hash,
            CarrierCode = carrierCode.Trim().ToUpperInvariant()
        };

        try
        {
            using var workbookStream = new MemoryStream(bytes);
            using var workbook = new XLWorkbook(workbookStream);

            var cardMap = new Dictionary<CardKey, ParsedRateCard>();
            var rateSheets = workbook.Worksheets
                .Where(IsNetRateSheet)
                .ToList();

            foreach (var worksheet in rateSheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParseNetRateSheet(
                    worksheet,
                    result.CarrierCode,
                    fallbackEffectiveFrom,
                    result,
                    cardMap);
            }

            foreach (var worksheet in workbook.Worksheets.Where(IsAllInSheet))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParseAllInSheet(worksheet, result, cardMap);
            }

            result.RateCards.AddRange(cardMap.Values
                .OrderBy(x => x.EffectiveFrom)
                .ThenBy(x => x.Direction)
                .ThenBy(x => x.ServiceCode)
                .ThenBy(x => x.PackageType));

            ParseZoneSheets(workbook, result);

            if (result.RateCards.Count == 0)
            {
                result.Issues.Add(new RateImportIssueDto(
                    "Error",
                    "NO_RATE_DATA",
                    "Không tìm thấy sheet bảng giá NET hợp lệ.",
                    null,
                    null,
                    null));
            }
        }
        catch (Exception ex)
        {
            result.Issues.Add(new RateImportIssueDto(
                "Error",
                "INVALID_WORKBOOK",
                $"Không đọc được file Excel: {ex.Message}",
                null,
                null,
                null));
        }

        return result;
    }

    private static bool IsNetRateSheet(IXLWorksheet worksheet)
    {
        var name = Normalize(worksheet.Name);
        if (name.Contains("ALLIN") || name.StartsWith("ZONE")) return false;

        var title = Normalize(worksheet.Cell(1, 1).GetString());
        return name.Contains("NET") || title.Contains("FEDEX EXPORT") || title.Contains("FEDEX IMPORT");
    }

    private static bool IsAllInSheet(IXLWorksheet worksheet) =>
        Normalize(worksheet.Name).Contains("ALLIN");

    private static void ParseNetRateSheet(
        IXLWorksheet worksheet,
        string carrierCode,
        DateTime? fallbackEffectiveFrom,
        ParsedRateWorkbook result,
        IDictionary<CardKey, ParsedRateCard> cardMap)
    {
        var direction = DetectDirection(worksheet);
        var baseService = DetectBaseService(worksheet);
        var effectiveFrom = DetectEffectiveDate(worksheet, fallbackEffectiveFrom);

        if (direction is null || baseService is null)
        {
            result.Issues.Add(new RateImportIssueDto(
                "Warning",
                "SHEET_SKIPPED",
                "Không xác định được chiều vận chuyển hoặc dịch vụ, sheet đã được bỏ qua.",
                worksheet.Name,
                null,
                null));
            return;
        }

        if (effectiveFrom is null)
        {
            result.Issues.Add(new RateImportIssueDto(
                "Error",
                "EFFECTIVE_DATE_NOT_FOUND",
                "Không tìm thấy ngày áp dụng trong sheet. Hãy nhập ngày hiệu lực khi Preview.",
                worksheet.Name,
                null,
                null));
            return;
        }

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var currentService = baseService;
        string? currentPackageType = null;
        var currentRateUnit = "Fixed";
        var zonesByColumn = new Dictionary<int, string>();
        var beforeCount = cardMap.Values.Sum(x => x.Bands.Count);
        var sheetCardKeys = new HashSet<CardKey>();

        for (var row = 1; row <= lastRow; row++)
        {
            var firstText = worksheet.Cell(row, 1).GetString().Trim();
            var normalized = Normalize(firstText);

            if (normalized.StartsWith("GHI CHU") ||
                normalized.StartsWith("PHU THU") ||
                normalized.StartsWith("THAM KHAO"))
            {
                break;
            }

            if (normalized == "KGS")
            {
                zonesByColumn = ReadZoneHeaders(worksheet, row, lastColumn);
                continue;
            }

            if (normalized.StartsWith("ENVELOPE"))
            {
                currentPackageType = "Envelope";
                currentRateUnit = "Fixed";
                continue;
            }

            if (normalized.StartsWith("PAK"))
            {
                currentPackageType = "Pak";
                currentRateUnit = "Fixed";
                continue;
            }

            if (normalized == "PACKAGE")
            {
                currentPackageType = "Package";
                currentRateUnit = "Fixed";
                continue;
            }

            if (normalized.Contains("FREIGHT"))
            {
                currentService = normalized.Contains("IEF") ? "IEF" : "IPF";
                currentPackageType = "Freight";
                currentRateUnit = "PerKg";
                continue;
            }

            if (normalized.StartsWith("PER KILOGRAM"))
            {
                currentPackageType ??= "Package";
                currentRateUnit = "PerKg";
                continue;
            }

            if (normalized is "WEIGHT" or "DEMAND" || currentPackageType is null || zonesByColumn.Count == 0)
            {
                continue;
            }

            var hasRateValues = zonesByColumn.Keys.Any(column =>
                TryReadDecimal(worksheet.Cell(row, column), out var amount) && amount > 0m);

            if (!TryParseWeight(firstText, out var weightFrom, out var weightTo))
            {
                // FedEx Envelope trong file Import có ô trọng lượng trống nhưng thực tế là mức 0.5 kg.
                if (currentPackageType == "Envelope" && string.IsNullOrWhiteSpace(firstText) && hasRateValues)
                {
                    weightFrom = 0.5m;
                    weightTo = 0.5m;
                }
                else
                {
                    continue;
                }
            }

            var key = new CardKey(direction, currentService, currentPackageType, effectiveFrom.Value.Date, worksheet.Name);
            if (!cardMap.TryGetValue(key, out var card))
            {
                card = new ParsedRateCard
                {
                    CarrierCode = carrierCode,
                    Direction = direction,
                    ServiceCode = currentService,
                    PackageType = currentPackageType,
                    EffectiveFrom = effectiveFrom.Value.Date,
                    SourceSheet = worksheet.Name
                };
                cardMap.Add(key, card);
            }

            sheetCardKeys.Add(key);

            foreach (var (column, zoneCode) in zonesByColumn)
            {
                if (!TryReadDecimal(worksheet.Cell(row, column), out var amount) || amount <= 0m)
                {
                    continue;
                }

                var duplicate = card.Bands.Any(x =>
                    x.ZoneCode == zoneCode &&
                    x.WeightFrom == weightFrom &&
                    x.WeightTo == weightTo);

                if (duplicate)
                {
                    result.Issues.Add(new RateImportIssueDto(
                        "Error",
                        "DUPLICATE_RATE",
                        $"Trùng giá {currentService}/{currentPackageType}/Zone {zoneCode}/{weightFrom}-{weightTo} kg.",
                        worksheet.Name,
                        row,
                        worksheet.Cell(row, column).Address.ToString()));
                    continue;
                }

                card.Bands.Add(new ParsedRateBand(
                    zoneCode,
                    weightFrom,
                    weightTo,
                    currentRateUnit,
                    decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
                    row));
            }
        }

        var afterCount = cardMap.Values.Sum(x => x.Bands.Count);
        var packageTypes = sheetCardKeys
            .Select(x => x.PackageType)
            .Distinct()
            .Order()
            .ToArray();

        result.SheetSummaries.Add(new RateSheetPreviewDto(
            worksheet.Name,
            direction,
            baseService,
            effectiveFrom.Value.Date,
            sheetCardKeys.Count,
            afterCount - beforeCount,
            packageTypes));
    }

    private static Dictionary<int, string> ReadZoneHeaders(
        IXLWorksheet worksheet,
        int row,
        int lastColumn)
    {
        var zones = new Dictionary<int, string>();
        for (var column = 2; column <= lastColumn; column++)
        {
            var zone = worksheet.Cell(row, column).GetString().Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(zone)) continue;
            if (!ZoneCodeRegex().IsMatch(zone)) continue;
            zones[column] = zone;
        }
        return zones;
    }

    private static void ParseAllInSheet(
        IXLWorksheet worksheet,
        ParsedRateWorkbook result,
        IDictionary<CardKey, ParsedRateCard> cardMap)
    {
        var direction = DetectDirection(worksheet);
        var baseService = DetectBaseService(worksheet);
        if (direction is null || baseService is null) return;

        var matchingCards = cardMap.Values
            .Where(x => x.Direction == direction &&
                        (x.ServiceCode == baseService ||
                         x.ServiceCode == (baseService == "IP" ? "IPF" : "IEF")))
            .ToList();

        if (matchingCards.Count == 0) return;

        // ALLIN hiện tại đi với phiên bản mới nhất của cùng chiều/dịch vụ.
        var effectiveFrom = matchingCards.Max(x => x.EffectiveFrom);
        matchingCards = matchingCards.Where(x => x.EffectiveFrom == effectiveFrom).ToList();

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        var demandRow = Enumerable.Range(1, lastRow)
            .FirstOrDefault(row => Normalize(worksheet.Cell(row, 1).GetString()) == "DEMAND");
        if (demandRow == 0) return;

        var zoneHeaderRow = 0;
        for (var row = demandRow - 1; row >= 1; row--)
        {
            if (Normalize(worksheet.Cell(row, 1).GetString()) == "KGS")
            {
                zoneHeaderRow = row;
                break;
            }
        }

        if (TryReadDecimal(worksheet.Cell(demandRow, 24), out var fuelMultiplier) && fuelMultiplier > 0m)
        {
            foreach (var card in matchingCards) card.FuelMultiplier = fuelMultiplier;
            result.Surcharges.Add(new ParsedSurcharge(
                result.CarrierCode,
                direction,
                baseService,
                null,
                "FUEL_MULTIPLIER",
                "Hệ số phụ phí nhiên liệu từ sheet ALLIN",
                "Multiplier",
                0m,
                fuelMultiplier,
                effectiveFrom,
                false,
                worksheet.Name,
                CellAddress(worksheet.Cell(demandRow, 24))));
        }

        if (TryReadDecimal(worksheet.Cell(demandRow, 25), out var vatMultiplier) && vatMultiplier > 0m)
        {
            foreach (var card in matchingCards) card.VatMultiplier = vatMultiplier;
            result.Surcharges.Add(new ParsedSurcharge(
                result.CarrierCode,
                direction,
                baseService,
                null,
                "VAT_MULTIPLIER",
                "Hệ số VAT từ sheet ALLIN",
                "Multiplier",
                0m,
                vatMultiplier,
                effectiveFrom,
                false,
                worksheet.Name,
                CellAddress(worksheet.Cell(demandRow, 25))));
        }

        if (zoneHeaderRow > 0)
        {
            var zoneHeaders = ReadZoneHeaders(worksheet, zoneHeaderRow, 23);
            foreach (var (column, zoneCode) in zoneHeaders)
            {
                if (!TryReadDecimal(worksheet.Cell(demandRow, column), out var amount) || amount <= 0m)
                {
                    continue;
                }

                foreach (var service in new[] { baseService, baseService == "IP" ? "IPF" : "IEF" })
                {
                    result.Surcharges.Add(new ParsedSurcharge(
                        result.CarrierCode,
                        direction,
                        service,
                        zoneCode,
                        "DEMAND",
                        "Phụ phí nhu cầu theo kg",
                        "PerKg",
                        decimal.Round(amount, 2),
                        null,
                        effectiveFrom,
                        false,
                        worksheet.Name,
                        CellAddress(worksheet.Cell(demandRow, column))));
                }
            }
        }

        // Một số file dùng X7/X8 làm tham số phụ trong công thức ALLIN nhưng nhãn không đồng nhất.
        // Lưu dưới dạng thông tin để người quản trị duyệt, không tự động cộng vào báo giá.
        for (var row = demandRow + 1; row <= Math.Min(demandRow + 3, lastRow); row++)
        {
            if (!TryReadDecimal(worksheet.Cell(row, 24), out var rawAmount) || rawAmount <= 0m) continue;
            var label = worksheet.Cell(row, 1).GetString().Trim();
            result.Surcharges.Add(new ParsedSurcharge(
                result.CarrierCode,
                direction,
                baseService,
                null,
                $"RAW_{worksheet.Cell(row, 24).Address}",
                string.IsNullOrWhiteSpace(label) ? "Tham số ALLIN chưa xác định" : label,
                "RawParameter",
                decimal.Round(rawAmount, 2),
                null,
                effectiveFrom,
                true,
                worksheet.Name,
                CellAddress(worksheet.Cell(row, 24))));
        }
    }

    private static void ParseZoneSheets(XLWorkbook workbook, ParsedRateWorkbook result)
    {
        var direction = result.RateCards.Select(x => x.Direction).FirstOrDefault() ?? "IMPORT";
        var supportedZones = result.RateCards
            .GroupBy(x => x.ServiceCode)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(x => x.Bands).Select(x => x.ZoneCode).ToHashSet());

        var warnedPairs = new HashSet<string>();

        foreach (var worksheet in workbook.Worksheets.Where(x => Normalize(x.Name).StartsWith("ZONE")))
        {
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            for (var row = 2; row <= lastRow; row++)
            {
                var country = worksheet.Cell(row, 1).GetString().Trim();
                if (string.IsNullOrWhiteSpace(country)) continue;

                for (var index = 0; index < ZoneServices.Length; index++)
                {
                    var service = ZoneServices[index];
                    var zoneCode = worksheet.Cell(row, index + 2).GetString().Trim().ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(zoneCode)) continue;

                    var isSupported = supportedZones.TryGetValue(service, out var zones) && zones.Contains(zoneCode);
                    result.CountryZones.Add(new ParsedCountryZone(
                        result.CarrierCode,
                        direction,
                        country,
                        service,
                        zoneCode,
                        isSupported,
                        worksheet.Name,
                        row));

                    if (!isSupported && warnedPairs.Add($"{service}:{zoneCode}"))
                    {
                        result.Issues.Add(new RateImportIssueDto(
                            "Warning",
                            "ZONE_WITHOUT_RATE",
                            $"Zone '{zoneCode}' của dịch vụ {service} có trong bảng quốc gia nhưng không có cột giá; hệ thống sẽ đánh dấu không hỗ trợ.",
                            worksheet.Name,
                            row,
                            worksheet.Cell(row, index + 2).Address.ToString()));
                    }
                }
            }
        }

        if (result.CountryZones.Count == 0)
        {
            result.Issues.Add(new RateImportIssueDto(
                "Warning",
                "ZONE_SHEET_NOT_FOUND",
                "Không tìm thấy dữ liệu Country/Zone. Có thể vẫn import giá nhưng không tự xác định Zone theo quốc gia.",
                null,
                null,
                null));
        }
    }

    private static string? DetectDirection(IXLWorksheet worksheet)
    {
        var text = Normalize($"{worksheet.Name} {worksheet.Cell(1, 1).GetString()} {worksheet.Cell(2, 1).GetString()}");
        if (text.Contains("EXPORT") || text.Contains(" EXP ") || text.Contains("_EXP_")) return "EXPORT";
        if (text.Contains("IMPORT") || text.Contains(" IMP ") || text.Contains("_IMP_")) return "IMPORT";
        return null;
    }

    private static string? DetectBaseService(IXLWorksheet worksheet)
    {
        var text = Normalize($"{worksheet.Name} {worksheet.Cell(1, 1).GetString()} {worksheet.Cell(2, 1).GetString()}");
        if (ServiceIpRegex().IsMatch(text)) return "IP";
        if (ServiceIeRegex().IsMatch(text)) return "IE";
        return null;
    }

    private static DateTime? DetectEffectiveDate(IXLWorksheet worksheet, DateTime? fallback)
    {
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var row = 1; row <= lastRow; row++)
        {
            var text = worksheet.Cell(row, 1).GetString();
            if (!Normalize(text).Contains("AP DUNG TU NGAY")) continue;
            var parsed = TryExtractDate(text);
            if (parsed is not null) return parsed;
        }

        var fromName = TryExtractDate(worksheet.Name);
        return fromName ?? fallback?.Date;
    }

    private static DateTime? TryExtractDate(string text)
    {
        var match = DateRegex().Match(text);
        if (!match.Success) return null;

        var day = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var year = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (year < 100) year += 2000;

        return DateTime.TryParseExact(
            $"{day:00}/{month:00}/{year:0000}",
            "dd/MM/yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static bool TryParseWeight(string text, out decimal from, out decimal to)
    {
        from = 0m;
        to = 0m;
        var normalized = text.Trim().Replace(',', '.');

        var rangeMatch = WeightRangeRegex().Match(normalized);
        if (rangeMatch.Success &&
            decimal.TryParse(rangeMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out from) &&
            decimal.TryParse(rangeMatch.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out to))
        {
            return true;
        }

        if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out from))
        {
            to = from;
            return true;
        }

        return false;
    }

    private static bool TryReadDecimal(IXLCell cell, out decimal value)
    {
        value = 0m;
        if (cell.IsEmpty()) return false;

        if (cell.TryGetValue<double>(out var doubleValue))
        {
            value = (decimal)doubleValue;
            return true;
        }

        var text = cell.GetString().Trim()
            .Replace(" ", string.Empty)
            .Replace(",", string.Empty);
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static string CellAddress(IXLCell cell) => cell.Address.ToString() ?? string.Empty;

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(System.Text.NormalizationForm.FormD);
        var chars = decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();
        return new string(chars)
            .Normalize(System.Text.NormalizationForm.FormC)
            .Replace('-', ' ')
            .Replace('.', ' ')
            .Replace('/', ' ')
            .ToUpperInvariant();
    }

    private readonly record struct CardKey(
        string Direction,
        string ServiceCode,
        string PackageType,
        DateTime EffectiveFrom,
        string SourceSheet);

    [GeneratedRegex("^[A-Z0-9]{1,3}$", RegexOptions.IgnoreCase)]
    private static partial Regex ZoneCodeRegex();

    [GeneratedRegex(@"(?:^|[ _])IP(?:[ _]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ServiceIpRegex();

    [GeneratedRegex(@"(?:^|[ _])IE(?:[ _]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ServiceIeRegex();

    [GeneratedRegex(@"(\d{1,2})[./-](\d{1,2})[./-](\d{2,4})")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*-\s*(\d+(?:\.\d+)?)$")]
    private static partial Regex WeightRangeRegex();
}
