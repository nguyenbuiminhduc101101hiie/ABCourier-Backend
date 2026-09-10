using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CourierApi.Contracts;
using CourierApi.Data;
using CourierApi.Domain;
using CourierApi.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CourierApi.Services;

public sealed class AuthService(
    AppDbContext db,
    IOptions<JwtOptions> jwtOptions,
    IPasswordHasher<AppUser> passwordHasher)
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public const string DefaultSalesName = "NOMI";
    public const string DefaultSalesEmail = "nomi@lms.vn";

    public async Task EnsureSeedAsync(CancellationToken ct = default)
    {
        await EnsureCustomerSalesColumnAsync(ct);

        var customer = await db.Customers.FirstOrDefaultAsync(x => x.CustomerCode == "ABC", ct);
        if (customer is null)
        {
            customer = new Customer
            {
                Id = Guid.NewGuid(),
                CustomerCode = "ABC",
                CompanyName = "ABC Trading",
                BillingEmail = "customer@abc.com",
                CreditLimit = 500_000_000m,
                BillingCycle = "Monthly",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Customers.Add(customer);
            await db.SaveChangesAsync(ct);
        }

        var nomi = await UpsertUserAsync(DefaultSalesEmail, DefaultSalesName, "Sales", null, "Demo@123", ct);
        await UpsertUserAsync("customer@abc.com", "Customer ABC", "Customer", customer.Id, "Demo@123", ct);
        await UpsertUserAsync("staff@lms.vn", "Staff LMS", "Staff", null, "Demo@123", ct);
        await UpsertUserAsync("admin@lms.vn", "Admin LMS", "Admin,RateHub", null, "Demo@123", ct);
        await UpsertUserAsync("sales@lms.vn", "Sales LMS", "Sales", null, "Demo@123", ct);
        await UpsertUserAsync("ratehub@lms.vn", "Rate Hub", "RateHub", null, "Demo@123", ct);

        var unassigned = await db.Customers.Where(x => x.SalesUserId == null).ToListAsync(ct);
        foreach (var item in unassigned)
        {
            item.SalesUserId = nomi.Id;
            item.UpdatedAt = DateTime.UtcNow;
        }

        if (unassigned.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task EnsureCustomerSalesColumnAsync(CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.Customers_ABCourier', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Customers_ABCourier', N'SalesUserId') IS NULL
            BEGIN
                ALTER TABLE dbo.Customers_ABCourier ADD SalesUserId uniqueidentifier NULL;
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.Customers_ABCourier', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Customers_ABCourier', N'SalesUserId') IS NOT NULL
               AND OBJECT_ID(N'dbo.Users_ABCourier', N'U') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Customers_ABCourier_SalesUser'
               )
            BEGIN
                ALTER TABLE dbo.Customers_ABCourier
                    ADD CONSTRAINT FK_Customers_ABCourier_SalesUser
                        FOREIGN KEY (SalesUserId) REFERENCES dbo.Users_ABCourier(Id);
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.Customers_ABCourier', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Customers_ABCourier', N'SalesUserId') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_ABCourier_SalesUserId'
               )
            BEGIN
                CREATE INDEX IX_Customers_ABCourier_SalesUserId
                    ON dbo.Customers_ABCourier (SalesUserId);
            END
            """, ct);
    }

    public async Task<Guid> ResolveSalesUserIdAsync(Guid? requested, CancellationToken ct = default)
    {
        if (requested.HasValue)
        {
            var sales = await db.Users.FirstOrDefaultAsync(x => x.Id == requested.Value && x.IsActive, ct);
            if (sales is not null && HasDepartment(sales.Department, "Sales"))
            {
                return sales.Id;
            }
        }

        var nomi = await GetOrCreateDefaultSalesAsync(ct);
        return nomi.Id;
    }

    public async Task AssignCustomerSalesAsync(Customer customer, Guid? requestedSalesUserId, CancellationToken ct = default)
    {
        customer.SalesUserId = await ResolveSalesUserIdAsync(requestedSalesUserId, ct);
        customer.UpdatedAt = DateTime.UtcNow;
    }

    public async Task<AppUser> GetOrCreateDefaultSalesAsync(CancellationToken ct = default)
        => await UpsertUserAsync(DefaultSalesEmail, DefaultSalesName, "Sales", null, "Demo@123", ct);

    private async Task<AppUser> UpsertUserAsync(
        string email,
        string displayName,
        string department,
        Guid? customerId,
        string password,
        CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                DisplayName = displayName,
                Department = NormalizeStoredDepartments(department),
                CustomerId = customerId,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            db.Users.Add(user);
        }
        else
        {
            user.DisplayName = displayName;
            user.Department = MergeDepartments(user.Department, department);
            if (customerId.HasValue) user.CustomerId = customerId;
            user.IsActive = true;
            if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
                passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
            {
                user.PasswordHash = passwordHasher.HashPassword(user, password);
            }
        }

        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<IReadOnlyList<LoginAccountDto>> ListLoginAccountsAsync(
        string department,
        CancellationToken ct = default)
    {
        var normalized = NormalizeDepartment(department);
        if (normalized is null) return [];

        var users = await db.Users.AsNoTracking()
            .Include(x => x.Customer)
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        return users
            .Where(x => HasDepartment(x.Department, normalized))
            .OrderByDescending(x => x.LastLoginAtUtc ?? DateTime.MinValue)
            .ThenBy(x => x.DisplayName)
            .ThenBy(x => x.Email)
            .Select(x => new LoginAccountDto(
                x.Email,
                x.DisplayName,
                x.Customer?.CompanyName))
            .ToList();
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var department = NormalizeDepartment(request.Department);
        if (department is null) return null;

        var email = request.Email.Trim();
        var user = await db.Users
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Email == email && x.IsActive, ct);

        if (user is null) return null;
        if (!HasDepartment(user.Department, department)) return null;

        var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verify == PasswordVerificationResult.Failed) return null;

        user.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var departments = ParseDepartments(user.Department);
        var expires = DateTime.UtcNow.AddMinutes(_jwt.ExpireMinutes);
        var token = CreateToken(user, department, departments, expires);
        return new LoginResponse(
            token,
            user.Id,
            user.Email,
            user.DisplayName,
            department,
            departments,
            user.CustomerId,
            user.Customer?.CompanyName,
            expires);
    }

    public static string? NormalizeDepartment(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "customer" => "Customer",
        "staff" => "Staff",
        "admin" => "Admin",
        "sales" => "Sales",
        "ratehub" or "rate_hub" or "rate-hub" => "RateHub",
        _ => null
    };

    public static IReadOnlyList<string> ParseDepartments(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return [];
        return stored
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeDepartment)
            .Where(x => x is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool HasDepartment(string? stored, string required)
    {
        var requiredNormalized = NormalizeDepartment(required);
        if (requiredNormalized is null) return false;
        return ParseDepartments(stored)
            .Any(x => string.Equals(x, requiredNormalized, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeStoredDepartments(string? stored)
        => string.Join(',', ParseDepartments(stored));

    public static string MergeDepartments(string? existing, string? add)
    {
        var merged = ParseDepartments(existing)
            .Concat(ParseDepartments(add))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return string.Join(',', merged);
    }

    private string CreateToken(
        AppUser user,
        string activeDepartment,
        IReadOnlyList<string> departments,
        DateTime expires)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            new("department", activeDepartment),
            new("departments", string.Join(',', departments)),
        };
        foreach (var role in departments)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        if (user.CustomerId.HasValue)
        {
            claims.Add(new Claim("customerId", user.CustomerId.Value.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
