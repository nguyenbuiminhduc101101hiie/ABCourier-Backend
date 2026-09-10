using CourierApi.Contracts;
using CourierApi.Data;
using CourierApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourierApi.Controllers;

[ApiController]
[Authorize]
[Route("api/surcharges")]
public sealed class SurchargesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SurchargeDto>>> List(
        [FromQuery] Guid? importBatchId = null,
        [FromQuery] string carrierCode = "FEDEX",
        [FromQuery] string? direction = null,
        [FromQuery] string? serviceCode = null,
        [FromQuery] string? code = null,
        CancellationToken cancellationToken = default)
    {
        carrierCode = carrierCode.Trim().ToUpperInvariant();
        var batchId = importBatchId ?? await ResolveLatestBatchIdAsync(carrierCode, cancellationToken);
        if (batchId is null)
        {
            return Ok(Array.Empty<SurchargeDto>());
        }

        var query = db.RateSurcharges.AsNoTracking()
            .Where(x => x.ImportBatchId == batchId.Value && x.CarrierCode == carrierCode);

        if (!string.IsNullOrWhiteSpace(direction))
        {
            var dir = direction.Trim().ToUpperInvariant();
            query = query.Where(x => x.Direction == dir);
        }

        if (!string.IsNullOrWhiteSpace(serviceCode))
        {
            var svc = serviceCode.Trim().ToUpperInvariant();
            query = query.Where(x => x.ServiceCode == svc);
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            var c = code.Trim().ToUpperInvariant();
            query = query.Where(x => x.Code.ToUpper() == c);
        }

        var items = await query
            .OrderBy(x => x.Direction)
            .ThenBy(x => x.ServiceCode)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.ZoneCode)
            .Select(x => ToDto(x))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("batches")]
    public async Task<ActionResult<IReadOnlyList<RateBatchOptionDto>>> Batches(
        [FromQuery] string carrierCode = "FEDEX",
        CancellationToken cancellationToken = default)
    {
        carrierCode = carrierCode.Trim().ToUpperInvariant();
        var items = await db.RateImportBatches.AsNoTracking()
            .Where(x => x.CarrierCode == carrierCode && x.Status == "Committed")
            .OrderByDescending(x => x.CommittedAtUtc ?? x.CreatedAtUtc)
            .Take(50)
            .Select(x => new RateBatchOptionDto(
                x.Id,
                x.FileName,
                x.EffectiveFrom,
                x.CommittedAtUtc,
                x.SurchargeCount))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<SurchargeDto>> Create(
        [FromBody] UpsertSurchargeRequest request,
        CancellationToken cancellationToken)
    {
        var batchExists = await db.RateImportBatches
            .AnyAsync(x => x.Id == request.ImportBatchId, cancellationToken);
        if (!batchExists)
        {
            return BadRequest(new { message = "ImportBatchId không tồn tại." });
        }

        var entity = new RateSurcharge
        {
            Id = Guid.NewGuid(),
            ImportBatchId = request.ImportBatchId,
            CarrierCode = Normalize(request.CarrierCode, "FEDEX"),
            Direction = Normalize(request.Direction),
            ServiceCode = Normalize(request.ServiceCode),
            ZoneCode = string.IsNullOrWhiteSpace(request.ZoneCode)
                ? null
                : Normalize(request.ZoneCode),
            Code = Normalize(request.Code),
            Name = request.Name.Trim(),
            CalculationType = request.CalculationType.Trim(),
            Amount = request.Amount,
            Multiplier = request.Multiplier,
            EffectiveFrom = (request.EffectiveFrom ?? DateTime.Today).Date,
            IsInformational = request.IsInformational,
            SourceSheet = "Manual",
            SourceCell = string.Empty,
        };

        db.RateSurcharges.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        var batch = await db.RateImportBatches.FirstAsync(x => x.Id == request.ImportBatchId, cancellationToken);
        batch.SurchargeCount = await db.RateSurcharges.CountAsync(x => x.ImportBatchId == batch.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SurchargeDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.RateSurcharges.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? NotFound() : Ok(ToDto(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SurchargeDto>> Update(
        Guid id,
        [FromBody] UpsertSurchargeRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.RateSurcharges.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) return NotFound(new { message = "Không tìm thấy phụ phí." });

        entity.CarrierCode = Normalize(request.CarrierCode, "FEDEX");
        entity.Direction = Normalize(request.Direction);
        entity.ServiceCode = Normalize(request.ServiceCode);
        entity.ZoneCode = string.IsNullOrWhiteSpace(request.ZoneCode)
            ? null
            : Normalize(request.ZoneCode);
        entity.Code = Normalize(request.Code);
        entity.Name = request.Name.Trim();
        entity.CalculationType = request.CalculationType.Trim();
        entity.Amount = request.Amount;
        entity.Multiplier = request.Multiplier;
        entity.EffectiveFrom = (request.EffectiveFrom ?? entity.EffectiveFrom).Date;
        entity.IsInformational = request.IsInformational;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.RateSurcharges.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) return NotFound(new { message = "Không tìm thấy phụ phí." });

        var batchId = entity.ImportBatchId;
        db.RateSurcharges.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);

        var batch = await db.RateImportBatches.FirstOrDefaultAsync(x => x.Id == batchId, cancellationToken);
        if (batch is not null)
        {
            batch.SurchargeCount = await db.RateSurcharges.CountAsync(x => x.ImportBatchId == batchId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    private async Task<Guid?> ResolveLatestBatchIdAsync(string carrierCode, CancellationToken cancellationToken)
    {
        return await db.RateImportBatches.AsNoTracking()
            .Where(x => x.CarrierCode == carrierCode && x.Status == "Committed")
            .OrderByDescending(x => x.CommittedAtUtc ?? x.CreatedAtUtc)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static SurchargeDto ToDto(RateSurcharge x) => new(
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
        x.SourceCell);

    private static string Normalize(string value, string fallback = "")
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return string.IsNullOrEmpty(trimmed) ? fallback : trimmed.ToUpperInvariant();
    }
}
