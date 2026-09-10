using CourierApi.Contracts;
using CourierApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourierApi.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
public sealed class AuthController(AuthService authService, PasswordResetService passwordReset) : ControllerBase
{
    [HttpGet("accounts")]
    public async Task<ActionResult<IReadOnlyList<LoginAccountDto>>> Accounts(
        [FromQuery] string? department,
        CancellationToken ct)
    {
        if (AuthService.NormalizeDepartment(department) is null)
        {
            return BadRequest(new { message = "Department phải là Customer, Staff, Admin, Sales hoặc RateHub." });
        }

        try
        {
            var items = await authService.ListLoginAccountsAsync(department!, ct);
            return Ok(items);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Không tải được danh sách tài khoản. Kiểm tra kết nối SQL Server.",
                detail = ex.InnerException?.Message ?? ex.Message
            });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Email và mật khẩu là bắt buộc." });
        }

        if (AuthService.NormalizeDepartment(request.Department) is null)
        {
            return BadRequest(new { message = "Department phải là Customer, Staff, Admin, Sales hoặc RateHub." });
        }

        try
        {
            var result = await authService.LoginAsync(request, ct);
            if (result is null)
            {
                return Unauthorized(new { message = "Sai email/mật khẩu hoặc không đúng bộ phận." });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Không kết nối được SQL Server. Kiểm tra ConnectionStrings trên IIS (không dùng Trusted_Connection của máy dev).",
                detail = ex.InnerException?.Message ?? ex.Message
            });
        }
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var result = await passwordReset.RequestOtpAsync(request.Email ?? "", request.Department ?? "", ct);
        return StatusCode(result.Status, new { message = result.Message });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await passwordReset.ResetPasswordAsync(
            request.Email ?? "",
            request.Department ?? "",
            request.Otp ?? "",
            request.NewPassword ?? "",
            ct);
        return StatusCode(result.Status, new { message = result.Message });
    }
}
