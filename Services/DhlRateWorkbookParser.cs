using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using CourierApi.Contracts;

namespace CourierApi.Services;

/// <summary>
/// Parser bảng giá DHL Express (mẫu kiểu DHL2AB_NET + ZONE DHL VN).
/// ServiceCode lưu: EXWW (Express Worldwide).
/// PackageType: Document (tài liệu ≤2kg) / Package (hàng hóa + tài liệu lớn) / Freight (PerKg ≥30.1).
/// ZoneCode: "1"…"10".
/// </summary>
public sealed partial class DhlRateWorkbookParser : IRateWorkbookParser
{
    public const string ServiceExpressWorldwide = "EXWW";

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
            CarrierCode = string.IsNullOrWhiteSpace(carrierCode)
                ? "DHL"
                : carrierCode.Trim().ToUpperInvariant(),
        };

        try
        {
            using var workbookStream = new MemoryStream(bytes);
            using var workbook = new XLWorkbook(workbookStream);

            var cardMap = new Dictionary<CardKey, ParsedRateCard>();
            var netSheets = workbook.Worksheets.Where(IsNetRateSheet).ToList();

            foreach (var worksheet in netSheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParseNetRateSheet(
                    worksheet,
                    result.CarrierCode,
                    fallbackEffectiveFrom,
                    result,
                    cardMap);
            }

            result.RateCards.AddRange(cardMap.Values
                .OrderBy(x => x.EffectiveFrom)
                .ThenBy(x => x.Direction)
                .ThenBy(x => x.ServiceCode)
                .ThenBy(x => x.PackageType));

            ParseZoneSheets(workbook, result);
            ParseFuelVatDemandSheets(workbook, result, cardMap);
            ParsePublishedSurchargesSheet(workbook, result);
            ParsePromoExtraFees(workbook, result);

            if (result.RateCards.Count == 0)
            {
                result.Issues.Add(new RateImportIssueDto(
                    "Error",
                    "NO_RATE_DATA",
                    "Không tìm thấy sheet bảng giá DHL NET hợp lệ (tên sheet cần chứa NET).",
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
        if (name.StartsWith("ZONE") || name.Contains("SURCHARGE") || name.Contains("CONDITION"))
        {
            return false;
        }

        // Sheet khuyến mãi tuyến / FSC: xử lý riêng, không coi là NET.
        if (name.Contains("TUYEN") || name.Contains("KM ") || name.Contains("FSC") ||
            (name.Contains("INC") && name.Contains("VAT")))
        {
            return false;
        }

        return name.Contains("NET");
    }

    private static void ParseNetRateSheet(
        IXLWorksheet worksheet,
        string carrierCode,
        DateTime? fallbackEffectiveFrom,
        ParsedRateWorkbook result,
        IDictionary<CardKey, ParsedRateCard> cardMap)
    {
        var direction = DetectDirection(worksheet);
        var effectiveFrom = DetectEffectiveDate(worksheet, fallbackEffectiveFrom);

        if (direction is null)
        {
            result.Issues.Add(new RateImportIssueDto(
                "Warning",
                "SHEET_SKIPPED",
                "Không xác định được chiều EXPORT/IMPORT trên sheet DHL, sheet đã bỏ qua.",
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
                "Không tìm thấy ngày áp dụng trong sheet DHL. Hãy nhập ngày hiệu lực khi Preview.",
                worksheet.Name,
                null,
                null));
            return;
        }

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        string? currentPackageType = null;
        var currentRateUnit = "Fixed";
        var zonesByColumn = new Dictionary<int, string>();
        var weightToColumn = 0;
        var beforeCount = cardMap.Values.Sum(x => x.Bands.Count);
        var sheetCardKeys = new HashSet<CardKey>();

        for (var row = 1; row <= lastRow; row++)
        {
            var firstText = worksheet.Cell(row, 1).GetString().Trim();
            var normalized = Normalize(firstText);

            if (normalized.StartsWith("GHI CHU") ||
                normalized.StartsWith("PHU THU") ||
                normalized.StartsWith("LUU Y") ||
                normalized.StartsWith("NOTE"))
            {
                break;
            }

            if (IsDocumentSection(normalized))
            {
                currentPackageType = "Document";
                currentRateUnit = "Fixed";
                weightToColumn = 0;
                continue;
            }

            if (IsPackageSection(normalized))
            {
                currentPackageType = "Package";
                currentRateUnit = "Fixed";
                weightToColumn = 0;
                continue;
            }

            if (IsPerKgSection(normalized))
            {
                currentPackageType = "Freight";
                currentRateUnit = "PerKg";
                weightToColumn = 0;
                continue;
            }

            if (normalized is "KG" or "KGS" || IsZoneHeaderRow(worksheet, row, lastColumn))
            {
                zonesByColumn = ReadZoneHeaders(worksheet, row, lastColumn);
                // Hàng "Từ | Đến | Vùng…" → cột B là weightTo.
                var second = Normalize(worksheet.Cell(row, 2).GetString());
                weightToColumn = second is "DEN" or "TO" or "DEN KG" ? 2 : 0;
                continue;
            }

            if (normalized.StartsWith("NHU CAU") || normalized.StartsWith("DEMAND"))
            {
                continue;
            }

            if (currentPackageType is null || zonesByColumn.Count == 0)
            {
                continue;
            }

            if (!TryParseWeight(firstText, out var weightFrom, out var weightToSingle))
            {
                continue;
            }

            var weightTo = weightToSingle;
            if (weightToColumn > 0 &&
                TryReadDecimal(worksheet.Cell(row, weightToColumn), out var explicitTo) &&
                explicitTo > 0)
            {
                weightTo = explicitTo;
            }

            var card = GetOrCreateCard(
                cardMap,
                sheetCardKeys,
                carrierCode,
                direction,
                ServiceExpressWorldwide,
                currentPackageType,
                effectiveFrom.Value,
                worksheet.Name);

            foreach (var (column, zoneCode) in zonesByColumn)
            {
                if (!TryReadDecimal(worksheet.Cell(row, column), out var amount) || amount <= 0m)
                {
                    continue;
                }

                amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
                if (card.Bands.Any(x =>
                        x.ZoneCode == zoneCode &&
                        x.WeightFrom == weightFrom &&
                        x.WeightTo == weightTo))
                {
                    result.Issues.Add(new RateImportIssueDto(
                        "Error",
                        "DUPLICATE_RATE",
                        $"Trùng bậc giá Zone {zoneCode} {weightFrom}-{weightTo} kg.",
                        worksheet.Name,
                        row,
                        CellAddress(worksheet.Cell(row, column))));
                    continue;
                }

                card.Bands.Add(new ParsedRateBand(
                    zoneCode,
                    weightFrom,
                    weightTo,
                    currentRateUnit,
                    amount,
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
            ServiceExpressWorldwide,
            effectiveFrom.Value.Date,
            sheetCardKeys.Count,
            afterCount - beforeCount,
            packageTypes));
    }

    private static void ParseZoneSheets(XLWorkbook workbook, ParsedRateWorkbook result)
    {
        var direction = result.RateCards.Select(x => x.Direction).FirstOrDefault() ?? "EXPORT";
        var supportedZones = result.RateCards
            .SelectMany(x => x.Bands)
            .Select(x => x.ZoneCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var worksheet in workbook.Worksheets.Where(x => Normalize(x.Name).StartsWith("ZONE")))
        {
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

            for (var row = 1; row <= lastRow; row++)
            {
                for (var col = 1; col <= lastColumn - 1; col += 2)
                {
                    var country = worksheet.Cell(row, col).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(country)) continue;
                    if (Normalize(country).Contains("COUNTRIES")) continue;
                    if (Normalize(country).StartsWith("TIME DEFINITE")) continue;
                    if (Normalize(country).StartsWith("DHL ")) continue;
                    if (Normalize(country).StartsWith("CUSTOMER")) continue;

                    if (!TryReadZoneCode(worksheet.Cell(row, col + 1), out var zoneCode))
                    {
                        continue;
                    }

                    var isSupported = supportedZones.Contains(zoneCode);
                    if (!isSupported && warned.Add($"{ServiceExpressWorldwide}:{zoneCode}"))
                    {
                        result.Issues.Add(new RateImportIssueDto(
                            "Warning",
                            "ZONE_WITHOUT_RATE",
                            $"Zone {zoneCode} có trong sheet ZONE nhưng không có cột giá NET tương ứng.",
                            worksheet.Name,
                            row,
                            CellAddress(worksheet.Cell(row, col + 1))));
                    }

                    result.CountryZones.Add(new ParsedCountryZone(
                        result.CarrierCode,
                        direction,
                        country,
                        ServiceExpressWorldwide,
                        zoneCode,
                        isSupported,
                        worksheet.Name,
                        row));
                }
            }
        }

        if (result.CountryZones.Count == 0)
        {
            result.Issues.Add(new RateImportIssueDto(
                "Warning",
                "ZONE_SHEET_NOT_FOUND",
                "Không tìm thấy dữ liệu Country/Zone DHL. Có thể vẫn import giá nhưng không tự map quốc gia.",
                null,
                null,
                null));
        }
    }

    /// <summary>
    /// Sheet TK_RATE_* FSC+DEMAND+VAT: lấy hệ số PPXD/FSC, VAT và DEMAND theo zone (nếu có).
    /// ALLIN ≈ NET × PPXD × VAT (+ DEMAND).
    /// </summary>
    private static void ParseFuelVatDemandSheets(
        XLWorkbook workbook,
        ParsedRateWorkbook result,
        IDictionary<CardKey, ParsedRateCard> cardMap)
    {
        if (cardMap.Count == 0) return;

        foreach (var worksheet in workbook.Worksheets.Where(IsFuelVatSheet))
        {
            var direction = DetectDirection(worksheet)
                ?? result.RateCards.Select(x => x.Direction).FirstOrDefault()
                ?? "EXPORT";
            var effectiveFrom = result.RateCards.Max(x => x.EffectiveFrom);
            var cards = cardMap.Values
                .Where(x => x.Direction == direction && x.ServiceCode == ServiceExpressWorldwide)
                .ToList();
            if (cards.Count == 0) cards = cardMap.Values.ToList();

            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

            decimal? fuel = null;
            decimal? vat = null;
            var fuelCell = "";
            var vatCell = "";

            for (var row = 1; row <= Math.Min(lastRow, 30); row++)
            {
                for (var col = 1; col <= lastColumn; col++)
                {
                    var label = Normalize(worksheet.Cell(row, col).GetString());
                    if (label is "PPXD" or "FSC" or "FUEL" or "FUEL SURCHARGE")
                    {
                        if (TryReadMultiplierNear(worksheet, row, col, out var value))
                        {
                            fuel = value;
                            fuelCell = CellAddress(worksheet.Cell(row, col));
                        }
                    }
                    else if (label is "VAT" or "GTGT")
                    {
                        if (TryReadMultiplierNear(worksheet, row, col, out var value))
                        {
                            vat = value;
                            vatCell = CellAddress(worksheet.Cell(row, col));
                        }
                    }
                }
            }

            if (fuel is > 0m)
            {
                foreach (var card in cards) card.FuelMultiplier = fuel.Value;
                result.Surcharges.Add(new ParsedSurcharge(
                    result.CarrierCode,
                    direction,
                    ServiceExpressWorldwide,
                    null,
                    "FUEL_MULTIPLIER",
                    "Hệ số phụ phí nhiên liệu / PPXD từ sheet FSC",
                    "Multiplier",
                    0m,
                    fuel.Value,
                    effectiveFrom,
                    false,
                    worksheet.Name,
                    fuelCell));
            }

            if (vat is > 0m)
            {
                foreach (var card in cards) card.VatMultiplier = vat.Value;
                result.Surcharges.Add(new ParsedSurcharge(
                    result.CarrierCode,
                    direction,
                    ServiceExpressWorldwide,
                    null,
                    "VAT_MULTIPLIER",
                    "Hệ số VAT từ sheet FSC",
                    "Multiplier",
                    0m,
                    vat.Value,
                    effectiveFrom,
                    false,
                    worksheet.Name,
                    vatCell));
            }

            for (var row = 1; row <= Math.Min(lastRow, 40); row++)
            {
                var first = Normalize(worksheet.Cell(row, 1).GetString());
                if (first is not ("NHU CAU" or "DEMAND")) continue;

                var zoneHeaderRow = 0;
                for (var up = row - 1; up >= Math.Max(1, row - 3); up--)
                {
                    if (ReadZoneHeaders(worksheet, up, lastColumn).Count > 0)
                    {
                        zoneHeaderRow = up;
                        break;
                    }
                }

                if (zoneHeaderRow == 0) break;
                var zones = ReadZoneHeaders(worksheet, zoneHeaderRow, lastColumn);
                foreach (var (column, zoneCode) in zones)
                {
                    if (!TryReadDecimal(worksheet.Cell(row, column), out var amount) || amount <= 0m)
                    {
                        continue;
                    }

                    result.Surcharges.Add(new ParsedSurcharge(
                        result.CarrierCode,
                        direction,
                        ServiceExpressWorldwide,
                        zoneCode,
                        "DEMAND",
                        "Phụ phí nhu cầu theo kg (DHL)",
                        "PerKg",
                        decimal.Round(amount, 2),
                        null,
                        effectiveFrom,
                        false,
                        worksheet.Name,
                        CellAddress(worksheet.Cell(row, column))));
                }

                break;
            }

            if (fuel is null && vat is null)
            {
                result.Issues.Add(new RateImportIssueDto(
                    "Warning",
                    "FSC_PARAMS_NOT_FOUND",
                    "Tìm thấy sheet FSC/ALLIN nhưng không đọc được PPXD/VAT. Tra giá sẽ dùng hệ số 1.",
                    worksheet.Name,
                    null,
                    null));
            }
        }
    }

    private static bool IsFuelVatSheet(IXLWorksheet worksheet)
    {
        var name = Normalize(worksheet.Name);
        return name.Contains("FSC") ||
               name.Contains("ALLIN") ||
               (name.Contains("INC") && (name.Contains("VAT") || name.Contains("DEMAND")));
    }

    private static bool TryReadMultiplierNear(IXLWorksheet worksheet, int labelRow, int labelCol, out decimal value)
    {
        foreach (var (r, c) in new[]
                 {
                     (labelRow + 1, labelCol),
                     (labelRow, labelCol + 1),
                     (labelRow + 1, labelCol + 1),
                 })
        {
            if (TryReadDecimal(worksheet.Cell(r, c), out value) && value > 0m && value < 10m)
            {
                return true;
            }
        }

        value = 0m;
        return false;
    }

    private static void ParsePublishedSurchargesSheet(XLWorkbook workbook, ParsedRateWorkbook result)
    {
        var worksheet = workbook.Worksheets.FirstOrDefault(x => Normalize(x.Name).Contains("SURCHARGE"));
        if (worksheet is null) return;

        var direction = result.RateCards.Select(x => x.Direction).FirstOrDefault() ?? "EXPORT";
        var effectiveFrom = result.RateCards.Count == 0
            ? DateTime.Today
            : result.RateCards.Max(x => x.EffectiveFrom);

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var row = 1; row <= lastRow; row++)
        {
            var name = worksheet.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var nameNorm = Normalize(name);
            if (nameNorm is "NAME" or "SERVICES" or "CUSTOMS SERVICES" or "SURCHARGES" ||
                nameNorm.StartsWith("PUBLISHED") ||
                nameNorm.StartsWith("ADDITIONAL CHARGES") ||
                nameNorm.StartsWith("DHL EXPRESS"))
            {
                continue;
            }

            var mechanism = worksheet.Cell(row, 5).GetString().Trim();
            var chargeText = worksheet.Cell(row, 6).GetString().Trim();
            if (string.IsNullOrWhiteSpace(mechanism) && string.IsNullOrWhiteSpace(chargeText))
            {
                continue;
            }

            var description = worksheet.Cell(row, 2).GetString().Trim();
            var code = ToSurchargeCode(name);
            var displayName = description.Length == 0
                ? name
                : $"{name}: {(description.Length > 100 ? description[..100] + "…" : description)}";
            if (displayName.Length > 160) displayName = displayName[..160];

            ParseChargeText(
                chargeText,
                mechanism,
                out var calcType,
                out var amount,
                out var multiplier,
                out var informational);

            result.Surcharges.Add(new ParsedSurcharge(
                result.CarrierCode,
                direction,
                ServiceExpressWorldwide,
                null,
                code,
                displayName,
                calcType,
                amount,
                multiplier,
                effectiveFrom,
                informational,
                worksheet.Name,
                CellAddress(worksheet.Cell(row, 6))));
        }
    }

    private static void ParsePromoExtraFees(XLWorkbook workbook, ParsedRateWorkbook result)
    {
        var worksheet = workbook.Worksheets.FirstOrDefault(x =>
        {
            var n = Normalize(x.Name);
            return n.Contains("TUYEN") || (n.Contains("KM") && n.Contains("DHL"));
        });
        if (worksheet is null) return;

        var direction = result.RateCards.Select(x => x.Direction).FirstOrDefault() ?? "EXPORT";
        var effectiveFrom = result.RateCards.Count == 0
            ? DateTime.Today
            : result.RateCards.Max(x => x.EffectiveFrom);

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var inFees = false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var row = 1; row <= lastRow; row++)
        {
            for (var col = 1; col <= Math.Min(lastColumn, 6); col++)
            {
                var text = worksheet.Cell(row, col).GetString().Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;
                var norm = Normalize(text);

                if (norm.StartsWith("PHU THU"))
                {
                    inFees = true;
                    continue;
                }

                if (!inFees) continue;
                if (norm.StartsWith("GHI CHU") || norm.StartsWith("LUU Y")) continue;

                var money = Regex.Match(
                    text,
                    @"(\d{1,3}(?:[.,]\d{3})+|\d+)(?:\s*[.,]\d+)?\s*(?:vnd|đ)?",
                    RegexOptions.IgnoreCase);
                if (!money.Success) continue;

                var raw = money.Groups[1].Value.Replace(".", string.Empty).Replace(",", string.Empty);
                if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) ||
                    amount <= 0m)
                {
                    continue;
                }

                var perKg = norm.Contains("/ KG") || norm.Contains("VND/KG") || norm.Contains("VND / KG");
                var codeBase = ToSurchargeCode(text.Length > 35 ? text[..35] : text);
                var code = $"PROMO_{codeBase}";
                if (code.Length > 40) code = code[..40];
                if (!seen.Add(code)) continue;

                var title = text.Length > 160 ? text[..160] : text;
                result.Surcharges.Add(new ParsedSurcharge(
                    result.CarrierCode,
                    direction,
                    ServiceExpressWorldwide,
                    null,
                    code,
                    title,
                    perKg ? "PerKg" : "Fixed",
                    decimal.Round(amount, 2),
                    null,
                    effectiveFrom,
                    true,
                    worksheet.Name,
                    CellAddress(worksheet.Cell(row, col))));
            }
        }

        for (var row = 1; row <= lastRow; row++)
        {
            for (var col = 1; col <= Math.Min(lastColumn, 6); col++)
            {
                var text = worksheet.Cell(row, col).GetString();
                var norm = Normalize(text);
                if (!norm.Contains("GGP")) continue;
                var match = Regex.Match(text, @"GGP[^\d]*(\d{1,3}(?:[.,]\d{3})+|\d+)", RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                var raw = match.Groups[1].Value.Replace(".", string.Empty).Replace(",", string.Empty);
                if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                {
                    continue;
                }

                result.Surcharges.Add(new ParsedSurcharge(
                    result.CarrierCode,
                    direction,
                    ServiceExpressWorldwide,
                    null,
                    "GGP",
                    "Phí GGP (từ ghi chú sheet tuyến KM)",
                    "PerKg",
                    amount,
                    null,
                    effectiveFrom,
                    true,
                    worksheet.Name,
                    CellAddress(worksheet.Cell(row, col))));
                return;
            }
        }
    }

    private static void ParseChargeText(
        string chargeText,
        string mechanism,
        out string calcType,
        out decimal amount,
        out decimal? multiplier,
        out bool informational)
    {
        calcType = "Fixed";
        amount = 0m;
        multiplier = null;
        informational = false;

        var mech = Normalize(mechanism);
        var text = chargeText.Trim();
        var textNorm = Normalize(text);

        if (string.IsNullOrWhiteSpace(text) ||
            textNorm.Contains("VARIABLE") ||
            textNorm.Contains("REFER TO") ||
            textNorm.Contains("PLS REFER") ||
            textNorm.Contains("WEBSITE"))
        {
            calcType = "RawParameter";
            informational = true;
            return;
        }

        var percent = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*%");
        if (percent.Success || mech.Contains("%"))
        {
            calcType = "Multiplier";
            informational = true;
            if (percent.Success &&
                decimal.TryParse(percent.Groups[1].Value.Replace(',', '.'), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var pct))
            {
                multiplier = pct / 100m;
            }

            var minMatch = Regex.Match(text, @"minimum of\s+(\d{1,3}(?:,\d{3})+|\d+)", RegexOptions.IgnoreCase);
            if (minMatch.Success)
            {
                var raw = minMatch.Groups[1].Value.Replace(",", string.Empty);
                decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out amount);
            }

            return;
        }

        var money = Regex.Match(text, @"(\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+)(?:\s*VND)?", RegexOptions.IgnoreCase);
        if (money.Success)
        {
            var raw = money.Groups[1].Value.Replace(",", string.Empty);
            decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out amount);
        }

        if (mech.Contains("PER KG") || textNorm.Contains("PER KG") || textNorm.Contains("/ KG"))
        {
            calcType = "PerKg";
        }
        else if (mech.Contains("PER PIECE") || mech.Contains("PER LINE") || mech.Contains("PER STACK"))
        {
            calcType = "Fixed";
            informational = true;
        }
        else
        {
            calcType = "Fixed";
        }

        if (amount <= 0m)
        {
            informational = true;
            calcType = "RawParameter";
        }
    }

    private static string ToSurchargeCode(string name)
    {
        var normalized = Normalize(name);
        var chars = normalized
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        var code = new string(chars);
        while (code.Contains("__", StringComparison.Ordinal))
        {
            code = code.Replace("__", "_", StringComparison.Ordinal);
        }

        code = code.Trim('_');
        if (code.Length == 0) code = "SURCHARGE";
        if (code.Length > 40) code = code[..40];
        return code;
    }

    private static ParsedRateCard GetOrCreateCard(
        IDictionary<CardKey, ParsedRateCard> cardMap,
        HashSet<CardKey> sheetCardKeys,
        string carrierCode,
        string direction,
        string serviceCode,
        string packageType,
        DateTime effectiveFrom,
        string sourceSheet)
    {
        var key = new CardKey(direction, serviceCode, packageType, effectiveFrom.Date, sourceSheet);
        sheetCardKeys.Add(key);
        if (cardMap.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var card = new ParsedRateCard
        {
            CarrierCode = carrierCode,
            Direction = direction,
            ServiceCode = serviceCode,
            PackageType = packageType,
            Currency = "VND",
            EffectiveFrom = effectiveFrom.Date,
            SourceSheet = sourceSheet,
        };
        cardMap[key] = card;
        return card;
    }

    private static Dictionary<int, string> ReadZoneHeaders(IXLWorksheet worksheet, int row, int lastColumn)
    {
        var map = new Dictionary<int, string>();
        for (var col = 2; col <= lastColumn; col++)
        {
            var text = worksheet.Cell(row, col).GetString().Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!TryParseZoneLabel(text, out var zone)) continue;
            map[col] = zone;
        }

        return map;
    }

    private static bool IsZoneHeaderRow(IXLWorksheet worksheet, int row, int lastColumn)
    {
        for (var col = 2; col <= Math.Min(lastColumn, 14); col++)
        {
            if (TryParseZoneLabel(worksheet.Cell(row, col).GetString(), out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseZoneLabel(string text, out string zoneCode)
    {
        zoneCode = string.Empty;
        var normalized = Normalize(text);
        var match = ZoneLabelRegex().Match(normalized);
        if (!match.Success) return false;
        zoneCode = match.Groups[1].Value;
        return true;
    }

    private static bool TryReadZoneCode(IXLCell cell, out string zoneCode)
    {
        zoneCode = string.Empty;
        if (cell.TryGetValue<double>(out var number) && number is >= 1 and <= 99)
        {
            zoneCode = ((int)number).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        var text = cell.GetString().Trim();
        if (TryParseZoneLabel(text, out zoneCode)) return true;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed is >= 1 and <= 99)
        {
            zoneCode = parsed.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static bool IsDocumentSection(string normalized) =>
        normalized.Contains("TAI LIEU") &&
        (normalized.Contains("DEN 2") || normalized.Contains("2 0 KG") || normalized.Contains("2.0"));

    private static bool IsPackageSection(string normalized) =>
        normalized.Contains("HANG HOA") ||
        (normalized.Contains("TAI LIEU") && normalized.Contains("KHONG PHAI")) ||
        (normalized.Contains("TAI LIEU") && normalized.Contains("2 5"));

    private static bool IsPerKgSection(string normalized) =>
        normalized.Contains("30 1") ||
        (normalized.Contains("BAO GIA") && normalized.Contains("1 KG")) ||
        (normalized.Contains("TREN MAT") && normalized.Contains("KG"));

    private static string? DetectDirection(IXLWorksheet worksheet)
    {
        var lastRow = Math.Min(worksheet.LastRowUsed()?.RowNumber() ?? 0, 12);
        var parts = new List<string> { worksheet.Name };
        for (var row = 1; row <= lastRow; row++)
        {
            parts.Add(worksheet.Cell(row, 1).GetString());
        }

        var text = Normalize(string.Join(' ', parts));
        if (text.Contains("EXPORT") || text.Contains(" EXP ") || text.Contains("_EXP_")) return "EXPORT";
        if (text.Contains("IMPORT") || text.Contains(" IMP ") || text.Contains("_IMP_")) return "IMPORT";
        return null;
    }

    private static DateTime? DetectEffectiveDate(IXLWorksheet worksheet, DateTime? fallback)
    {
        var fromName = TryExtractDate(worksheet.Name);
        if (fromName is not null) return fromName;

        var lastRow = Math.Min(worksheet.LastRowUsed()?.RowNumber() ?? 0, 20);
        for (var row = 1; row <= lastRow; row++)
        {
            var text = worksheet.Cell(row, 1).GetString();
            var parsed = TryExtractDate(text);
            if (parsed is not null) return parsed;
        }

        return fallback?.Date;
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

    [GeneratedRegex(@"\bVUNG\s*(\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex ZoneLabelRegex();

    [GeneratedRegex(@"(\d{1,2})[./-](\d{1,2})[./-](\d{2,4})")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*-\s*(\d+(?:\.\d+)?)$")]
    private static partial Regex WeightRangeRegex();
}
