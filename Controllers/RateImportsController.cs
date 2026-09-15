using System.Security.Claims;
using CourierApi.Contracts;
using CourierApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourierApi.Controllers;

[ApiController]
[Authorize]
[Route("api/rate-imports")]
public sealed class RateImportsController(
    RateImportApplicationService service,
    CloneJobStore cloneJobs) : ControllerBase
{
    private const long MaxFileSize = 20 * 1024 * 1024;

    [HttpPost("preview")]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<ActionResult<RateImportPreviewResponse>> Preview(
        [FromForm] IFormFile file,
        [FromForm] string carrierCode = "FEDEX",
        [FromForm] DateTime? effectiveFrom = null,
        CancellationToken cancellationToken = default)
    {
        if (IsSales()) return Forbid();
        if (file.Length == 0)
        {
            return BadRequest(new { message = "File Excel rỗng." });
        }

        if (file.Length > MaxFileSize)
        {
            return BadRequest(new { message = "File vượt quá 20 MB." });
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Chỉ hỗ trợ file .xlsx." });
        }

        await using var stream = file.OpenReadStream();
        var response = await service.PreviewAsync(
            stream,
            file.FileName,
            carrierCode,
            effectiveFrom,
            cancellationToken);

        return Ok(response);
    }

    [HttpPost("commit")]
    public async Task<ActionResult<RateImportCommitResponse>> Commit(
        [FromBody] RateImportCommitRequest request,
        CancellationToken cancellationToken)
    {
        if (IsSales()) return Forbid();
        try
        {
            return Ok(await service.CommitAsync(request, cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<RateImportHistoryDto>>> History(
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetHistoryAsync(search, cancellationToken, salesUserId: SalesFilter()));
    }

    [HttpGet("batches/{batchId:guid}/view")]
    public async Task<ActionResult<RateBatchViewDto>> View(
        Guid batchId,
        [FromQuery] string? direction,
        [FromQuery] string? serviceCode,
        [FromQuery] string? packageType,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetBatchViewAsync(
                batchId,
                direction,
                serviceCode,
                packageType,
                cancellationToken,
                salesUserId: SalesFilter()));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("rate-set-codes")]
    public async Task<ActionResult<IReadOnlyList<RateSetCodeOptionDto>>> RateSetCodes(
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetRateSetCodesAsync(cancellationToken, SalesFilter()));
    }

    [HttpGet("customers")]
    public async Task<ActionResult<IReadOnlyList<CustomerOptionDto>>> Customers(
        CancellationToken cancellationToken)
    {
        if (IsSales()) return Forbid();
        return Ok(await service.GetCustomersAsync(cancellationToken));
    }

    [HttpGet("sales")]
    public async Task<ActionResult<IReadOnlyList<SalesOptionDto>>> Sales(
        CancellationToken cancellationToken)
    {
        if (IsSales()) return Forbid();
        return Ok(await service.GetSalesUsersAsync(cancellationToken));
    }

    [HttpPost("clone")]
    public async Task<ActionResult<RateImportCloneResponse>> Clone(
        [FromBody] RateImportCloneRequest request,
        CancellationToken cancellationToken)
    {
        if (IsSales()) return Forbid();
        try
        {
            return Ok(await service.CloneRateSetAsync(request, cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("clone/start")]
    public async Task<ActionResult<RateImportCloneStartResponse>> CloneStart(
        [FromBody] RateImportCloneRequest request,
        CancellationToken cancellationToken)
    {
        if (IsSales()) return Forbid();
        try
        {
            var (jobId, totalBands) = await service.StartCloneRateSetAsync(request, cancellationToken);
            return Ok(new RateImportCloneStartResponse(jobId, totalBands));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("clone/status/{jobId:guid}")]
    public ActionResult<RateImportCloneStatusResponse> CloneStatus(Guid jobId)
    {
        if (IsSales()) return Forbid();
        if (!cloneJobs.TryGet(jobId, out var status))
        {
            return NotFound(new { message = "Không tìm thấy tiến trình. Có thể đã hết hạn." });
        }

        return Ok(new RateImportCloneStatusResponse(
            status.Status.ToString(),
            status.CurrentStep.ToString(),
            status.ProcessedBands,
            status.TotalBands,
            status.Result,
            status.Error,
            status.ErrorType?.ToString()));
    }

    [HttpPatch("batches/{batchId:guid}/rate-set-code")]
    public async Task<ActionResult<RateImportRenameResponse>> RenameRateSetCode(
        Guid batchId,
        [FromBody] RateImportRenameRequest request,
        CancellationToken cancellationToken)
    {
        if (IsSales()) return Forbid();
        try
        {
            return Ok(await service.RenameRateSetCodeAsync(
                batchId,
                request.RateSetCode,
                cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("batches/{batchId:guid}")]
    [HttpPost("batches/{batchId:guid}/delete")]
    public async Task<ActionResult<RateImportDeleteResponse>> DeleteBatch(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        if (IsSales()) return Forbid();
        try
        {
            return Ok(await service.DeleteBatchAsync(batchId, cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private Guid? CurrentUserId()
    {
        return Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"),
            out var id)
            ? id
            : null;
    }

    private Guid? SalesFilter()
    {
        var department = User.FindFirstValue("department");
        if (!string.Equals(department, "Sales", StringComparison.OrdinalIgnoreCase)) return null;
        return CurrentUserId();
    }

    private bool IsSales() => SalesFilter().HasValue;
}
