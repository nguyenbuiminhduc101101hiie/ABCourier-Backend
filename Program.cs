using System.Text;
using CourierApi.Data;
using CourierApi.Domain;
using CourierApi.Hubs;
using CourierApi.Options;
using CourierApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// QuestPDF Community license: miễn phí khi doanh thu tổ chức < 1 triệu USD/năm.
// Xem https://www.questpdf.com/license/ nếu vượt ngưỡng này.
QuestPDF.Settings.License = LicenseType.Community;
RegisterPdfFonts(builder.Environment.ContentRootPath);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.Configure<PublicApiOptions>(builder.Configuration.GetSection(PublicApiOptions.SectionName));

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.UseCompatibilityLevel(120)));

builder.Services.AddSingleton<FedExRateWorkbookParser>();
builder.Services.AddSingleton<DhlRateWorkbookParser>();
builder.Services.AddSingleton<RateWorkbookParserResolver>();
builder.Services.AddSingleton<RateImportPreviewStore>();
builder.Services.AddSingleton<CloneJobStore>();
builder.Services.AddScoped<RateImportApplicationService>();
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<SmtpEmailSender>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<PlatformDataService>();
builder.Services.AddScoped<BookingPdfService>();
builder.Services.AddScoped<RateQuoteService>();
builder.Services.AddScoped<ScaleImportService>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddHttpClient<IGeocodingService, GeocodingService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

var allowedCorsHosts = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()?
    .Select(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) ? uri.Host : null)
    .OfType<string>()
    .ToHashSet(StringComparer.OrdinalIgnoreCase)
    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Flutter", policy =>
        policy.SetIsOriginAllowed(IsAllowedCorsOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    var origin = context.Request.Headers.Origin.ToString();
    var corsOk = IsAllowedCorsOrigin(origin);

    if (corsOk)
    {
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey("Access-Control-Allow-Origin"))
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = origin;
                context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
                context.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,PUT,DELETE,OPTIONS,PATCH";
                context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type,Authorization";
                context.Response.Headers["Vary"] = "Origin";
            }
            return Task.CompletedTask;
        });
    }

    if (HttpMethods.IsOptions(context.Request.Method))
    {
        if (corsOk)
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,PUT,DELETE,OPTIONS,PATCH";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type,Authorization";
            context.Response.Headers["Access-Control-Max-Age"] = "86400";
            context.Response.Headers["Vary"] = "Origin";
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        }

        return;
    }

    await next();
});

app.UseExceptionHandler(errApp =>
{
    errApp.Run(async context =>
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (IsAllowedCorsOrigin(origin) &&
            !context.Response.Headers.ContainsKey("Access-Control-Allow-Origin"))
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            context.Response.Headers["Vary"] = "Origin";
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        var ex = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        await context.Response.WriteAsJsonAsync(new
        {
            message = ex?.InnerException?.Message ?? ex?.Message ?? "Server error"
        });
    });
});

app.UseSwagger();
app.UseSwaggerUI();

app.UseRouting();
app.UseCors("Flutter");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<BookingNotificationHub>("/hubs/bookings");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    if (!await db.Database.CanConnectAsync())
    {
        log.LogError("Cannot connect to DefaultConnection. API still starts; login will fail until SQL is configured on the IIS server.");
    }
    else
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        await db.Database.OpenConnectionAsync();
        cmd.CommandText = "SELECT OBJECT_ID('dbo.Bookings_ABCourier', 'U')";
        var id = await cmd.ExecuteScalarAsync();
        if (id is null || id == DBNull.Value)
        {
            log.LogError("Missing dbo.Bookings_ABCourier. Run Database/005_create_ABCourier_tables_on_ABfreight.sql.");
        }
        else
        {
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
            await auth.EnsureSeedAsync();
            var resets = scope.ServiceProvider.GetRequiredService<PasswordResetService>();
            await resets.EnsureTableAsync();
            var rates = scope.ServiceProvider.GetRequiredService<RateImportApplicationService>();
            await rates.EnsureCustomerColumnAsync();
            await rates.EnsureSalesUserColumnAsync();
            var scales = scope.ServiceProvider.GetRequiredService<ScaleImportService>();
            await scales.EnsureTablesAsync();
            var platform = scope.ServiceProvider.GetRequiredService<PlatformDataService>();
            await platform.EnsureAwbUniqueIndexAsync();
            await platform.EnsureBookingCarrierColumnAsync();
            await platform.EnsureBookingCountryColumnsAsync();
            await platform.EnsurePickupCaptureSchemaAsync();
        }
    }
}
catch (Exception ex)
{
    var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    log.LogError(ex, "Startup database check failed. API will still listen.");
}

app.Run();

static void RegisterPdfFonts(string contentRootPath)
{
    var fontsDir = Path.Combine(contentRootPath, "Fonts");
    if (!Directory.Exists(fontsDir)) return;

    foreach (var file in Directory.EnumerateFiles(fontsDir, "*.ttf"))
    {
        using var stream = File.OpenRead(file);
        QuestPDF.Drawing.FontManager.RegisterFontWithCustomName("Be Vietnam Pro", stream);
    }
}

bool IsAllowedCorsOrigin(string origin)
{
    if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        return false;

    if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        return true;

    return allowedCorsHosts.Contains(uri.Host);
}
