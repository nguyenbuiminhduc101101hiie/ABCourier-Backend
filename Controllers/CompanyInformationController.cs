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
[Route("api/company-information")]
public sealed class CompanyInformationController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CompanyInformationDto>> Get(CancellationToken ct)
    {
        if (!IsAdmin()) return StatusCode(403, new { message = "Cần quyền Admin." });

        var info = await db.CompanyInformation.AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return Ok(info is null ? EmptyDto() : ToDto(info));
    }

    [HttpPut]
    public async Task<ActionResult<CompanyInformationDto>> Update(
        [FromBody] UpsertCompanyInformationRequest request,
        CancellationToken ct)
    {
        if (!IsAdmin()) return StatusCode(403, new { message = "Cần quyền Admin." });

        var error = Validate(request);
        if (error is not null) return BadRequest(new { message = error });

        var now = DateTime.UtcNow;
        var info = await db.CompanyInformation.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (info is null)
        {
            info = new CompanyInformation { Id = Guid.NewGuid(), CreatedAt = now };
            db.CompanyInformation.Add(info);
        }

        info.CompanyName = request.CompanyName.Trim();
        info.TaxCode = TrimOrNull(request.TaxCode);
        info.Address = TrimOrNull(request.Address);
        info.Phone = TrimOrNull(request.Phone);
        info.Email = TrimOrNull(request.Email);
        info.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(info));
    }

    private bool IsAdmin()
    {
        var active = User.FindFirstValue("department");
        if (string.Equals(active, "Admin", StringComparison.OrdinalIgnoreCase)) return true;
        var all = User.FindFirstValue("departments") ?? "";
        return AuthService.HasDepartment(all, "Admin") || User.IsInRole("Admin");
    }

    private static CompanyInformationDto EmptyDto() => new(null, "", null, null, null, null, null);

    private static CompanyInformationDto ToDto(CompanyInformation x) => new(
        x.Id,
        x.CompanyName,
        x.TaxCode,
        x.Address,
        x.Phone,
        x.Email,
        x.UpdatedAt);

    private static string? Validate(UpsertCompanyInformationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName))
            return "Tên công ty là bắt buộc.";
        var email = TrimOrNull(request.Email);
        if (email is not null && !email.Contains('@'))
            return "Email không hợp lệ.";
        return null;
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
