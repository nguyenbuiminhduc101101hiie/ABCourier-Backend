# CourierApi — Backend API cho nền tảng chuyển phát nhanh ABCourier

> REST API ASP.NET Core 8 phục vụ ứng dụng ABCourier: xác thực người dùng, import & tra cứu bảng giá FedEx/DHL, booking, job/AWB, task lấy hàng của nhân viên, hóa đơn và thông báo realtime.

Đây là **backend** của hệ thống LMS Courier. Frontend Flutter (khách hàng / nhân viên / web công ty / Sales / Rate Hub) nằm ở repo [ABCourier](https://github.com/duclogisticssoftware/ABCourier).

## 1. Chức năng

| Nhóm | Mô tả |
|---|---|
| **Xác thực** | Đăng nhập trả JWT, quên mật khẩu bằng OTP qua email, 4 vai trò: `Admin`, `Staff`, `Sales`, `Customer` |
| **Rate Import** | Upload file Excel bảng giá của hãng → **Preview → Validate → Commit**; parser riêng cho **FedEx** và **DHL Express**; quản lý batch giá (version, ngày hiệu lực, hash file), gán bảng giá cho khách / Sales, nhân bản, xóa |
| **Tra giá** | Tính giá theo quốc gia, dịch vụ, trọng lượng tính cước + phụ phí |
| **Phụ phí** | CRUD phụ phí theo batch giá |
| **Booking** | Khách tạo booking, báo giá booking, cập nhật chi tiết/trạng thái; tự chọn hãng vận chuyển |
| **Job / AWB** | Tạo job từ booking, gắn số AWB (duy nhất) |
| **Pickup** | Task lấy hàng cho nhân viên, chỉ đường (Goong), xác nhận lấy hàng kèm ảnh/chữ ký, đồng bộ dữ liệu offline |
| **Billing** | Danh sách và tạo hóa đơn, dashboard tổng quan |
| **Danh mục** | Khách hàng, người dùng (nhiều phòng ban), thông tin công ty, import bảng cân |
| **Realtime** | SignalR hub `/hubs/bookings` — đẩy thông báo booking mới cho web công ty |

## 2. Công nghệ

| Hạng mục | Công nghệ |
|---|---|
| Nền tảng | **ASP.NET Core 8** Web API |
| Database | **SQL Server**, Entity Framework Core 8 |
| Bảo mật | JWT Bearer, ASP.NET Identity password hasher |
| Excel | ClosedXML, ExcelDataReader |
| Bản đồ | Goong API (geocoding & directions) |
| Email | SMTP |
| Tài liệu API | Swagger / Swashbuckle |
| Triển khai | IIS (`web.config`) hoặc Docker |

## 3. API chính

```text
POST  /api/auth/login | forgot-password | reset-password
GET   /api/platform/dashboard
GET   /api/platform/bookings            POST /api/platform/bookings     POST /api/platform/bookings/quote
PATCH /api/platform/bookings/{id}       PUT  /api/platform/bookings/{id}/details
GET   /api/platform/jobs                POST /api/platform/jobs         PATCH /api/platform/jobs/{id}/awb
GET   /api/platform/pickup-tasks        GET  /api/platform/pickup-tasks/{no}/directions
POST  /api/platform/pickup-tasks/{no}/confirm                           POST /api/platform/pickup-tasks/sync
GET   /api/platform/invoices            POST /api/platform/invoices
GET   /api/platform/rate-tables         GET  /api/platform/rate-countries
POST  /api/rate-imports/preview | commit | clone      GET /api/rate-imports/history
GET   /api/rates/lookup | countries
CRUD  /api/surcharges, /api/customers, /api/users, /api/company-information
HUB   /hubs/bookings
```

Xem đầy đủ tại Swagger: `http://localhost:5088/swagger`.

## 4. Cấu trúc mã nguồn

```text
Controllers/   Auth, Platform, RateImports, Rates, Surcharges, Customers, Users, CompanyInformation
Services/      Nghiệp vụ: FedEx/DHL workbook parser, RateImportApplicationService, RateQuoteService,
               PlatformDataService, ScaleImportService, Geocoding, PasswordReset, SMTP
Domain/        Entity: RateEntities, PlatformEntities
Contracts/     DTO request/response
Data/          AppDbContext (EF Core)
Database/      Script SQL tạo & nâng cấp schema, chạy theo thứ tự số (000 → 018)
Hubs/          BookingNotificationHub (SignalR)
Options/       JwtOptions, SmtpOptions
```

## 5. Chạy dự án

Yêu cầu: .NET 8 SDK, SQL Server.

1. Tạo database và chạy lần lượt các script trong `Database/` theo số thứ tự.
2. Cấu hình `appsettings.json` (hoặc `appsettings.Local.json`):
   - `ConnectionStrings:DefaultConnection`
   - `Jwt` (`Issuer`, `Audience`, `Key`)
   - `Cors` — origin của frontend
   - `Smtp` — gửi OTP quên mật khẩu
   - `Geocoding:GoongApiKey` — chỉ đường cho nhân viên

   > ⚠️ Không commit khóa JWT, mật khẩu, API key thật lên git.

3. Chạy:

   ```bash
   dotnet restore
   dotnet run
   ```

   API chạy tại <http://localhost:5088>.

### Docker

```bash
docker build -t courier-api .
docker run -p 5088:5088 -e ConnectionStrings__DefaultConnection="..." courier-api
```

## 6. Publish lên IIS

```bash
dotnet publish -c Release -o ./publish
```

Chép `publish/` lên IIS; cấp quyền database cho App Pool (xem `Database/008_grant_iis_apppool.sql`).
