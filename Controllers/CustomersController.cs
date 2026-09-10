using System.Security.Claims;
using CourierApi.Contracts;
using CourierApi.Data;
using CourierApi.Domain;
using CourierApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourierApi.Controllers;

[ApiController]
[Authorize]
[Route("api/customers")]
public sealed class CustomersController(AppDbContext db, AuthService auth) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerListItemDto>>> List(CancellationToken ct)
    {
        if (!IsAdmin()) return StatusCode(403, new { message = "Cần quyền Admin." });

        var items = await db.Customers.AsNoTracking()
            .Include(x => x.SalesUser)
            .OrderBy(x => x.CompanyName)
            .ToListAsync(ct);
        return Ok(items.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerListItemDto>> Get(Guid id, CancellationToken ct)
    {
        if (!IsAdmin()) return StatusCode(403, new { message = "Cần quyền Admin." });

        var customer = await db.Customers.AsNoTracking()
            .Include(x => x.SalesUser)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (customer is null) return NotFound(new { message = "Không tìm thấy khách hàng." });
        return Ok(ToDto(customer));
    }

    [HttpPost]
    public async Task<ActionResult<CustomerListItemDto>> Create(
        [FromBody] UpsertCustomerRequest request,
        CancellationToken ct)
    {
        if (!IsAdmin()) return StatusCode(403, new { message = "Cần quyền Admin." });

        var error = Validate(request);
        if (error is not null) return BadRequest(new { message = error });

        var code = NormalizeCode(request.CustomerCode);
        if (await db.Customers.AnyAsync(x => x.CustomerCode == code, ct))
        {
            return Conflict(new { message = "Mã khách hàng đã tồn tại." });
        }

        var now = DateTime.UtcNow;
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            CustomerCode = code,
            CompanyName = request.CompanyName.Trim(),
            TaxCode = TrimOrNull(request.TaxCode),
            BillingEmail = TrimOrNull(request.BillingEmail),
            CreditLimit = request.CreditLimit,
            BillingCycle = NormalizeCycle(request.BillingCycle),
            RatePlanCode = TrimOrNull(request.RatePlanCode),
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };
        await auth.AssignCustomerSalesAsync(customer, null, ct);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        await db.Entry(customer).Reference(x => x.SalesUser).LoadAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, ToDto(customer));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerListItemDto>> Update(
        Guid id,
        [FromBody] UpsertCustomerRequest request,
        CancellationToken ct)
    {
        if (!IsAdmin()) return StatusCode(403, new { message = "Cần quyền Admin." });

        var error = Validate(request);
        if (error is not null) return BadRequest(new { message = error });

        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (customer is null) return NotFound(new { message = "Không tìm thấy khách hàng." });

        var code = NormalizeCode(request.CustomerCode);
        if (await db.Customers.AnyAsync(x => x.CustomerCode == code && x.Id != id, ct))
        {
            return Conflict(new { message = "Mã khách hàng đã tồn tại." });
        }

        customer.CustomerCode = code;
        customer.CompanyName = request.CompanyName.Trim();
        customer.TaxCode = TrimOrNull(request.TaxCode);
        customer.BillingEmail = TrimOrNull(request.BillingEmail);
        customer.CreditLimit = request.CreditLimit;
        customer.BillingCycle = NormalizeCycle(request.BillingCycle);
        customer.RatePlanCode = TrimOrNull(request.RatePlanCode);
        customer.IsActive = request.IsActive;
        customer.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(customer));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!IsAdmin()) return StatusCode(403, new { message = "Cần quyền Admin." });

        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (customer is null) return NotFound(new { message = "Không tìm thấy khách hàng." });

        var linkedUsers = await db.Users.AnyAsync(x => x.CustomerId == id, ct);
        var linkedBookings = await db.Bookings.AnyAsync(x => x.CustomerId == id, ct);
        var linkedJobs = await db.Jobs.AnyAsync(x => x.CustomerId == id, ct);
        var linkedInvoices = await db.Invoices.AnyAsync(x => x.CustomerId == id, ct);
        var linkedAddresses = await db.CustomerAddresses.AnyAsync(x => x.CustomerId == id, ct);
        var linkedRateSets = await db.RateImportBatches.AnyAsync(x => x.CustomerId == id, ct);
        if (linkedUsers || linkedBookings || linkedJobs || linkedInvoices || linkedAddresses || linkedRateSets)
        {
            return Conflict(new
            {
                message = "Không xóa được vì khách hàng đã gắn user, booking, job, hóa đơn, địa chỉ hoặc bảng giá. Hãy ngừng hoạt động thay vì xóa."
            });
        }

        db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private bool IsAdmin()
    {
        var active = User.FindFirstValue("department");
        if (string.Equals(active, "Admin", StringComparison.OrdinalIgnoreCase)) return true;
        var all = User.FindFirstValue("departments") ?? "";
        return AuthService.HasDepartment(all, "Admin") || User.IsInRole("Admin");
    }

    private static CustomerListItemDto ToDto(Customer x) => new(
        x.Id,
        x.CustomerCode,
        x.CompanyName,
        x.TaxCode,
        x.BillingEmail,
        x.CreditLimit,
        x.BillingCycle,
        x.RatePlanCode,
        x.SalesUserId,
        x.SalesUser?.DisplayName ?? AuthService.DefaultSalesName,
        x.IsActive,
        x.CreatedAt,
        x.UpdatedAt);

    private static string? Validate(UpsertCustomerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerCode))
            return "Mã khách hàng là bắt buộc.";
        if (request.CustomerCode.Trim().Length > 30)
            return "Mã khách hàng tối đa 30 ký tự.";
        if (string.IsNullOrWhiteSpace(request.CompanyName))
            return "Tên công ty là bắt buộc.";
        if (request.CreditLimit < 0)
            return "Hạn mức tín dụng không được âm.";
        var email = TrimOrNull(request.BillingEmail);
        if (email is not null && !email.Contains('@'))
            return "Email thanh toán không hợp lệ.";
        return null;
    }

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();

    private static string NormalizeCycle(string? value)
    {
        var cycle = value?.Trim();
        return string.IsNullOrWhiteSpace(cycle) ? "Monthly" : cycle;
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
