using System.Security.Claims;
using CourierApi.Contracts;
using CourierApi.Data;
using CourierApi.Domain;
using CourierApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourierApi.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController(
    AppDbContext db,
    IPasswordHasher<AppUser> passwordHasher,
    AuthService auth) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserListItemDto>>> List(CancellationToken ct)
    {
        if (!IsAdmin()) return Forbid();

        var users = await db.Users.AsNoTracking()
            .Include(x => x.Customer)
                .ThenInclude(x => x!.SalesUser)
            .OrderBy(x => x.Email)
            .ToListAsync(ct);

        return Ok(users.Select(ToDto).ToList());
    }

    [HttpGet("customers")]
    public async Task<ActionResult<IReadOnlyList<CustomerListItemDto>>> Customers(CancellationToken ct)
    {
        if (!IsAdmin()) return StatusCode(403, new { message = "Cần quyền Admin." });

        var items = await db.Customers.AsNoTracking()
            .Include(x => x.SalesUser)
            .OrderBy(x => x.CompanyName)
            .ToListAsync(ct);
        return Ok(items.Select(ToCustomerDto).ToList());
    }

    [HttpGet("sales")]
    public async Task<ActionResult<IReadOnlyList<SalesOptionDto>>> Sales(CancellationToken ct)
    {
        if (!IsAdmin()) return StatusCode(403, new { message = "Cần quyền Admin." });

        var users = await db.Users.AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        return Ok(users
            .Where(x => AuthService.HasDepartment(x.Department, "Sales"))
            .OrderBy(x => IsDefaultSales(x) ? 0 : 1)
            .ThenBy(x => x.DisplayName)
            .ThenBy(x => x.Email)
            .Select(x => new SalesOptionDto(
                x.Id,
                x.Email,
                x.DisplayName,
                IsDefaultSales(x)))
            .ToList());
    }

    [HttpPost("customers")]
    public async Task<ActionResult<CustomerListItemDto>> CreateCustomer(
        [FromBody] UpsertCustomerRequest request,
        CancellationToken ct)
    {
        if (!IsAdmin()) return StatusCode(403, new { message = "Cần quyền Admin." });

        var error = ValidateCustomer(request);
        if (error is not null) return BadRequest(new { message = error });

        var code = request.CustomerCode.Trim().ToUpperInvariant();
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
            Address = TrimOrNull(request.Address),
            CreditLimit = request.CreditLimit,
            BillingCycle = string.IsNullOrWhiteSpace(request.BillingCycle) ? "Monthly" : request.BillingCycle.Trim(),
            RatePlanCode = TrimOrNull(request.RatePlanCode),
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };
        await auth.AssignCustomerSalesAsync(customer, null, ct);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        await db.Entry(customer).Reference(x => x.SalesUser).LoadAsync(ct);
        return CreatedAtAction(nameof(Customers), ToCustomerDto(customer));
    }

    [HttpPut("customers/{id:guid}")]
    public async Task<ActionResult<CustomerListItemDto>> UpdateCustomer(
        Guid id,
        [FromBody] UpsertCustomerRequest request,
        CancellationToken ct)
    {
        if (!IsAdmin()) return StatusCode(403, new { message = "Cần quyền Admin." });

        var error = ValidateCustomer(request);
        if (error is not null) return BadRequest(new { message = error });

        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (customer is null) return NotFound(new { message = "Không tìm thấy khách hàng." });

        var code = request.CustomerCode.Trim().ToUpperInvariant();
        if (await db.Customers.AnyAsync(x => x.CustomerCode == code && x.Id != id, ct))
        {
            return Conflict(new { message = "Mã khách hàng đã tồn tại." });
        }

        customer.CustomerCode = code;
        customer.CompanyName = request.CompanyName.Trim();
        customer.TaxCode = TrimOrNull(request.TaxCode);
        customer.BillingEmail = TrimOrNull(request.BillingEmail);
        customer.Address = TrimOrNull(request.Address);
        customer.CreditLimit = request.CreditLimit;
        customer.BillingCycle = string.IsNullOrWhiteSpace(request.BillingCycle) ? "Monthly" : request.BillingCycle.Trim();
        customer.RatePlanCode = TrimOrNull(request.RatePlanCode);
        customer.IsActive = request.IsActive;
        customer.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await db.Entry(customer).Reference(x => x.SalesUser).LoadAsync(ct);
        return Ok(ToCustomerDto(customer));
    }

    [HttpDelete("customers/{id:guid}")]
    public async Task<IActionResult> DeleteCustomer(Guid id, CancellationToken ct)
    {
        if (!IsAdmin()) return StatusCode(403, new { message = "Cần quyền Admin." });

        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (customer is null) return NotFound(new { message = "Không tìm thấy khách hàng." });

        if (await db.Users.AnyAsync(x => x.CustomerId == id, ct) ||
            await db.Bookings.AnyAsync(x => x.CustomerId == id, ct) ||
            await db.Jobs.AnyAsync(x => x.CustomerId == id, ct) ||
            await db.Invoices.AnyAsync(x => x.CustomerId == id, ct) ||
            await db.CustomerAddresses.AnyAsync(x => x.CustomerId == id, ct))
        {
            return Conflict(new
            {
                message = "Không xóa được vì khách hàng đã gắn user, booking, job, hóa đơn hoặc địa chỉ. Hãy ngừng hoạt động thay vì xóa."
            });
        }

        db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<UserListItemDto>> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken ct)
    {
        if (!IsAdmin()) return Forbid();

        var email = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return BadRequest(new { message = "Email không hợp lệ." });
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 4)
        {
            return BadRequest(new { message = "Mật khẩu tối thiểu 4 ký tự." });
        }

        var departments = AuthService.NormalizeStoredDepartments(request.Department);
        if (string.IsNullOrWhiteSpace(departments))
        {
            return BadRequest(new { message = "Department phải gồm Customer, Staff, Admin, Sales hoặc RateHub." });
        }

        if (await db.Users.AnyAsync(x => x.Email == email, ct))
        {
            return Conflict(new { message = "Email đã tồn tại." });
        }

        if (request.CustomerId.HasValue &&
            !await db.Customers.AnyAsync(x => x.Id == request.CustomerId.Value, ct))
        {
            return BadRequest(new { message = "CustomerId không tồn tại." });
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? email : request.DisplayName.Trim(),
            Department = departments,
            CustomerId = request.CustomerId,
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await AssignLinkedCustomerSalesAsync(request.CustomerId, departments, request.SalesUserId, ct);
        await db.SaveChangesAsync(ct);
        await ReloadUserAsync(user, ct);
        return CreatedAtAction(nameof(List), ToDto(user));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserListItemDto>> Update(
        Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken ct)
    {
        if (!IsAdmin()) return Forbid();

        var user = await db.Users.Include(x => x.Customer).ThenInclude(x => x!.SalesUser).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null) return NotFound(new { message = "Không tìm thấy user." });

        var departments = AuthService.NormalizeStoredDepartments(request.Department);
        if (string.IsNullOrWhiteSpace(departments))
        {
            return BadRequest(new { message = "Department phải gồm Customer, Staff, Admin, Sales hoặc RateHub." });
        }

        if (request.CustomerId.HasValue &&
            !await db.Customers.AnyAsync(x => x.Id == request.CustomerId.Value, ct))
        {
            return BadRequest(new { message = "CustomerId không tồn tại." });
        }

        user.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? user.Email
            : request.DisplayName.Trim();
        user.Department = departments;
        user.CustomerId = request.CustomerId;
        user.IsActive = request.IsActive;

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            if (request.NewPassword.Length < 4)
            {
                return BadRequest(new { message = "Mật khẩu mới tối thiểu 4 ký tự." });
            }
            user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        }

        await AssignLinkedCustomerSalesAsync(request.CustomerId, departments, request.SalesUserId, ct);
        await db.SaveChangesAsync(ct);
        await ReloadUserAsync(user, ct);
        return Ok(ToDto(user));
    }

    private bool IsAdmin()
    {
        var active = User.FindFirstValue("department");
        if (string.Equals(active, "Admin", StringComparison.OrdinalIgnoreCase)) return true;
        var all = User.FindFirstValue("departments") ?? "";
        return AuthService.HasDepartment(all, "Admin") ||
               User.IsInRole("Admin");
    }

    private static UserListItemDto ToDto(AppUser user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Department,
        AuthService.ParseDepartments(user.Department),
        user.CustomerId,
        user.Customer?.CompanyName,
        user.Customer?.SalesUserId,
        user.CustomerId is null ? null : (user.Customer?.SalesUser?.DisplayName ?? AuthService.DefaultSalesName),
        user.IsActive,
        user.CreatedAtUtc,
        user.LastLoginAtUtc);

    private static CustomerListItemDto ToCustomerDto(Customer x) => new(
        x.Id,
        x.CustomerCode,
        x.CompanyName,
        x.TaxCode,
        x.BillingEmail,
        x.Address,
        x.CreditLimit,
        x.BillingCycle,
        x.RatePlanCode,
        x.SalesUserId,
        x.SalesUser?.DisplayName ?? AuthService.DefaultSalesName,
        x.IsActive,
        x.CreatedAt,
        x.UpdatedAt);

    private static string? ValidateCustomer(UpsertCustomerRequest request)
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

    private async Task AssignLinkedCustomerSalesAsync(
        Guid? customerId,
        string departments,
        Guid? salesUserId,
        CancellationToken ct)
    {
        if (!customerId.HasValue || !AuthService.HasDepartment(departments, "Customer")) return;
        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == customerId.Value, ct);
        if (customer is null) return;
        await auth.AssignCustomerSalesAsync(customer, salesUserId, ct);
    }

    private async Task ReloadUserAsync(AppUser user, CancellationToken ct)
    {
        await db.Entry(user).Reference(x => x.Customer).LoadAsync(ct);
        if (user.Customer is not null)
        {
            await db.Entry(user.Customer).Reference(x => x.SalesUser).LoadAsync(ct);
        }
    }

    private static bool IsDefaultSales(AppUser user) =>
        string.Equals(user.DisplayName, AuthService.DefaultSalesName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(user.Email, AuthService.DefaultSalesEmail, StringComparison.OrdinalIgnoreCase);

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
