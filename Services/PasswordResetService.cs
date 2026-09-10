using System.Security.Cryptography;
using System.Text;
using CourierApi.Data;
using CourierApi.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CourierApi.Services;

public sealed class PasswordResetService(
    AppDbContext db,
    SmtpEmailSender emailSender,
    IPasswordHasher<AppUser> passwordHasher,
    ILogger<PasswordResetService> log)
{
    private const int OtpMinutes = 10;
    private const int MaxAttempts = 5;
    private const int ResendCooldownSeconds = 60;
    private const int MinPasswordLength = 6;

    public async Task EnsureTableAsync(CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.PasswordResetOtps_ABCourier', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PasswordResetOtps_ABCourier
                (
                    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PasswordResetOtps_ABCourier PRIMARY KEY,
                    Email NVARCHAR(256) NOT NULL,
                    Department NVARCHAR(50) NOT NULL,
                    OtpHash NVARCHAR(128) NOT NULL,
                    ExpiresAtUtc DATETIME2 NOT NULL,
                    AttemptCount INT NOT NULL CONSTRAINT DF_PasswordResetOtps_AttemptCount DEFAULT(0),
                    UsedAtUtc DATETIME2 NULL,
                    CreatedAtUtc DATETIME2 NOT NULL
                );
                CREATE INDEX IX_PasswordResetOtps_ABCourier_EmailDept
                    ON dbo.PasswordResetOtps_ABCourier (Email, Department, CreatedAtUtc);
            END
            """);
    }

    public async Task<(int Status, string Message)> RequestOtpAsync(
        string email,
        string department,
        CancellationToken ct = default)
    {
        var normalizedEmail = email.Trim();
        var normalizedDept = AuthService.NormalizeDepartment(department);
        if (!normalizedEmail.Contains('@') || normalizedDept is null)
            return (400, "Email và bộ phận đăng nhập không hợp lệ.");

        if (!emailSender.IsConfigured)
            return (500, "Máy chủ chưa cấu hình SMTP, không gửi được OTP.");

        var user = await db.Users.FirstOrDefaultAsync(
            x => x.Email == normalizedEmail && x.IsActive, ct);
        if (user is null || !AuthService.HasDepartment(user.Department, normalizedDept))
            return (400, "Không tìm thấy tài khoản với email và cổng đăng nhập này.");

        var latest = await db.PasswordResetOtps
            .Where(x => x.Email == normalizedEmail && x.Department == normalizedDept && x.UsedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (latest is not null && latest.CreatedAtUtc.AddSeconds(ResendCooldownSeconds) > DateTime.UtcNow)
            return (429, $"Vui lòng đợi {ResendCooldownSeconds} giây trước khi gửi lại OTP.");

        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.PasswordResetOtps.Add(new PasswordResetOtp
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            Department = normalizedDept,
            OtpHash = HashOtp(normalizedEmail, normalizedDept, otp),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(OtpMinutes),
            AttemptCount = 0,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        var portal = PortalLabel(normalizedDept);
        try
        {
            await emailSender.SendHtmlAsync(
                normalizedEmail,
                $"[{portal}] Mã OTP đặt lại mật khẩu",
                BuildEmailHtml(user.DisplayName, otp, portal),
                ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to send password reset OTP to {Email}", normalizedEmail);
            return (500, "Không gửi được email OTP. Kiểm tra cấu hình SMTP hoặc thử lại.");
        }

        return (200, $"Đã gửi mã OTP đến {normalizedEmail}. Mã có hiệu lực {OtpMinutes} phút.");
    }

    public async Task<(int Status, string Message)> ResetPasswordAsync(
        string email,
        string department,
        string otp,
        string newPassword,
        CancellationToken ct = default)
    {
        var normalizedEmail = email.Trim();
        var normalizedDept = AuthService.NormalizeDepartment(department);
        var code = (otp ?? "").Trim();
        if (!normalizedEmail.Contains('@') || normalizedDept is null || code.Length != 6)
            return (400, "Email, bộ phận hoặc mã OTP không hợp lệ.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < MinPasswordLength)
            return (400, $"Mật khẩu mới tối thiểu {MinPasswordLength} ký tự.");

        var user = await db.Users.FirstOrDefaultAsync(
            x => x.Email == normalizedEmail && x.IsActive, ct);
        if (user is null || !AuthService.HasDepartment(user.Department, normalizedDept))
            return (400, "Không tìm thấy tài khoản với email và cổng đăng nhập này.");

        var record = await db.PasswordResetOtps
            .Where(x => x.Email == normalizedEmail && x.Department == normalizedDept && x.UsedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (record is null)
            return (400, "Chưa có mã OTP. Hãy bấm gửi OTP trước.");
        if (record.ExpiresAtUtc < DateTime.UtcNow)
            return (400, "Mã OTP đã hết hạn. Hãy gửi lại mã mới.");
        if (record.AttemptCount >= MaxAttempts)
            return (400, "Đã nhập sai OTP quá nhiều lần. Hãy gửi lại mã mới.");

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(record.OtpHash),
                Encoding.UTF8.GetBytes(HashOtp(normalizedEmail, normalizedDept, code))))
        {
            record.AttemptCount += 1;
            await db.SaveChangesAsync(ct);
            return (400, "Mã OTP không đúng.");
        }

        record.UsedAtUtc = DateTime.UtcNow;
        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        await db.SaveChangesAsync(ct);
        return (200, "Đặt lại mật khẩu thành công. Hãy đăng nhập bằng mật khẩu mới.");
    }

    private static string HashOtp(string email, string department, string otp)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{email}|{department}|{otp}"));
        return Convert.ToHexString(bytes);
    }

    private static string PortalLabel(string department) => department switch
    {
        "Customer" => "Khách hàng",
        "Staff" => "Nhân viên",
        "Admin" => "Admin",
        "Sales" => "Sales",
        "RateHub" => "Rate Hub",
        _ => department,
    };

    private static string BuildEmailHtml(string displayName, string otp, string portal) =>
        $"""
        <div style="font-family:Segoe UI,Arial,sans-serif;max-width:520px;margin:0 auto;color:#1f2937">
          <h2 style="color:#4D148C;margin-bottom:8px">Đặt lại mật khẩu AB Courier</h2>
          <p>Xin chào <strong>{WebEncode(displayName)}</strong>,</p>
          <p>Bạn đang yêu cầu đặt lại mật khẩu cho cổng <strong>{WebEncode(portal)}</strong>.</p>
          <p style="font-size:15px">Mã OTP của bạn:</p>
          <p style="font-size:32px;letter-spacing:8px;font-weight:800;color:#4D148C;margin:12px 0">{otp}</p>
          <p>Mã có hiệu lực trong <strong>{OtpMinutes} phút</strong>. Không chia sẻ mã này với người khác.</p>
          <p style="color:#6b7280;font-size:13px">Nếu bạn không yêu cầu, hãy bỏ qua email này.</p>
        </div>
        """;

    private static string WebEncode(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
