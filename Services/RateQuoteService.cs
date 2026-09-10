using System.Globalization;
using System.Text;
using System.Text.Json;
using CourierApi.Contracts;
using CourierApi.Data;
using CourierApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CourierApi.Services;

public sealed class RateQuoteService(AppDbContext db)
{
    public async Task<BookingRateQuoteDto> QuoteAsync(
        Guid? customerId,
        string direction,
        string service,
        string fromCountry,
        string toCountry,
        DateTime shipDate,
        IReadOnlyList<BookingPackageDto> packages,
        bool residential = false,
        string? carrierCode = null,
        string? zoneCode = null,
        CancellationToken ct = default)
    {
        direction = NormalizeDirection(direction);
        var requestedService = (service ?? "").Trim().ToUpperInvariant();
        var date = shipDate.Date;
        var requestedCarrier = NormalizeRequestedCarrier(carrierCode);
        var autoSelect = requestedCarrier is null;
        var carriers = autoSelect
            ? await db.RateImportBatches.AsNoTracking()
                .Where(x => x.Status == "Committed")
                .Select(x => x.CarrierCode)
                .Distinct()
                .ToListAsync(ct)
            : new List<string> { requestedCarrier! };

        if (carriers.Count == 0)
        {
            throw new InvalidOperationException("Chưa có bảng giá đã import (Rate Hub) để báo giá.");
        }

        var quotes = new List<BookingRateQuoteDto>();
        var errors = new List<string>();
        foreach (var carrier in carriers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var mappedService = MapServiceForCarrier(carrier, requestedService);
            if (string.IsNullOrWhiteSpace(mappedService)) continue;

            try
            {
                var quote = await QuoteCarrierAsync(
                    customerId,
                    direction,
                    mappedService,
                    fromCountry,
                    toCountry,
                    date,
                    packages,
                    residential,
                    carrier,
                    zoneCode,
                    autoSelect,
                    ct);
                quotes.Add(quote);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"{carrier}: {ex.Message}");
            }
        }

        if (quotes.Count == 0)
        {
            var detail = errors.Count == 0
                ? "Không có đơn vị vận chuyển tương thích với dịch vụ đã chọn."
                : string.Join(" ", errors);
            throw new InvalidOperationException(
                "Chưa có bảng giá hiệu lực cho ngày gửi này. " + detail);
        }

        var assigned = quotes.Where(x => x.RateSource == "Customer").ToList();
        var pool = autoSelect && assigned.Count > 0 ? assigned : quotes;
        return pool.OrderBy(x => x.Total).ThenBy(x => x.CarrierCode, StringComparer.OrdinalIgnoreCase).First();
    }

    public async Task<IReadOnlyList<CountryOptionDto>> ListCountriesAsync(
        Guid? customerId,
        string direction,
        string service,
        DateTime shipDate,
        string? carrierCode,
        CancellationToken ct = default)
    {
        direction = NormalizeDirection(direction);
        var requestedService = (service ?? "").Trim().ToUpperInvariant();
        var date = shipDate.Date;
        var requestedCarrier = NormalizeRequestedCarrier(carrierCode);
        var carriers = requestedCarrier is null
            ? await db.RateImportBatches.AsNoTracking()
                .Where(x => x.Status == "Committed")
                .Select(x => x.CarrierCode)
                .Distinct()
                .ToListAsync(ct)
            : new List<string> { requestedCarrier };

        var customerCountries = new Dictionary<string, CountryOptionDto>(StringComparer.OrdinalIgnoreCase);
        var publicCountries = new Dictionary<string, CountryOptionDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var carrier in carriers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var mappedService = MapServiceForCarrier(carrier, requestedService);
            if (string.IsNullOrWhiteSpace(mappedService)) continue;

            var cards = await db.RateCards.AsNoTracking()
                .Include(x => x.ImportBatch)
                .Where(x =>
                    x.ImportBatch.Status == "Committed" &&
                    x.CarrierCode == carrier &&
                    x.Direction == direction &&
                    x.ServiceCode == mappedService &&
                    x.EffectiveFrom <= date &&
                    (x.EffectiveTo == null || x.EffectiveTo >= date) &&
                    (x.ImportBatch.EffectiveFrom == null || x.ImportBatch.EffectiveFrom <= date))
                .Select(x => new { x.ImportBatchId, x.ImportBatch.CustomerId })
                .ToListAsync(ct);

            var customerBatchIds = customerId.HasValue
                ? cards.Where(x => x.CustomerId == customerId).Select(x => x.ImportBatchId).Distinct().ToList()
                : [];
            var usingCustomer = customerBatchIds.Count > 0;
            var batchIds = usingCustomer
                ? customerBatchIds
                : cards.Where(x => x.CustomerId == null).Select(x => x.ImportBatchId).Distinct().ToList();
            if (batchIds.Count == 0) continue;

            var rows = await db.CountryZoneMappings.AsNoTracking()
                .Where(x =>
                    x.Direction == direction &&
                    x.ServiceCode == mappedService &&
                    x.IsSupported &&
                    batchIds.Contains(x.ImportBatchId))
                .Select(x => new { x.CountryName, x.ZoneCode, x.Direction, x.ServiceCode })
                .ToListAsync(ct);

            var target = usingCustomer ? customerCountries : publicCountries;
            foreach (var row in rows)
            {
                var name = (row.CountryName ?? "").Trim();
                if (name.Length == 0) continue;
                target.TryAdd(name, new CountryOptionDto(
                    name,
                    row.ZoneCode,
                    row.Direction,
                    row.ServiceCode));
            }
        }

        var chosen = customerCountries.Count > 0 ? customerCountries : publicCountries;
        return chosen.Values
            .OrderBy(x => x.CountryName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<BookingRateQuoteDto> QuoteCarrierAsync(
        Guid? customerId,
        string direction,
        string service,
        string fromCountry,
        string toCountry,
        DateTime date,
        IReadOnlyList<BookingPackageDto> packages,
        bool residential,
        string carrierCode,
        string? zoneCode,
        bool autoSelect,
        CancellationToken ct)
    {
        var zoneCountry = direction == "IMPORT" ? fromCountry : toCountry;
        if (string.IsNullOrWhiteSpace(zoneCountry)) zoneCountry = toCountry;

        var packageType = packages
            .Select(x => string.IsNullOrWhiteSpace(x.PackageType) ? "Package" : x.PackageType.Trim())
            .FirstOrDefault() ?? "Package";
        var rawChargeable = packages.Sum(RawChargeable);
        packageType = NormalizePackageType(packageType, rawChargeable, service, carrierCode);
        var chargeable = RoundChargeable(rawChargeable, packageType, service);
        if (chargeable <= 0)
        {
            throw new InvalidOperationException("Trọng lượng tính cước phải lớn hơn 0.");
        }

        var cards = await db.RateCards.AsNoTracking()
            .Include(x => x.ImportBatch)
            .Include(x => x.Bands)
            .Where(x =>
                x.ImportBatch.Status == "Committed" &&
                x.CarrierCode == carrierCode &&
                x.Direction == direction &&
                x.ServiceCode == service &&
                x.PackageType == packageType &&
                x.EffectiveFrom <= date &&
                (x.EffectiveTo == null || x.EffectiveTo >= date) &&
                (x.ImportBatch.EffectiveFrom == null || x.ImportBatch.EffectiveFrom <= date))
            .ToListAsync(ct);

        var customerCards = customerId.HasValue
            ? cards.Where(x => x.ImportBatch.CustomerId == customerId).ToList()
            : [];
        var usingCustomerRates = customerCards.Count > 0;
        var candidates = usingCustomerRates
            ? customerCards
            : cards.Where(x => x.ImportBatch.CustomerId == null).ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                usingCustomerRates
                    ? $"Khách đã có bảng {carrierCode} nhưng không còn hiệu lực cho ngày {date:dd/MM/yyyy}."
                    : customerId.HasValue
                        ? $"Khách chưa có bảng {carrierCode} riêng và cũng không có bảng công khai hiệu lực."
                        : $"Không tìm thấy bảng giá công khai {carrierCode} còn hiệu lực.");
        }

        var batchIds = candidates.Select(x => x.ImportBatchId).Distinct().ToHashSet();
        var zones = (await db.CountryZoneMappings.AsNoTracking()
                .Where(x => x.Direction == direction &&
                            x.ServiceCode == service &&
                            x.IsSupported)
                .ToListAsync(ct))
            .Where(x => batchIds.Contains(x.ImportBatchId))
            .ToList();
        var surcharges = (await db.RateSurcharges.AsNoTracking()
                .Where(x => x.Direction == direction &&
                            x.ServiceCode == service &&
                            !x.IsInformational)
                .ToListAsync(ct))
            .Where(x => batchIds.Contains(x.ImportBatchId))
            .ToList();

        BookingRateQuoteDto? best = null;
        foreach (var card in candidates)
        {
            var resolvedZone = string.IsNullOrWhiteSpace(zoneCode)
                ? ResolveZone(zones.Where(z => z.ImportBatchId == card.ImportBatchId), zoneCountry)
                : zoneCode.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(resolvedZone)) continue;

            var band = card.Bands
                .Where(b => string.Equals(b.ZoneCode, resolvedZone, StringComparison.OrdinalIgnoreCase) &&
                            b.WeightFrom <= chargeable &&
                            b.WeightTo >= chargeable)
                .OrderBy(b => b.WeightTo - b.WeightFrom)
                .FirstOrDefault();
            if (band is null) continue;

            var freight = band.RateUnit == "PerKg"
                ? decimal.Round(band.Amount * chargeable, 2, MidpointRounding.AwayFromZero)
                : band.Amount;

            var batchSurcharges = surcharges.Where(s => s.ImportBatchId == card.ImportBatchId).ToList();
            var demandPerKg = batchSurcharges
                .Where(s => s.Code == "DEMAND" &&
                            s.CalculationType == "PerKg" &&
                            (s.ZoneCode == null || string.Equals(s.ZoneCode, resolvedZone, StringComparison.OrdinalIgnoreCase)))
                .Select(s => s.Amount)
                .DefaultIfEmpty(0)
                .Max();
            var demand = decimal.Round(demandPerKg * chargeable, 2, MidpointRounding.AwayFromZero);

            var handling = SumSurcharge(batchSurcharges, resolvedZone, chargeable, "HANDLING", "HDL");
            if (residential)
            {
                handling += SumSurcharge(batchSurcharges, resolvedZone, chargeable, "RESIDENTIAL", "RES", "RSC");
            }

            var taxedBase = freight + demand + handling;
            var withFuel = decimal.Round(taxedBase * card.FuelMultiplier, 2, MidpointRounding.AwayFromZero);
            var total = decimal.Round(withFuel * card.VatMultiplier, 2, MidpointRounding.AwayFromZero);
            var fuel = withFuel - taxedBase;
            var vat = total - withFuel;
            var source = usingCustomerRates ? "Customer" : "Default";

            var quote = new BookingRateQuoteDto(
                resolvedZone,
                chargeable,
                freight,
                fuel,
                demand,
                handling,
                vat,
                total,
                card.Id,
                card.ImportBatchId,
                card.ImportBatch.RateSetCode,
                source,
                card.CarrierCode,
                card.EffectiveFrom,
                JsonSerializer.Serialize(new
                {
                    card.ImportBatch.RateSetCode,
                    source,
                    carrierCode = card.CarrierCode,
                    serviceCode = service,
                    autoSelected = autoSelect,
                    card.ImportBatchId,
                    rateCardId = card.Id,
                    zoneCode = resolvedZone,
                    chargeable,
                    shipDate = date,
                    country = zoneCountry,
                    effectiveFrom = card.EffectiveFrom,
                    effectiveTo = card.EffectiveTo
                }),
                band.RateUnit,
                band.Amount,
                card.FuelMultiplier,
                card.VatMultiplier,
                service);

            if (best is null || quote.Total < best.Total)
            {
                best = quote;
            }
        }

        if (best is null)
        {
            throw new InvalidOperationException(
                $"Có bảng giá {carrierCode} hiệu lực nhưng không map được zone/bậc kg cho quốc gia {zoneCountry.ToUpperInvariant()} / dịch vụ {service}.");
        }

        return best;
    }

    public static string? NormalizeRequestedCarrier(string? carrierCode)
    {
        var value = (carrierCode ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(value) || value is "AUTO" or "BEST" or "ANY") return null;
        return value;
    }

    public static string MapServiceForCarrier(string carrierCode, string requestedService)
    {
        var carrier = carrierCode.Trim().ToUpperInvariant();
        var service = (requestedService ?? "").Trim().ToUpperInvariant();
        if (carrier == "DHL")
        {
            if (service is "IE" or "IEF") return "";
            return DhlRateWorkbookParser.ServiceExpressWorldwide;
        }

        if (service is "EXWW" or "EXPRESS") return "IP";
        return string.IsNullOrWhiteSpace(service) ? "IP" : service;
    }

    public static decimal RawChargeable(BookingPackageDto item)
    {
        var pieces = Math.Max(1, item.Pieces);
        var dim = Math.Round(item.Length * item.Width * item.Height / 5000m * pieces, 3);
        return Math.Max(item.ActualWeight * pieces, dim);
    }

    public static decimal RoundChargeable(decimal weight, string? packageType, string service)
    {
        if (string.Equals(packageType, "Freight", StringComparison.OrdinalIgnoreCase) ||
            service is "IPF" or "IEF")
        {
            return Math.Max(68m, Math.Ceiling(weight));
        }

        if (weight <= 20.5m)
        {
            return Math.Ceiling(weight * 2) / 2m;
        }

        return Math.Ceiling(weight);
    }

    public static string NormalizeDirection(string direction)
    {
        var value = direction.Trim().ToUpperInvariant();
        if (value.StartsWith("IMP")) return "IMPORT";
        return "EXPORT";
    }

    public static string NormalizePackageType(string packageType, decimal weight, string service, string carrierCode)
    {
        if (carrierCode == "DHL")
        {
            return packageType.Trim().ToLowerInvariant() switch
            {
                "document" or "doc" or "docs" => "Document",
                "freight" => "Freight",
                _ => "Package",
            };
        }

        if (service is "IPF" or "IEF") return "Freight";

        var normalized = packageType.Trim().ToLowerInvariant();
        if (normalized == "envelope" && weight > 0.5m) normalized = "pak";
        if (normalized == "pak" && weight > 2.5m) normalized = "package";
        if (weight > 20.5m) normalized = "package";
        return normalized switch
        {
            "envelope" => "Envelope",
            "pak" => "Pak",
            "freight" => "Freight",
            _ => "Package"
        };
    }

    private static decimal SumSurcharge(
        IReadOnlyList<RateSurcharge> items,
        string zoneCode,
        decimal chargeable,
        params string[] codes)
    {
        decimal total = 0;
        foreach (var item in items)
        {
            if (!codes.Contains(item.Code, StringComparer.OrdinalIgnoreCase)) continue;
            if (item.ZoneCode != null &&
                !string.Equals(item.ZoneCode, zoneCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            total += item.CalculationType == "PerKg"
                ? decimal.Round(item.Amount * chargeable, 2, MidpointRounding.AwayFromZero)
                : item.Amount;
        }

        return total;
    }

    private static string? ResolveZone(IEnumerable<CountryZoneMapping> mappings, string country)
    {
        var aliases = CountryAliases(country);
        foreach (var mapping in mappings)
        {
            var name = NormalizeName(mapping.CountryName);
            if (aliases.Contains(name)) return mapping.ZoneCode.Trim().ToUpperInvariant();
        }

        return null;
    }

    private static HashSet<string> CountryAliases(string country)
    {
        var code = country.Trim().ToUpperInvariant();
        var names = new HashSet<string>(StringComparer.Ordinal) { NormalizeName(country), NormalizeName(code) };
        if (IsoAliases.TryGetValue(code, out var extra))
        {
            foreach (var item in extra) names.Add(NormalizeName(item));
        }

        return names;
    }

    private static string NormalizeName(string value)
    {
        var form = value.Trim().Normalize(NormalizationForm.FormD);
        var chars = form.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).ToUpperInvariant()
            .Replace(".", "")
            .Replace(",", "")
            .Replace("-", " ")
            .Replace("'", "")
            .Replace("  ", " ")
            .Trim();
    }

    private static readonly Dictionary<string, string[]> IsoAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = ["UNITED STATES", "USA", "UNITED STATES OF AMERICA", "AMERICA"],
        ["VN"] = ["VIET NAM", "VIETNAM", "VIET NAM SOCIALIST REPUBLIC"],
        ["AU"] = ["AUSTRALIA"],
        ["SG"] = ["SINGAPORE"],
        ["JP"] = ["JAPAN"],
        ["KR"] = ["KOREA", "SOUTH KOREA", "KOREA REPUBLIC OF", "REPUBLIC OF KOREA"],
        ["GB"] = ["UNITED KINGDOM", "UK", "GREAT BRITAIN", "ENGLAND"],
        ["DE"] = ["GERMANY"],
        ["CA"] = ["CANADA"],
        ["TH"] = ["THAILAND"],
        ["MY"] = ["MALAYSIA"],
        ["CN"] = ["CHINA", "CHINA PEOPLES REPUBLIC", "PRC"],
        ["HK"] = ["HONG KONG"],
        ["TW"] = ["TAIWAN"],
        ["FR"] = ["FRANCE"],
        ["NL"] = ["NETHERLANDS", "HOLLAND"],
        ["IT"] = ["ITALY"],
        ["ES"] = ["SPAIN"],
        ["NZ"] = ["NEW ZEALAND"],
        ["PH"] = ["PHILIPPINES"],
        ["ID"] = ["INDONESIA"],
        ["IN"] = ["INDIA"],
    };
}
