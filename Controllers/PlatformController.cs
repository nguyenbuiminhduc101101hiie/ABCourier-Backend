using System.Security.Claims;
using CourierApi.Contracts;
using CourierApi.Hubs;
using CourierApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CourierApi.Controllers;

[ApiController]
[Authorize]
[Route("api/platform")]
public sealed class PlatformController(
    PlatformDataService service,
    ScaleImportService scaleImports,
    RateImportApplicationService rateImports,
    IHubContext<BookingNotificationHub> bookingHub) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardDto>> Dashboard(CancellationToken ct)
        => Ok(await service.GetDashboardAsync(CustomerFilter(), ct, SalesFilter()));

    [HttpGet("bookings")]
    public async Task<ActionResult<IReadOnlyList<BookingSummaryDto>>> Bookings(CancellationToken ct)
        => Ok(await service.GetBookingsAsync(CustomerFilter(), ct, SalesFilter()));

    [HttpGet("bookings/{bookingId}")]
    public async Task<ActionResult<BookingDetailDto>> BookingDetail(string bookingId, CancellationToken ct)
    {
        var detail = await service.GetBookingDetailAsync(bookingId, CustomerFilter(), ct, SalesFilter());
        return detail is null ? NotFound(new { message = "Không tìm thấy booking." }) : Ok(detail);
    }

    [HttpPut("bookings/{bookingId}/details")]
    public async Task<ActionResult<BookingDetailDto>> UpdateBookingDetails(
        string bookingId,
        [FromBody] UpdateBookingDetailsRequest request,
        CancellationToken ct)
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : (Guid?)null;
        try
        {
            if (IsSales()) return Forbid();
            var detail = await service.UpdateBookingDetailsAsync(bookingId, request, CustomerFilter(), userId, ct, SalesFilter());
            await NotifyBookingChangedAsync("updated", bookingId, detail.Status, ct);
            return Ok(detail);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("bookings")]
    public async Task<ActionResult<CreateBookingResult>> CreateBooking([FromBody] CreateBookingRequest request, CancellationToken ct)
    {
        if (IsSales()) return Forbid();
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : (Guid?)null;
        try
        {
            var result = await service.CreateBookingAsync(request, userId, CustomerFilter(), ct);
            await NotifyBookingChangedAsync("created", result.BookingId, result.Status, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("bookings/quote")]
    public async Task<ActionResult<BookingRateQuoteDto>> PreviewQuote(
        [FromBody] BookingQuotePreviewRequest request,
        CancellationToken ct)
    {
        if (IsSales()) return Forbid();
        try
        {
            return Ok(await service.PreviewQuoteAsync(request, CustomerFilter(), ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("bookings/{bookingId}")]
    public async Task<IActionResult> UpdateBooking(string bookingId, [FromBody] UpdateBookingStatusRequest request, CancellationToken ct)
    {
        if (IsSales()) return Forbid();
        await service.UpdateBookingStatusAsync(bookingId, request.Status, request.Note, ct);
        await NotifyBookingChangedAsync("updated", bookingId, request.Status, ct);
        return NoContent();
    }

    [HttpPost("scale-imports")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ActionResult<ScaleImportResult>> ImportScale(
        [FromForm] IFormFile file,
        CancellationToken ct)
    {
        var department = User.FindFirstValue("department") ?? "";
        if (!string.Equals(department, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        if (file.Length == 0)
        {
            return BadRequest(new { message = "File Excel rỗng." });
        }

        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".xls", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Chỉ hỗ trợ file .xls hoặc .xlsx." });
        }

        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
            ? id
            : (Guid?)null;
        try
        {
            await using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            var result = await scaleImports.ImportAsync(buffer, file.FileName, userId, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("jobs")]
    public async Task<ActionResult<IReadOnlyList<JobSummaryDto>>> Jobs(CancellationToken ct)
        => Ok(await service.GetJobsAsync(CustomerFilter(), ct, SalesFilter()));

    [HttpPost("jobs")]
    public async Task<ActionResult<CreateJobResult>> CreateJob([FromBody] CreateJobRequest request, CancellationToken ct)
    {
        if (IsSales()) return Forbid();
        try
        {
            var result = await service.CreateJobAsync(request.BookingId, request.AssignedUserId, ct);
            await NotifyBookingChangedAsync("jobCreated", request.BookingId, result.Status, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("jobs/{jobId}/awb")]
    public async Task<ActionResult<CreateJobResult>> UpdateJobAwb(
        string jobId,
        [FromBody] UpdateJobAwbRequest request,
        CancellationToken ct)
    {
        if (IsSales()) return Forbid();
        var department = User.FindFirstValue("department") ?? "";
        if (department.Contains("Customer", StringComparison.OrdinalIgnoreCase) &&
            !department.Contains("Admin", StringComparison.OrdinalIgnoreCase) &&
            !department.Contains("Staff", StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        try
        {
            var result = await service.UpdateJobAwbAsync(jobId, request.Awb, ct);
            await NotifyBookingChangedAsync("updated", result.JobId, result.Status, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("pickup-tasks")]
    public async Task<ActionResult<IReadOnlyList<PickupTaskDto>>> PickupTasks(CancellationToken ct)
    {
        Guid? staffFilter = null;
        var active = User.FindFirstValue("department") ?? "";
        if (string.Equals(active, "Staff", StringComparison.OrdinalIgnoreCase))
        {
            staffFilter = CurrentUserId();
        }

        return Ok(await service.GetPickupTasksAsync(staffFilter, ct));
    }

    [HttpGet("pickup-tasks/{pickupNo}/directions")]
    public async Task<ActionResult<DirectionsRouteDto>> PickupDirections(string pickupNo, CancellationToken ct)
    {
        try
        {
            var result = await service.GetPickupDirectionsAsync(pickupNo, ct);
            return result is null ? NotFound(new { message = "Không tìm thấy pickup task." }) : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("pickup-tasks/{pickupNo}/location")]
    public async Task<IActionResult> SaveCourierLocation(string pickupNo, [FromBody] SaveCourierLocationRequest request, CancellationToken ct)
    {
        try
        {
            await service.SaveCourierLocationAsync(pickupNo, request, CurrentUserId(), ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("pickup-tasks/{pickupNo}/confirm")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ActionResult<PickupTaskDto>> ConfirmPickup(
        string pickupNo,
        [FromForm] List<IFormFile>? photos,
        [FromForm] IFormFile? signature,
        CancellationToken ct)
    {
        var department = User.FindFirstValue("department") ?? "";
        var staffOnly = string.Equals(department, "Staff", StringComparison.OrdinalIgnoreCase);
        try
        {
            var result = await service.ConfirmPickupCaptureAsync(
                pickupNo,
                photos ?? [],
                signature,
                CurrentUserId(),
                staffOnly,
                ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("pickup-evidence/{id:guid}")]
    public async Task<IActionResult> PickupEvidence(Guid id, CancellationToken ct)
    {
        var department = User.FindFirstValue("department") ?? "";
        var staffOnly = string.Equals(department, "Staff", StringComparison.OrdinalIgnoreCase);
        try
        {
            var file = await service.OpenPickupEvidenceAsync(id, CurrentUserId(), staffOnly, ct);
            if (file is null) return NotFound();
            return File(file.Value.Stream, file.Value.ContentType);
        }
        catch (InvalidOperationException)
        {
            return Forbid();
        }
    }

    [HttpDelete("pickup-evidence/{id:guid}")]
    public async Task<ActionResult<PickupTaskDto>> DeletePickupEvidence(Guid id, CancellationToken ct)
    {
        var department = User.FindFirstValue("department") ?? "";
        var staffOnly = string.Equals(department, "Staff", StringComparison.OrdinalIgnoreCase);
        try
        {
            var result = await service.DeletePickupEvidenceAsync(id, CurrentUserId(), staffOnly, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("pickup-tasks/{pickupNo}")]
    public async Task<IActionResult> UpdatePickupTask(string pickupNo, [FromBody] UpdatePickupTaskRequest request, CancellationToken ct)
    {
        try
        {
            await service.UpdatePickupTaskAsync(pickupNo, request, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("pickup-tasks/sync")]
    public async Task<IActionResult> SyncPickupTasks(CancellationToken ct)
    {
        await service.SyncPendingPickupTasksAsync(ct);
        return NoContent();
    }

    [HttpGet("invoices")]
    public async Task<ActionResult<IReadOnlyList<InvoiceSummaryDto>>> Invoices(CancellationToken ct)
        => Ok(await service.GetInvoicesAsync(CustomerFilter(), ct, SalesFilter()));

    [HttpGet("customer-profile")]
    public async Task<ActionResult<CustomerProfileDto>> CustomerProfile(CancellationToken ct)
    {
        var customerId = CustomerFilter();
        if (customerId is null) return BadRequest(new { message = "Chỉ tài khoản Customer mới có hồ sơ khách hàng." });
        var profile = await service.GetCustomerProfileAsync(customerId.Value, ct);
        return profile is null ? NotFound() : Ok(profile);
    }

    [HttpGet("rate-tables")]
    public async Task<ActionResult<IReadOnlyList<RateImportHistoryDto>>> RateTables(CancellationToken ct)
    {
        var customerId = CustomerFilter();
        if (customerId is null)
        {
            return BadRequest(new { message = "Chỉ tài khoản khách hàng mới xem được bảng giá của mình." });
        }

        return Ok(await rateImports.GetHistoryAsync(null, ct, customerId));
    }

    [HttpGet("rate-tables/{batchId:guid}")]
    public async Task<ActionResult<RateBatchViewDto>> RateTableView(
        Guid batchId,
        [FromQuery] string? direction,
        [FromQuery] string? serviceCode,
        [FromQuery] string? packageType,
        CancellationToken ct)
    {
        var customerId = CustomerFilter();
        if (customerId is null)
        {
            return BadRequest(new { message = "Chỉ tài khoản khách hàng mới xem được bảng giá của mình." });
        }

        try
        {
            return Ok(await rateImports.GetBatchViewAsync(
                batchId,
                direction,
                serviceCode,
                packageType,
                ct,
                customerId));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("rate-countries")]
    public async Task<ActionResult<IReadOnlyList<CountryOptionDto>>> RateCountries(
        [FromQuery] string direction,
        [FromQuery] string serviceCode,
        [FromQuery] string? carrierCode,
        [FromQuery] DateTime? shipDate,
        CancellationToken ct)
    {
        var customerId = CustomerFilter();
        if (customerId is null)
        {
            return BadRequest(new { message = "Chỉ tài khoản khách hàng mới xem được quốc gia theo bảng giá." });
        }

        if (string.IsNullOrWhiteSpace(direction) || string.IsNullOrWhiteSpace(serviceCode))
        {
            return BadRequest(new { message = "Cần truyền direction và service." });
        }

        return Ok(await service.ListRateCountriesAsync(
            customerId.Value,
            direction,
            serviceCode,
            shipDate ?? DateTime.Today,
            carrierCode,
            ct));
    }

    [HttpPost("invoices")]
    public ActionResult CreateInvoice([FromBody] CreateInvoiceRequest request)
        => BadRequest(new { message = "Chưa hỗ trợ phát hành hóa đơn từ API. Dùng quy trình Billing Batch trên DB." });

    private Guid? CurrentUserId()
    {
        return Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"),
            out var id)
            ? id
            : null;
    }

    private Guid? CustomerFilter()
    {
        var department = User.FindFirstValue("department");
        if (!string.Equals(department, "Customer", StringComparison.OrdinalIgnoreCase)) return null;
        return Guid.TryParse(User.FindFirstValue("customerId"), out var customerId) ? customerId : null;
    }

    private Guid? SalesFilter()
    {
        var department = User.FindFirstValue("department");
        if (!string.Equals(department, "Sales", StringComparison.OrdinalIgnoreCase)) return null;
        return CurrentUserId();
    }

    private bool IsSales() => SalesFilter().HasValue;

    private Task NotifyBookingChangedAsync(string type, string bookingId, string? status, CancellationToken ct)
        => bookingHub.Clients.Group(BookingNotificationHub.CompanyGroup)
            .SendAsync("bookingChanged", new
            {
                type,
                bookingId,
                status,
                at = DateTime.UtcNow
            }, ct);
}
