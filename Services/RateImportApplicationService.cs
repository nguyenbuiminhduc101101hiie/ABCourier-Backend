using System.Security.Cryptography;
using System.Text;
using CourierApi.Contracts;
using CourierApi.Data;
using CourierApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CourierApi.Services;

public sealed class RateImportApplicationService(
    AppDbContext db,
    RateWorkbookParserResolver parserResolver,
    RateImportPreviewStore previewStore,
    AuthService auth)
{
    public async Task<RateImportPreviewResponse> PreviewAsync(
        Stream fileStream,
        string fileName,
        string carrierCode,
        DateTime? fallbackEffectiveFrom,
        CancellationToken cancellationToken)
    {
        var parser = parserResolver.Resolve(carrierCode);
        var parsed = await parser.ParseAsync(
            fileStream,
            fileName,
            carrierCode,
            fallbackEffectiveFrom,
            cancellationToken);

        var token = previewStore.Put(parsed);
        return ToPreviewResponse(token, parsed);
    }

    public async Task<RateImportCommitResponse> CommitAsync(
        RateImportCommitRequest request,
        CancellationToken cancellationToken)
    {
        if (!previewStore.TryGet(request.ImportToken, out var parsed))
        {
            throw new KeyNotFoundException("Phiên Preview đã hết hạn hoặc không tồn tại. Hãy Preview lại file.");
        }

        if (parsed.ErrorCount > 0)
        {
            throw new InvalidOperationException("File còn lỗi kiểm tra nên chưa thể Commit.");
        }

        var rateSetCode = request.RateSetCode?.Trim();
        if (string.IsNullOrWhiteSpace(rateSetCode))
        {
            throw new InvalidOperationException("Hãy nhập mã hoặc tên bộ giá trước khi ghi.");
        }

        if (rateSetCode.Length > 80)
        {
            throw new InvalidOperationException("Mã/tên bộ giá không được vượt quá 80 ký tự.");
        }

        var customer = await ResolveCustomerAsync(request.CustomerId, cancellationToken);
        var salesUserId = await auth.ResolveSalesUserIdAsync(request.SalesUserId ?? customer.SalesUserId, cancellationToken);

        var duplicated = await db.RateImportBatches.AnyAsync(
            x => x.CarrierCode == parsed.CarrierCode &&
                 x.FileHash == parsed.FileHash &&
                 x.Status == "Committed" &&
                 x.CustomerId == customer.Id,
            cancellationToken);

        if (duplicated && !request.AllowDuplicateFile)
        {
            throw new InvalidOperationException("File này đã được import cho khách hàng này. Bật AllowDuplicateFile nếu thực sự muốn nhập lại.");
        }

        var codeExists = await db.RateImportBatches.AnyAsync(
            x => x.RateSetCode == rateSetCode && x.CustomerId == customer.Id,
            cancellationToken);
        if (codeExists)
        {
            throw new InvalidOperationException($"Khách hàng “{customer.CompanyName}” đã có bộ giá “{rateSetCode}”. Hãy chọn tên khác hoặc nhân bản.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var batch = new RateImportBatch
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            SalesUserId = salesUserId,
            CarrierCode = parsed.CarrierCode,
            RateSetCode = rateSetCode,
            FileName = parsed.FileName,
            FileHash = parsed.FileHash,
            Status = "Committed",
            EffectiveFrom = parsed.RateCards.Count == 0 ? null : parsed.RateCards.Max(x => x.EffectiveFrom),
            RateCardCount = parsed.RateCards.Count,
            RateBandCount = parsed.RateCards.Sum(x => x.Bands.Count),
            CountryZoneCount = parsed.CountryZones.Count,
            SurchargeCount = parsed.Surcharges.Count,
            WarningCount = parsed.WarningCount,
            ErrorCount = parsed.ErrorCount,
            CreatedAtUtc = now,
            CommittedAtUtc = now
        };

        db.RateImportBatches.Add(batch);

        foreach (var parsedCard in parsed.RateCards)
        {
            var hasNewerActiveVersion = await db.RateCards.AnyAsync(
                x => x.ImportBatch.CustomerId == customer.Id &&
                     x.CarrierCode == parsedCard.CarrierCode &&
                     x.Direction == parsedCard.Direction &&
                     x.ServiceCode == parsedCard.ServiceCode &&
                     x.PackageType == parsedCard.PackageType &&
                     x.IsActive &&
                     x.EffectiveFrom > parsedCard.EffectiveFrom,
                cancellationToken);

            var activateThisCard = request.Activate && !hasNewerActiveVersion;

            if (activateThisCard)
            {
                var oldActiveCards = await db.RateCards
                    .Where(x => x.ImportBatch.CustomerId == customer.Id &&
                                x.CarrierCode == parsedCard.CarrierCode &&
                                x.Direction == parsedCard.Direction &&
                                x.ServiceCode == parsedCard.ServiceCode &&
                                x.PackageType == parsedCard.PackageType &&
                                x.IsActive &&
                                x.EffectiveFrom <= parsedCard.EffectiveFrom)
                    .ToListAsync(cancellationToken);

                foreach (var oldCard in oldActiveCards)
                {
                    oldCard.IsActive = false;
                    oldCard.EffectiveTo = parsedCard.EffectiveFrom.AddDays(-1);
                }
            }

            var card = new RateCard
            {
                Id = Guid.NewGuid(),
                ImportBatchId = batch.Id,
                CarrierCode = parsedCard.CarrierCode,
                Direction = parsedCard.Direction,
                ServiceCode = parsedCard.ServiceCode,
                PackageType = parsedCard.PackageType,
                Currency = parsedCard.Currency,
                EffectiveFrom = parsedCard.EffectiveFrom,
                FuelMultiplier = parsedCard.FuelMultiplier,
                VatMultiplier = parsedCard.VatMultiplier,
                IsActive = activateThisCard,
                SourceSheet = parsedCard.SourceSheet
            };

            foreach (var parsedBand in parsedCard.Bands)
            {
                card.Bands.Add(new RateBand
                {
                    Id = Guid.NewGuid(),
                    ZoneCode = parsedBand.ZoneCode,
                    WeightFrom = parsedBand.WeightFrom,
                    WeightTo = parsedBand.WeightTo,
                    RateUnit = parsedBand.RateUnit,
                    Amount = parsedBand.Amount,
                    SourceRow = parsedBand.SourceRow
                });
            }

            batch.RateCards.Add(card);
        }

        foreach (var mapping in parsed.CountryZones)
        {
            batch.CountryZones.Add(new CountryZoneMapping
            {
                Id = Guid.NewGuid(),
                CarrierCode = mapping.CarrierCode,
                Direction = mapping.Direction,
                CountryName = mapping.CountryName,
                ServiceCode = mapping.ServiceCode,
                ZoneCode = mapping.ZoneCode,
                IsSupported = mapping.IsSupported,
                SourceSheet = mapping.SourceSheet,
                SourceRow = mapping.SourceRow
            });
        }

        foreach (var surcharge in parsed.Surcharges)
        {
            batch.Surcharges.Add(new RateSurcharge
            {
                Id = Guid.NewGuid(),
                CarrierCode = surcharge.CarrierCode,
                Direction = surcharge.Direction,
                ServiceCode = surcharge.ServiceCode,
                ZoneCode = surcharge.ZoneCode,
                Code = surcharge.Code,
                Name = surcharge.Name,
                CalculationType = surcharge.CalculationType,
                Amount = surcharge.Amount,
                Multiplier = surcharge.Multiplier,
                EffectiveFrom = surcharge.EffectiveFrom,
                IsInformational = surcharge.IsInformational,
                SourceSheet = surcharge.SourceSheet,
                SourceCell = surcharge.SourceCell
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        previewStore.Remove(request.ImportToken);

        return new RateImportCommitResponse(
            batch.Id,
            batch.Status,
            batch.RateCardCount,
            batch.RateBandCount,
            batch.CountryZoneCount,
            batch.SurchargeCount,
            now);
    }

    public async Task<IReadOnlyList<RateImportHistoryDto>> GetHistoryAsync(
        string? search,
        CancellationToken cancellationToken,
        Guid? customerId = null,
        Guid? salesUserId = null)
    {
        var query = db.RateImportBatches.AsNoTracking();

        if (customerId.HasValue)
        {
            query = query.Where(x =>
                x.CustomerId == customerId &&
                x.Status == "Committed");
        }

        if (salesUserId.HasValue)
        {
            query = query.Where(x => x.SalesUserId == salesUserId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                (x.RateSetCode != null && x.RateSetCode.Contains(term)) ||
                x.FileName.Contains(term) ||
                (x.Customer != null && (
                    x.Customer.CompanyName.Contains(term) ||
                    x.Customer.CustomerCode.Contains(term))) ||
                (x.SalesUser != null && x.SalesUser.DisplayName.Contains(term)));
        }

        return await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .Select(x => new RateImportHistoryDto(
                x.Id,
                x.CarrierCode,
                x.RateSetCode,
                x.CustomerId,
                x.Customer != null ? x.Customer.CustomerCode : null,
                x.Customer != null ? x.Customer.CompanyName : null,
                x.SalesUserId,
                x.SalesUser != null ? x.SalesUser.DisplayName : "NOMI",
                x.FileName,
                x.Status,
                x.EffectiveFrom,
                x.RateCardCount,
                x.RateBandCount,
                x.CountryZoneCount,
                x.SurchargeCount,
                x.WarningCount,
                x.ErrorCount,
                x.CreatedAtUtc,
                x.CommittedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<RateImportRenameResponse> RenameRateSetCodeAsync(
        Guid batchId,
        string newRateSetCode,
        CancellationToken cancellationToken)
    {
        var code = newRateSetCode.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Hãy nhập mã hoặc tên bộ giá.");
        }

        if (code.Length > 80)
        {
            throw new InvalidOperationException("Mã/tên bộ giá không được vượt quá 80 ký tự.");
        }

        var anchor = await db.RateImportBatches
            .FirstOrDefaultAsync(x => x.Id == batchId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy batch import.");

        List<RateImportBatch> targets;
        if (string.IsNullOrWhiteSpace(anchor.RateSetCode))
        {
            targets = [anchor];
        }
        else
        {
            var oldCode = anchor.RateSetCode;
            targets = await db.RateImportBatches
                .Where(x => x.RateSetCode == oldCode && x.CustomerId == anchor.CustomerId)
                .ToListAsync(cancellationToken);
        }

        if (targets.All(x => string.Equals(x.RateSetCode, code, StringComparison.Ordinal)))
        {
            return await BuildRenameResponseAsync(code, targets, cancellationToken);
        }

        foreach (var batch in targets)
        {
            batch.RateSetCode = code;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await BuildRenameResponseAsync(code, targets, cancellationToken);
    }

    public async Task<IReadOnlyList<RateSetCodeOptionDto>> GetRateSetCodesAsync(
        CancellationToken cancellationToken,
        Guid? salesUserId = null)
    {
        var query = db.RateImportBatches
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.SalesUser)
            .Where(x => x.Status == "Committed" && x.RateSetCode != null && x.RateSetCode != "");
        if (salesUserId.HasValue)
        {
            query = query.Where(x => x.SalesUserId == salesUserId);
        }

        var batches = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return batches
            .GroupBy(x => (x.RateSetCode!, x.CustomerId))
            .Select(group => group.First())
            .OrderBy(x => x.Customer?.CompanyName)
            .ThenBy(x => x.RateSetCode)
            .Select(x => new RateSetCodeOptionDto(
                x.RateSetCode!,
                x.Id,
                x.CarrierCode,
                x.CustomerId,
                x.Customer?.CustomerCode,
                x.Customer?.CompanyName,
                x.SalesUserId,
                x.SalesUser?.DisplayName ?? AuthService.DefaultSalesName,
                x.EffectiveFrom,
                x.RateBandCount,
                x.RateCardCount,
                x.CreatedAtUtc))
            .ToList();
    }

    public async Task<RateImportCloneResponse> CloneRateSetAsync(
        RateImportCloneRequest request,
        CancellationToken cancellationToken)
    {
        var newCode = request.NewRateSetCode.Trim();

        if (request.SourceBatchId == Guid.Empty)
        {
            throw new InvalidOperationException("Hãy chọn bộ giá nguồn.");
        }

        if (string.IsNullOrWhiteSpace(newCode))
        {
            throw new InvalidOperationException("Hãy nhập mã/tên cho bộ giá mới.");
        }

        if (newCode.Length > 80)
        {
            throw new InvalidOperationException("Mã/tên bộ giá mới không được vượt quá 80 ký tự.");
        }

        var customer = await ResolveCustomerAsync(request.CustomerId, cancellationToken);

        if (request.AdjustmentPercent is < -99 or > 500)
        {
            throw new InvalidOperationException("Phần trăm điều chỉnh phải nằm trong khoảng -99 đến 500.");
        }

        var source = await db.RateImportBatches
            .AsSplitQuery()
            .Include(x => x.RateCards).ThenInclude(card => card.Bands)
            .Include(x => x.CountryZones)
            .Include(x => x.Surcharges)
            .FirstOrDefaultAsync(
                x => x.Id == request.SourceBatchId && x.Status == "Committed",
                cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy bộ giá nguồn.");

        var salesUserId = await auth.ResolveSalesUserIdAsync(
            request.SalesUserId ?? source.SalesUserId ?? customer.SalesUserId,
            cancellationToken);

        var sourceCode = source.RateSetCode ?? source.FileName;
        if (source.CustomerId == customer.Id &&
            string.Equals(source.RateSetCode, newCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Khi nhân bản cho cùng khách hàng, bộ giá mới phải có mã/tên khác bộ giá nguồn.");
        }

        var codeExists = await db.RateImportBatches.AnyAsync(
            x => x.RateSetCode == newCode && x.CustomerId == customer.Id,
            cancellationToken);
        if (codeExists)
        {
            throw new InvalidOperationException($"Khách hàng “{customer.CompanyName}” đã có mã/tên “{newCode}”. Hãy chọn tên khác.");
        }

        var factor = 1m + (request.AdjustmentPercent / 100m);
        var now = DateTime.UtcNow;
        var cloneId = Guid.NewGuid();
        var fileHash = ComputeCloneHash(source.Id, newCode, request.AdjustmentPercent, cloneId);

        var activeCardsByKey = new Dictionary<(string CarrierCode, string Direction, string ServiceCode, string PackageType), List<RateCard>>();
        if (request.Activate)
        {
            var candidateCards = await db.RateCards
                .Where(x => x.ImportBatch.CustomerId == customer.Id && x.IsActive)
                .ToListAsync(cancellationToken);

            activeCardsByKey = candidateCards
                .GroupBy(x => (x.CarrierCode, x.Direction, x.ServiceCode, x.PackageType))
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var clone = new RateImportBatch
        {
            Id = cloneId,
            CustomerId = customer.Id,
            SalesUserId = salesUserId,
            CarrierCode = source.CarrierCode,
            RateSetCode = newCode,
            FileName = $"Clone từ {sourceCode} ({request.AdjustmentPercent:0.##}%)",
            FileHash = fileHash,
            Status = "Committed",
            EffectiveFrom = source.EffectiveFrom,
            RateCardCount = source.RateCardCount,
            RateBandCount = source.RateBandCount,
            CountryZoneCount = source.CountryZoneCount,
            SurchargeCount = source.SurchargeCount,
            WarningCount = 0,
            ErrorCount = 0,
            CreatedAtUtc = now,
            CommittedAtUtc = now
        };

        db.RateImportBatches.Add(clone);

        foreach (var sourceCard in source.RateCards)
        {
            var activateThisCard = false;
            if (request.Activate)
            {
                var key = (sourceCard.CarrierCode, sourceCard.Direction, sourceCard.ServiceCode, sourceCard.PackageType);
                activeCardsByKey.TryGetValue(key, out var candidates);
                candidates ??= [];

                var hasNewerActiveVersion = candidates.Any(
                    x => x.IsActive && x.EffectiveFrom > sourceCard.EffectiveFrom);

                activateThisCard = !hasNewerActiveVersion;

                if (activateThisCard)
                {
                    var oldActiveCards = candidates
                        .Where(x => x.IsActive && x.EffectiveFrom <= sourceCard.EffectiveFrom)
                        .ToList();

                    foreach (var oldCard in oldActiveCards)
                    {
                        oldCard.IsActive = false;
                        oldCard.EffectiveTo = sourceCard.EffectiveFrom.AddDays(-1);
                    }
                }
            }

            var card = new RateCard
            {
                Id = Guid.NewGuid(),
                ImportBatchId = clone.Id,
                CarrierCode = sourceCard.CarrierCode,
                Direction = sourceCard.Direction,
                ServiceCode = sourceCard.ServiceCode,
                PackageType = sourceCard.PackageType,
                Currency = sourceCard.Currency,
                EffectiveFrom = sourceCard.EffectiveFrom,
                EffectiveTo = sourceCard.EffectiveTo,
                FuelMultiplier = sourceCard.FuelMultiplier,
                VatMultiplier = sourceCard.VatMultiplier,
                IsActive = activateThisCard,
                SourceSheet = sourceCard.SourceSheet
            };

            foreach (var sourceBand in sourceCard.Bands)
            {
                card.Bands.Add(new RateBand
                {
                    Id = Guid.NewGuid(),
                    ZoneCode = sourceBand.ZoneCode,
                    WeightFrom = sourceBand.WeightFrom,
                    WeightTo = sourceBand.WeightTo,
                    RateUnit = sourceBand.RateUnit,
                    Amount = ApplyPriceAdjustment(sourceBand.Amount, factor),
                    SourceRow = sourceBand.SourceRow
                });
            }

            clone.RateCards.Add(card);
        }

        foreach (var mapping in source.CountryZones)
        {
            clone.CountryZones.Add(new CountryZoneMapping
            {
                Id = Guid.NewGuid(),
                CarrierCode = mapping.CarrierCode,
                Direction = mapping.Direction,
                CountryName = mapping.CountryName,
                ServiceCode = mapping.ServiceCode,
                ZoneCode = mapping.ZoneCode,
                IsSupported = mapping.IsSupported,
                SourceSheet = mapping.SourceSheet,
                SourceRow = mapping.SourceRow
            });
        }

        foreach (var surcharge in source.Surcharges)
        {
            clone.Surcharges.Add(new RateSurcharge
            {
                Id = Guid.NewGuid(),
                CarrierCode = surcharge.CarrierCode,
                Direction = surcharge.Direction,
                ServiceCode = surcharge.ServiceCode,
                ZoneCode = surcharge.ZoneCode,
                Code = surcharge.Code,
                Name = surcharge.Name,
                CalculationType = surcharge.CalculationType,
                Amount = ShouldAdjustSurchargeAmount(surcharge.CalculationType)
                    ? ApplyPriceAdjustment(surcharge.Amount, factor)
                    : surcharge.Amount,
                Multiplier = surcharge.Multiplier,
                EffectiveFrom = surcharge.EffectiveFrom,
                IsInformational = surcharge.IsInformational,
                SourceSheet = surcharge.SourceSheet,
                SourceCell = surcharge.SourceCell
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RateImportCloneResponse(
            clone.Id,
            sourceCode,
            newCode,
            request.AdjustmentPercent,
            clone.RateCardCount,
            clone.RateBandCount,
            clone.CountryZoneCount,
            clone.SurchargeCount,
            now);
    }

    public async Task EnsureCustomerColumnAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH(N'dbo.RateImportBatches_ABCourier', N'CustomerId') IS NULL
            BEGIN
                ALTER TABLE dbo.RateImportBatches_ABCourier
                    ADD CustomerId uniqueidentifier NULL;
                CREATE INDEX IX_RateImportBatches_ABCourier_CustomerId
                    ON dbo.RateImportBatches_ABCourier(CustomerId);
                IF OBJECT_ID(N'dbo.Customers_ABCourier', N'U') IS NOT NULL
                   AND OBJECT_ID(N'dbo.FK_RateImportBatches_ABCourier_Customers', N'F') IS NULL
                BEGIN
                    ALTER TABLE dbo.RateImportBatches_ABCourier
                        ADD CONSTRAINT FK_RateImportBatches_ABCourier_Customers
                        FOREIGN KEY (CustomerId) REFERENCES dbo.Customers_ABCourier(Id);
                END
            END
            """, cancellationToken);
    }

    public async Task EnsureSalesUserColumnAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.RateImportBatches_ABCourier', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.RateImportBatches_ABCourier', N'SalesUserId') IS NULL
            BEGIN
                ALTER TABLE dbo.RateImportBatches_ABCourier ADD SalesUserId uniqueidentifier NULL;
            END
            """, cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.RateImportBatches_ABCourier', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.RateImportBatches_ABCourier', N'SalesUserId') IS NOT NULL
               AND OBJECT_ID(N'dbo.Users_ABCourier', N'U') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_RateImportBatches_ABCourier_SalesUser'
               )
            BEGIN
                ALTER TABLE dbo.RateImportBatches_ABCourier
                    ADD CONSTRAINT FK_RateImportBatches_ABCourier_SalesUser
                        FOREIGN KEY (SalesUserId) REFERENCES dbo.Users_ABCourier(Id);
            END
            """, cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.RateImportBatches_ABCourier', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.RateImportBatches_ABCourier', N'SalesUserId') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM sys.indexes WHERE name = N'IX_RateImportBatches_ABCourier_SalesUserId'
               )
            BEGIN
                CREATE INDEX IX_RateImportBatches_ABCourier_SalesUserId
                    ON dbo.RateImportBatches_ABCourier (SalesUserId);
            END
            """, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerOptionDto>> GetCustomersAsync(CancellationToken cancellationToken)
    {
        return await db.Customers.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.CompanyName)
            .Select(x => new CustomerOptionDto(
                x.Id,
                x.CustomerCode,
                x.CompanyName,
                x.SalesUserId,
                x.SalesUser != null ? x.SalesUser.DisplayName : "NOMI"))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SalesOptionDto>> GetSalesUsersAsync(CancellationToken cancellationToken)
    {
        var users = await db.Users.AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(cancellationToken);

        return users
            .Where(x => AuthService.HasDepartment(x.Department, "Sales"))
            .OrderBy(x => IsDefaultSales(x) ? 0 : 1)
            .ThenBy(x => x.DisplayName)
            .ThenBy(x => x.Email)
            .Select(x => new SalesOptionDto(
                x.Id,
                x.Email,
                x.DisplayName,
                IsDefaultSales(x)))
            .ToList();
    }

    private static bool IsDefaultSales(AppUser user) =>
        string.Equals(user.DisplayName, AuthService.DefaultSalesName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(user.Email, AuthService.DefaultSalesEmail, StringComparison.OrdinalIgnoreCase);

    private async Task<Customer> ResolveCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        if (customerId == Guid.Empty)
        {
            throw new InvalidOperationException("Hãy chọn khách hàng cho bộ giá.");
        }

        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == customerId, cancellationToken);
        if (customer is null)
        {
            throw new InvalidOperationException("Khách hàng không tồn tại trong Customers_ABCourier.");
        }

        if (!customer.IsActive)
        {
            throw new InvalidOperationException($"Khách hàng “{customer.CompanyName}” đang ngừng hoạt động.");
        }

        return customer;
    }

    private static decimal ApplyPriceAdjustment(decimal amount, decimal factor) =>
        decimal.Round(amount * factor, 2, MidpointRounding.AwayFromZero);

    private static bool ShouldAdjustSurchargeAmount(string calculationType) =>
        calculationType is "PerKg" or "Fixed";

    private static string ComputeCloneHash(
        Guid sourceBatchId,
        string newCode,
        decimal adjustmentPercent,
        Guid cloneId)
    {
        var payload = $"{sourceBatchId:N}|{newCode}|{adjustmentPercent}|{cloneId:N}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<RateImportRenameResponse> BuildRenameResponseAsync(
        string code,
        IReadOnlyList<RateImportBatch> targets,
        CancellationToken cancellationToken)
    {
        var batchIds = targets.Select(x => x.Id).ToList();

        var rateCardCount = await db.RateCards.CountAsync(
            x => batchIds.Contains(x.ImportBatchId),
            cancellationToken);
        var rateBandCount = await db.RateBands.CountAsync(
            x => batchIds.Contains(x.RateCard.ImportBatchId),
            cancellationToken);
        var countryZoneCount = await db.CountryZoneMappings.CountAsync(
            x => batchIds.Contains(x.ImportBatchId),
            cancellationToken);
        var surchargeCount = await db.RateSurcharges.CountAsync(
            x => batchIds.Contains(x.ImportBatchId),
            cancellationToken);

        return new RateImportRenameResponse(
            code,
            targets.Count,
            rateCardCount,
            rateBandCount,
            countryZoneCount,
            surchargeCount);
    }

    private static RateImportPreviewResponse ToPreviewResponse(string token, ParsedRateWorkbook parsed)
    {
        var dates = parsed.RateCards.Select(x => x.EffectiveFrom).ToArray();
        return new RateImportPreviewResponse(
            token,
            parsed.FileName,
            parsed.FileHash,
            parsed.CarrierCode,
            dates.Length == 0 ? null : dates.Min(),
            dates.Length == 0 ? null : dates.Max(),
            parsed.RateCards.Count,
            parsed.RateCards.Sum(x => x.Bands.Count),
            parsed.CountryZones.Count,
            parsed.Surcharges.Count,
            parsed.WarningCount,
            parsed.ErrorCount,
            parsed.SheetSummaries,
            parsed.Issues);
    }

    public async Task<RateImportDeleteResponse> DeleteBatchAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var batch = await db.RateImportBatches
            .FirstOrDefaultAsync(x => x.Id == batchId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy bảng giá đã import.");

        var cardIds = await db.RateCards
            .Where(x => x.ImportBatchId == batchId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var unlinkedQuoteCount = 0;
        if (cardIds.Count > 0)
        {
            unlinkedQuoteCount = await db.BookingQuotes
                .Where(x => x.RateCardId != null && cardIds.Contains(x.RateCardId.Value))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(quote => quote.RateCardId, (Guid?)null),
                    cancellationToken);
        }

        var result = new RateImportDeleteResponse(
            batch.Id,
            batch.RateSetCode,
            batch.RateCardCount,
            batch.RateBandCount,
            unlinkedQuoteCount);

        db.RateImportBatches.Remove(batch);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new InvalidOperationException(
                "Không xóa được bảng giá vì dữ liệu đang được bảng khác tham chiếu. " +
                (ex.InnerException?.Message ?? ex.Message));
        }

        return result;
    }

    public async Task<RateBatchViewDto> GetBatchViewAsync(
        Guid batchId,
        string? direction,
        string? serviceCode,
        string? packageType,
        CancellationToken cancellationToken,
        Guid? customerId = null,
        Guid? salesUserId = null)
    {
        var batch = await db.RateImportBatches.AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.SalesUser)
            .FirstOrDefaultAsync(x => x.Id == batchId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy bảng giá đã import.");

        if (customerId.HasValue &&
            (batch.CustomerId != customerId || batch.Status != "Committed"))
        {
            throw new KeyNotFoundException("Không tìm thấy bảng giá của khách hàng này.");
        }

        if (salesUserId.HasValue && batch.SalesUserId != salesUserId)
        {
            throw new KeyNotFoundException("Không tìm thấy bảng giá gắn với Sales này.");
        }

        var cards = await db.RateCards.AsNoTracking()
            .Where(x => x.ImportBatchId == batchId)
            .Select(x => new
            {
                x.Id,
                x.Direction,
                x.ServiceCode,
                x.PackageType,
                x.SourceSheet,
                x.Currency,
                x.FuelMultiplier,
                x.VatMultiplier,
                x.EffectiveFrom
            })
            .ToListAsync(cancellationToken);

        var sheets = cards
            .GroupBy(x => new { x.Direction, x.ServiceCode, x.PackageType })
            .Select(group =>
            {
                var first = group.First();
                return new RateSheetOptionDto(
                    first.Direction,
                    first.ServiceCode,
                    first.PackageType,
                    first.SourceSheet);
            })
            .OrderBy(x => x.Direction)
            .ThenBy(x => x.ServiceCode)
            .ThenBy(x => x.PackageType)
            .ToList();

        var selectedSheet = sheets.FirstOrDefault(item =>
            Matches(item.Direction, direction) &&
            Matches(item.ServiceCode, serviceCode) &&
            Matches(item.PackageType, packageType)) ?? sheets.FirstOrDefault();

        RateMatrixDto? matrix = null;
        if (selectedSheet is not null)
        {
            var card = cards.First(x =>
                x.Direction == selectedSheet.Direction &&
                x.ServiceCode == selectedSheet.ServiceCode &&
                x.PackageType == selectedSheet.PackageType);

            var bands = await db.RateBands.AsNoTracking()
                .Where(x => x.RateCardId == card.Id)
                .ToListAsync(cancellationToken);

            var zones = bands
                .Select(x => x.ZoneCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(ZoneSortKey)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rows = bands
                .GroupBy(x => (x.WeightFrom, x.WeightTo, x.RateUnit))
                .OrderBy(group => group.Key.WeightFrom)
                .ThenBy(group => group.Key.WeightTo)
                .Select(group => new RateMatrixRowDto(
                    group.Key.WeightFrom,
                    group.Key.WeightTo,
                    group.Key.RateUnit,
                    group
                        .GroupBy(x => x.ZoneCode, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            x => x.Key,
                            x => x.First().Amount,
                            StringComparer.OrdinalIgnoreCase)))
                .ToList();

            matrix = new RateMatrixDto(
                card.Direction,
                card.ServiceCode,
                card.PackageType,
                card.SourceSheet,
                card.Currency,
                card.FuelMultiplier,
                card.VatMultiplier,
                card.EffectiveFrom,
                zones,
                rows);
        }

        var countryQuery = db.CountryZoneMappings.AsNoTracking()
            .Where(x => x.ImportBatchId == batchId);
        if (selectedSheet is not null)
        {
            countryQuery = countryQuery.Where(x =>
                x.Direction == selectedSheet.Direction &&
                x.ServiceCode == selectedSheet.ServiceCode);
        }

        var countries = await countryQuery
            .OrderBy(x => x.CountryName)
            .ThenBy(x => x.ZoneCode)
            .Select(x => new RateCountryZoneDto(
                x.CountryName,
                x.ZoneCode,
                x.Direction,
                x.ServiceCode,
                x.IsSupported))
            .ToListAsync(cancellationToken);

        var surchargeQuery = db.RateSurcharges.AsNoTracking()
            .Where(x => x.ImportBatchId == batchId);
        if (selectedSheet is not null)
        {
            var selectedService = selectedSheet.ServiceCode;
            var relatedService = selectedService switch
            {
                "IPF" => "IP",
                "IEF" => "IE",
                _ => selectedService
            };
            surchargeQuery = surchargeQuery.Where(x =>
                x.Direction == selectedSheet.Direction &&
                (x.ServiceCode == selectedService ||
                 (relatedService != selectedService &&
                  x.ServiceCode == relatedService &&
                  x.ZoneCode == null)));
        }

        var surcharges = await surchargeQuery
            .OrderBy(x => x.Direction)
            .ThenBy(x => x.ServiceCode)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.ZoneCode)
            .Select(x => new SurchargeDto(
                x.Id,
                x.ImportBatchId,
                x.CarrierCode,
                x.Direction,
                x.ServiceCode,
                x.ZoneCode,
                x.Code,
                x.Name,
                x.CalculationType,
                x.Amount,
                x.Multiplier,
                x.EffectiveFrom,
                x.IsInformational,
                x.SourceSheet,
                x.SourceCell))
            .ToListAsync(cancellationToken);

        return new RateBatchViewDto(
            batch.Id,
            batch.CarrierCode,
            batch.RateSetCode,
            batch.FileName,
            batch.Status,
            batch.EffectiveFrom,
            batch.CustomerId,
            batch.Customer?.CustomerCode,
            batch.Customer?.CompanyName,
            batch.SalesUserId,
            batch.SalesUser?.DisplayName ?? AuthService.DefaultSalesName,
            batch.RateBandCount,
            batch.CountryZoneCount,
            batch.SurchargeCount,
            sheets,
            matrix,
            countries,
            surcharges);
    }

    private static bool Matches(string actual, string? expected) =>
        string.IsNullOrWhiteSpace(expected) ||
        actual.Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase);

    private static int ZoneSortKey(string zone)
    {
        var trimmed = zone.Trim();
        return int.TryParse(trimmed, out var number) ? number : int.MaxValue;
    }
}
