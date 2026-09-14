using CourierApi.Contracts;
using CourierApi.Data;
using CourierApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CourierApi.Services;

public sealed class PlatformDataService(AppDbContext db, RateQuoteService quotes, IWebHostEnvironment env, IGeocodingService geocoding)
{
    public async Task<DirectionsRouteDto?> GetPickupDirectionsAsync(string pickupNo, CancellationToken ct = default)
    {
        var task = await db.PickupTasks
            .Include(x => x.Booking).ThenInclude(b => b.Customer)
            .FirstOrDefaultAsync(x => x.PickupNo == pickupNo, ct);
        if (task is null) return null;

        var destinationAddress = task.Booking.Customer.Address?.Trim();
        if (string.IsNullOrEmpty(destinationAddress))
        {
            throw new InvalidOperationException("Khách hàng chưa có địa chỉ.");
        }

        var companyInfo = await db.CompanyInformation.AsNoTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        var originAddress = companyInfo?.Address?.Trim();
        if (string.IsNullOrEmpty(originAddress))
        {
            throw new InvalidOperationException("Chưa cấu hình địa chỉ công ty.");
        }

        var originGeo = await geocoding.GeocodeAddressAsync(originAddress, ct)
            ?? throw new InvalidOperationException("Không xác định được vị trí địa chỉ công ty.");
        var destGeo = await geocoding.GeocodeAddressAsync(destinationAddress, ct)
            ?? throw new InvalidOperationException("Không xác định được vị trí địa chỉ khách hàng.");

        var directions = await geocoding.GetDirectionsAsync(originGeo.Lat, originGeo.Lng, destGeo.Lat, destGeo.Lng, ct)
            ?? throw new InvalidOperationException("Không tìm được tuyến đường.");

        return new DirectionsRouteDto(
            originGeo.Lat,
            originGeo.Lng,
            originAddress,
            destGeo.Lat,
            destGeo.Lng,
            destinationAddress,
            directions.Points.Select(p => new GeoPointDto(p.Lat, p.Lng)).ToList(),
            directions.DistanceMeters,
            directions.DurationSeconds);
    }

    public async Task SaveCourierLocationAsync(string pickupNo, SaveCourierLocationRequest request, Guid? userId, CancellationToken ct = default)
    {
        if (userId is null) throw new InvalidOperationException("Không xác định được nhân viên đăng nhập.");

        var task = await db.PickupTasks.FirstOrDefaultAsync(x => x.PickupNo == pickupNo, ct)
            ?? throw new InvalidOperationException("Không tìm thấy pickup task.");

        db.CourierLocationTracks.Add(new CourierLocationTrack
        {
            Id = Guid.NewGuid(),
            PickupTaskId = task.Id,
            UserId = userId.Value,
            Latitude = (decimal)request.Latitude,
            Longitude = (decimal)request.Longitude,
            RecordedAtUtc = request.RecordedAtUtc ?? DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<DashboardDto> GetDashboardAsync(Guid? customerId, CancellationToken ct = default, Guid? salesUserId = null)
    {
        var bookingsQuery = db.Bookings.AsQueryable();
        var jobsQuery = db.Jobs.Include(x => x.ChargeLines).AsQueryable();
        if (customerId.HasValue)
        {
            bookingsQuery = bookingsQuery.Where(x => x.CustomerId == customerId);
            jobsQuery = jobsQuery.Where(x => x.CustomerId == customerId);
        }
        if (salesUserId.HasValue)
        {
            bookingsQuery = bookingsQuery.Where(x => x.Customer.SalesUserId == salesUserId);
            jobsQuery = jobsQuery.Where(x => x.Customer.SalesUserId == salesUserId);
        }

        var bookings = await bookingsQuery.ToListAsync(ct);
        var jobs = await jobsQuery.ToListAsync(ct);

        var revenue = jobs.Sum(j => j.ChargeLines.Where(c => c.ChargeKind == "Debit").Sum(c => c.Amount));
        var cost = jobs.Sum(j => j.ChargeLines.Where(c => c.ChargeKind == "Credit").Sum(c => c.Amount));
        var unbilled = jobs.Where(j => j.BillingStatus == "Ready")
            .Sum(j => j.ChargeLines.Where(c => c.ChargeKind == "Debit").Sum(c => c.Amount));

        return new DashboardDto(
            bookings.Count(x => x.Status is "Submitted" or "New" or "Review"),
            bookings.Count(x => x.Status == "Review"),
            jobs.Count(x => x.Status == "Exception"),
            unbilled,
            revenue,
            cost,
            revenue - cost);
    }

    public async Task<IReadOnlyList<BookingSummaryDto>> GetBookingsAsync(Guid? customerId, CancellationToken ct = default, Guid? salesUserId = null)
    {
        var query = db.Bookings
            .Include(x => x.Customer)
            .Include(x => x.Packages)
            .Include(x => x.Quotes)
            .Include(x => x.Documents)
            .AsQueryable();
        if (customerId.HasValue) query = query.Where(x => x.CustomerId == customerId);
        if (salesUserId.HasValue) query = query.Where(x => x.Customer.SalesUserId == salesUserId);

        var rows = await query.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct);
        return rows.Select(MapBooking).ToList();
    }

    public async Task<IReadOnlyList<JobSummaryDto>> GetJobsAsync(Guid? customerId, CancellationToken ct = default, Guid? salesUserId = null)
    {
        var query = db.Jobs
            .Include(x => x.Customer)
            .Include(x => x.Booking)
            .Include(x => x.ChargeLines)
            .AsQueryable();
        if (customerId.HasValue) query = query.Where(x => x.CustomerId == customerId);
        if (salesUserId.HasValue) query = query.Where(x => x.Customer.SalesUserId == salesUserId);

        var rows = await query.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct);
        return rows.Select(MapJob).ToList();
    }

    public async Task<IReadOnlyList<PickupTaskDto>> GetPickupTasksAsync(
        Guid? assignedToUserId,
        CancellationToken ct = default)
    {
        var query = db.PickupTasks
            .Include(x => x.Booking).ThenInclude(b => b.Customer)
            .Include(x => x.Booking).ThenInclude(b => b.Packages)
            .Include(x => x.Booking).ThenInclude(b => b.SenderAddress)
            .Include(x => x.Job)
            .Include(x => x.Evidence)
            .Include(x => x.ConfirmationLogs)
            .AsQueryable();
        if (assignedToUserId.HasValue)
        {
            query = query.Where(x => x.AssignedUserId == assignedToUserId);
        }

        var rows = await query
            .OrderByDescending(x => x.ScheduledAt)
            .Take(200)
            .ToListAsync(ct);

        var userIds = rows
            .SelectMany(t =>
            {
                var ids = new List<Guid>();
                if (t.AssignedUserId.HasValue) ids.Add(t.AssignedUserId.Value);
                if (t.LastConfirmedByUserId.HasValue) ids.Add(t.LastConfirmedByUserId.Value);
                ids.AddRange(t.ConfirmationLogs.Where(x => x.CreatedByUserId.HasValue).Select(x => x.CreatedByUserId!.Value));
                return ids;
            })
            .Distinct()
            .ToList();
        var names = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(x => userIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        return rows.Select(t => MapPickupTask(t, names)).ToList();
    }

    public async Task<CreateBookingResult> CreateBookingAsync(
        CreateBookingRequest request,
        Guid? userId,
        Guid? forcedCustomerId,
        CancellationToken ct = default)
    {
        var customer = await ResolveBookingCustomerAsync(request.CustomerName, userId, forcedCustomerId, ct);

        var packageInputs = (request.Packages is { Count: > 0 }
            ? request.Packages
            : [new BookingPackageDto(1, request.ActualWeight, request.Length, request.Width, request.Height, "Package")]).ToList();

        decimal totalDim = 0;
        decimal totalChargeable = 0;
        var packages = new List<BookingPackage>();
        for (var i = 0; i < packageInputs.Count; i++)
        {
            var item = packageInputs[i];
            var pieces = Math.Max(1, item.Pieces);
            var dim = Math.Round(item.Length * item.Width * item.Height / 5000m * pieces, 3);
            var chargeable = RateQuoteService.RoundChargeable(
                Math.Max(item.ActualWeight * pieces, dim),
                item.PackageType,
                request.Service);
            totalDim += dim;
            totalChargeable += chargeable;
            packages.Add(new BookingPackage
            {
                Id = Guid.NewGuid(),
                PackageNo = i + 1,
                PackageType = string.IsNullOrWhiteSpace(item.PackageType) ? "Package" : item.PackageType,
                Pieces = pieces,
                ActualWeight = item.ActualWeight,
                LengthCm = item.Length,
                WidthCm = item.Width,
                HeightCm = item.Height,
                DimWeight = dim,
                ChargeableWeight = chargeable,
                IsStackable = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        var residential = request.Receiver?.Residential == true;
        var rateQuote = await quotes.QuoteAsync(
            customer.Id,
            request.Direction,
            request.Service,
            request.FromCountry,
            request.ToCountry,
            request.ShipDate,
            packageInputs,
            residential,
            request.CarrierCode,
            ct: ct);

        var quotedService = string.IsNullOrWhiteSpace(rateQuote.ServiceCode)
            ? request.Service.Trim().ToUpperInvariant()
            : rateQuote.ServiceCode;
        totalChargeable = 0;
        foreach (var pkg in packages)
        {
            var raw = RateQuoteService.RawChargeable(new BookingPackageDto(
                pkg.Pieces, pkg.ActualWeight, pkg.LengthCm, pkg.WidthCm, pkg.HeightCm, pkg.PackageType));
            pkg.PackageType = RateQuoteService.NormalizePackageType(
                pkg.PackageType, raw, quotedService, rateQuote.CarrierCode);
            pkg.ChargeableWeight = RateQuoteService.RoundChargeable(raw, pkg.PackageType, quotedService);
            totalChargeable += pkg.ChargeableWeight;
        }

        var zone = rateQuote.Zone;
        var freight = rateQuote.Freight;
        var fuel = rateQuote.Fuel;
        var demand = rateQuote.Demand;
        var handling = rateQuote.Handling;
        var vat = rateQuote.Vat;
        var total = rateQuote.Total;
        var needsReview = request.RequiresComplianceReview
            || (request.Commodities?.Any(x => x.ContainsBattery || x.ContainsLiquid || x.IsFood || x.IsDangerousGoods) ?? false)
            || totalChargeable >= 68;
        var status = needsReview ? "Review" : "Submitted";

        var sender = SaveAddress(customer.Id, request.Sender, "Sender", true, false);
        var receiver = SaveAddress(customer.Id, request.Receiver, "Receiver", false, true);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingNo = $"BK{DateTime.UtcNow:yyMMddHHmmss}",
            CustomerId = customer.Id,
            Direction = request.Direction,
            CarrierCode = rateQuote.CarrierCode,
            ServiceCode = quotedService,
            FromCountryCode = request.FromCountry,
            ToCountryCode = request.ToCountry,
            ShipDate = request.ShipDate.Date,
            SenderAddressId = sender?.Id,
            ReceiverAddressId = receiver?.Id,
            Status = status,
            CustomerNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            RequiresComplianceReview = needsReview,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var pkg in packages)
        {
            pkg.BookingId = booking.Id;
            booking.Packages.Add(pkg);
        }

        var commodities = request.Commodities is { Count: > 0 }
            ? request.Commodities
            : string.IsNullOrWhiteSpace(request.CommodityName)
                ? []
                : [new BookingCommodityDto(request.CommodityName, request.CommodityName, 1, 0, "USD", null, false, false, false, false)];
        foreach (var item in commodities)
        {
            booking.Commodities.Add(new BookingCommodity
            {
                Id = Guid.NewGuid(),
                BookingId = booking.Id,
                ItemName = item.ItemName,
                DescriptionEnglish = item.DescriptionEnglish,
                Quantity = item.Quantity,
                UnitValue = item.UnitValue,
                Currency = string.IsNullOrWhiteSpace(item.Currency) ? "USD" : item.Currency,
                HsCode = item.HsCode,
                CountryOfOrigin = item.CountryOfOrigin,
                ContainsBattery = item.ContainsBattery,
                ContainsLiquid = item.ContainsLiquid,
                IsFood = item.IsFood,
                IsDangerousGoods = item.IsDangerousGoods
            });
        }

        foreach (var name in request.Documents ?? [])
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            booking.Documents.Add(new JobDocument
            {
                Id = Guid.NewGuid(),
                BookingId = booking.Id,
                DocumentType = "Attachment",
                FileName = name.Trim(),
                FileUrl = name.Trim(),
                UploadedBy = userId?.ToString(),
                CreatedAt = DateTime.UtcNow
            });
        }

        booking.Quotes.Add(new BookingQuote
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            VersionNo = 1,
            ZoneCode = zone,
            ChargeableWeight = rateQuote.ChargeableWeight,
            Freight = freight,
            FuelSurcharge = fuel,
            DemandSurcharge = demand,
            HandlingCharge = handling,
            VatAmount = vat,
            TotalAmount = total,
            RateCardId = rateQuote.RateCardId,
            CalculationSnapshot = rateQuote.SnapshotJson,
            Status = "Active",
            CreatedAt = DateTime.UtcNow
        });

        db.Bookings.Add(booking);
        await db.SaveChangesAsync(ct);

        return new CreateBookingResult(booking.BookingNo, totalDim, totalChargeable, zone, total, status);
    }

    private CustomerAddress? SaveAddress(Guid customerId, BookingAddressDto? dto, string fallbackName, bool defaultSender, bool defaultReceiver)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Line1) && string.IsNullOrWhiteSpace(dto.Name))
            return null;

        var address = new CustomerAddress
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            AddressName = fallbackName,
            ContactName = string.IsNullOrWhiteSpace(dto.Name) ? fallbackName : dto.Name.Trim(),
            CompanyName = dto.Company,
            Phone = dto.Phone,
            Email = dto.Email,
            AddressLine1 = dto.Line1 ?? "",
            City = dto.City ?? "",
            StateProvince = dto.State,
            PostalCode = dto.PostalCode,
            CountryCode = string.IsNullOrWhiteSpace(dto.CountryCode) ? "VN" : dto.CountryCode,
            IsResidential = dto.Residential,
            IsDefaultSender = defaultSender,
            IsDefaultReceiver = defaultReceiver,
            CreatedAt = DateTime.UtcNow
        };
        db.CustomerAddresses.Add(address);
        return address;
    }

    public async Task<BookingDetailDto?> GetBookingDetailAsync(string bookingIdOrNo, Guid? customerId, CancellationToken ct = default, Guid? salesUserId = null)
    {
        var booking = await LoadBookingAsync(bookingIdOrNo, ct);
        if (booking is null) return null;
        if (customerId.HasValue && booking.CustomerId != customerId.Value) return null;
        if (salesUserId.HasValue && booking.Customer?.SalesUserId != salesUserId) return null;
        return MapBookingDetail(booking);
    }

    public async Task<BookingDetailDto> UpdateBookingDetailsAsync(
        string bookingIdOrNo,
        UpdateBookingDetailsRequest request,
        Guid? customerId,
        Guid? userId,
        CancellationToken ct = default,
        Guid? salesUserId = null)
    {
        var booking = await LoadBookingAsync(bookingIdOrNo, ct)
            ?? throw new InvalidOperationException("Không tìm thấy booking.");
        if (customerId.HasValue && booking.CustomerId != customerId.Value)
            throw new InvalidOperationException("Không có quyền sửa booking này.");
        if (salesUserId.HasValue && booking.Customer?.SalesUserId != salesUserId)
            throw new InvalidOperationException("Không có quyền xem booking này.");
        if (booking.Status is "JobCreated" or "Rejected")
            throw new InvalidOperationException("Booking đã khóa, không bổ sung thông tin được.");

        if (request.Note is not null)
            booking.CustomerNote = request.Note.Trim();

        if (request.Sender is not null && booking.SenderAddressId is null)
        {
            var sender = SaveAddress(booking.CustomerId, request.Sender, "Sender", true, false);
            booking.SenderAddressId = sender?.Id;
        }
        else if (request.Sender is not null && booking.SenderAddress is not null)
        {
            ApplyAddress(booking.SenderAddress, request.Sender);
        }

        if (request.Receiver is not null && booking.ReceiverAddressId is null)
        {
            var receiver = SaveAddress(booking.CustomerId, request.Receiver, "Receiver", false, true);
            booking.ReceiverAddressId = receiver?.Id;
        }
        else if (request.Receiver is not null && booking.ReceiverAddress is not null)
        {
            ApplyAddress(booking.ReceiverAddress, request.Receiver);
        }

        if (request.Commodity is not null)
        {
            booking.Commodities.Add(new BookingCommodity
            {
                Id = Guid.NewGuid(),
                BookingId = booking.Id,
                ItemName = request.Commodity.ItemName,
                DescriptionEnglish = request.Commodity.DescriptionEnglish,
                Quantity = request.Commodity.Quantity,
                UnitValue = request.Commodity.UnitValue,
                Currency = string.IsNullOrWhiteSpace(request.Commodity.Currency) ? "USD" : request.Commodity.Currency,
                HsCode = request.Commodity.HsCode,
                CountryOfOrigin = request.Commodity.CountryOfOrigin,
                ContainsBattery = request.Commodity.ContainsBattery,
                ContainsLiquid = request.Commodity.ContainsLiquid,
                IsFood = request.Commodity.IsFood,
                IsDangerousGoods = request.Commodity.IsDangerousGoods
            });
            if (request.Commodity.ContainsBattery || request.Commodity.ContainsLiquid ||
                request.Commodity.IsFood || request.Commodity.IsDangerousGoods)
            {
                booking.RequiresComplianceReview = true;
                if (booking.Status == "Submitted") booking.Status = "Review";
            }
        }

        foreach (var name in request.Documents ?? [])
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            booking.Documents.Add(new JobDocument
            {
                Id = Guid.NewGuid(),
                BookingId = booking.Id,
                DocumentType = "Attachment",
                FileName = name.Trim(),
                FileUrl = name.Trim(),
                UploadedBy = userId?.ToString(),
                CreatedAt = DateTime.UtcNow
            });
        }

        booking.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return MapBookingDetail(await LoadBookingAsync(booking.BookingNo, ct) ?? booking);
    }

    private async Task<Booking?> LoadBookingAsync(string bookingIdOrNo, CancellationToken ct) =>
        await db.Bookings
            .Include(x => x.Customer)
            .Include(x => x.SenderAddress)
            .Include(x => x.ReceiverAddress)
            .Include(x => x.Packages)
            .Include(x => x.Commodities)
            .Include(x => x.Quotes)
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.BookingNo == bookingIdOrNo || x.Id.ToString() == bookingIdOrNo, ct);

    private static void ApplyAddress(CustomerAddress address, BookingAddressDto dto)
    {
        address.ContactName = dto.Name;
        address.CompanyName = dto.Company;
        address.Phone = dto.Phone;
        address.Email = dto.Email;
        address.AddressLine1 = dto.Line1;
        address.City = dto.City ?? "";
        address.StateProvince = dto.State;
        address.PostalCode = dto.PostalCode;
        address.CountryCode = dto.CountryCode;
        address.IsResidential = dto.Residential;
    }

    private async Task<Customer> ResolveBookingCustomerAsync(
        string? requestedName,
        Guid? userId,
        Guid? forcedCustomerId,
        CancellationToken ct)
    {
        if (forcedCustomerId.HasValue)
        {
            return await db.Customers.FirstAsync(x => x.Id == forcedCustomerId.Value, ct);
        }

        if (userId.HasValue)
        {
            var user = await db.Users.Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == userId.Value, ct);
            if (user?.Customer is not null)
                return user.Customer;
        }

        if (!string.IsNullOrWhiteSpace(requestedName))
        {
            var byName = await db.Customers.FirstOrDefaultAsync(x => x.CompanyName == requestedName, ct);
            if (byName is not null)
                return byName;
        }

        throw new InvalidOperationException(
            "Không xác định được khách hàng cho booking. Tài khoản Customer phải được gắn CustomerId (không mặc định ABC Trading).");
    }

    public async Task<CreateJobResult> CreateJobAsync(
        string bookingIdOrNo,
        Guid? assignedUserId,
        CancellationToken ct = default)
    {
        var booking = await db.Bookings
            .Include(x => x.Packages)
            .Include(x => x.Quotes)
            .Include(x => x.Job)
            .FirstOrDefaultAsync(x => x.BookingNo == bookingIdOrNo || x.Id.ToString() == bookingIdOrNo, ct)
            ?? throw new InvalidOperationException("Không tìm thấy booking.");

        if (booking.Job is not null)
        {
            return new CreateJobResult(booking.Job.JobNo, booking.Job.Awb ?? "", booking.Job.Status);
        }

        if (!assignedUserId.HasValue || assignedUserId == Guid.Empty)
        {
            throw new InvalidOperationException("Hãy chọn nhân viên Staff phụ trách pickup.");
        }

        var assignee = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == assignedUserId.Value, ct)
            ?? throw new InvalidOperationException("Không tìm thấy nhân viên đã chọn.");
        if (!assignee.IsActive)
        {
            throw new InvalidOperationException("Nhân viên đã bị khóa.");
        }
        if (!AuthService.HasDepartment(assignee.Department, "Staff"))
        {
            throw new InvalidOperationException("Người phụ trách phải có quyền Staff.");
        }

        var quote = booking.Quotes.OrderByDescending(x => x.VersionNo).FirstOrDefault();
        var awb = await GenerateUniqueAwbAsync(ct);
        var job = new Job
        {
            Id = Guid.NewGuid(),
            JobNo = $"JOB{DateTime.UtcNow:yyMMddHHmmss}",
            BookingId = booking.Id,
            CustomerId = booking.CustomerId,
            CarrierCode = string.IsNullOrWhiteSpace(booking.CarrierCode)
                ? (TryReadSnapshot(quote?.CalculationSnapshot, "carrierCode") ?? "FEDEX")
                : booking.CarrierCode,
            Direction = booking.Direction,
            ServiceCode = booking.ServiceCode,
            Awb = awb,
            Status = "AwbCreated",
            BillingStatus = "Ready",
            ActualWeight = booking.Packages.Sum(p => p.ActualWeight * p.Pieces),
            ChargeableWeight = booking.Packages.Sum(p => p.ChargeableWeight),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (quote is not null)
        {
            job.ChargeLines.Add(Line(job.Id, "Debit", "FREIGHT", "Cước vận chuyển", quote.Freight));
            job.ChargeLines.Add(Line(job.Id, "Debit", "FUEL", "Phụ phí nhiên liệu", quote.FuelSurcharge));
            job.ChargeLines.Add(Line(job.Id, "Debit", "DEMAND", "Phụ phí nhu cầu", quote.DemandSurcharge));
            job.ChargeLines.Add(Line(job.Id, "Debit", "HANDLING", "Phí xử lý", quote.HandlingCharge));
            job.ChargeLines.Add(Line(job.Id, "Credit", "CARRIER", $"Chi phí {job.CarrierCode} dự kiến",
                Math.Round(quote.Freight * 0.78m + quote.FuelSurcharge * 0.86m, 0)));
            job.ChargeLines.Add(Line(job.Id, "Credit", "PICKUP", "Chi phí lấy hàng", 120000m));
        }

        job.TrackingEvents.Add(new TrackingEvent
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            EventCode = "BOOKED",
            EventTitle = "Booking confirmed",
            Location = "Ho Chi Minh City",
            EventTime = DateTime.UtcNow.AddHours(-4),
            SourceType = "System",
            CreatedAt = DateTime.UtcNow
        });
        job.TrackingEvents.Add(new TrackingEvent
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            EventCode = "AWB",
            EventTitle = "AWB created",
            Location = "SGN Station",
            EventTime = DateTime.UtcNow,
            SourceType = "System",
            CreatedAt = DateTime.UtcNow
        });

        var task = new PickupTask
        {
            Id = Guid.NewGuid(),
            PickupNo = $"PU{DateTime.UtcNow:yyMMddHHmmss}",
            JobId = job.Id,
            BookingId = booking.Id,
            AssignedUserId = assignedUserId,
            ScheduledAt = booking.ShipDate,
            Status = "Assigned",
            ExpectedPieces = booking.Packages.Sum(p => p.Pieces),
            CreatedAt = DateTime.UtcNow
        };

        booking.Status = "JobCreated";
        booking.UpdatedAt = DateTime.UtcNow;
        db.Jobs.Add(job);
        db.PickupTasks.Add(task);
        await db.SaveChangesAsync(ct);
        return new CreateJobResult(job.JobNo, awb, job.Status);
    }

    public async Task<CreateJobResult> UpdateJobAwbAsync(string jobIdOrNo, string awb, CancellationToken ct = default)
    {
        var normalized = NormalizeAwb(awb);
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("Số vận đơn (AWB) không được để trống.");
        }

        var job = await db.Jobs.FirstOrDefaultAsync(
            x => x.JobNo == jobIdOrNo || x.Id.ToString() == jobIdOrNo, ct)
            ?? throw new InvalidOperationException("Không tìm thấy job.");

        if (string.Equals(NormalizeAwb(job.Awb ?? ""), normalized, StringComparison.OrdinalIgnoreCase))
        {
            return new CreateJobResult(job.JobNo, job.Awb ?? normalized, job.Status);
        }

        if (await AwbTakenAsync(normalized, job.Id, ct))
        {
            throw new InvalidOperationException($"Số vận đơn {normalized} đã tồn tại trên job khác.");
        }

        job.Awb = normalized;
        job.UpdatedAt = DateTime.UtcNow;

        var scaleLines = await db.PackageScaleLines.Where(x => x.JobId == job.Id).ToListAsync(ct);
        foreach (var line in scaleLines)
        {
            line.Awb = normalized;
        }

        await db.SaveChangesAsync(ct);
        return new CreateJobResult(job.JobNo, job.Awb ?? normalized, job.Status);
    }

    public async Task EnsureAwbUniqueIndexAsync(CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'UX_Jobs_ABCourier_Awb'
                  AND object_id = OBJECT_ID(N'dbo.Jobs_ABCourier')
            )
            AND NOT EXISTS (
                SELECT 1 FROM dbo.Jobs_ABCourier
                WHERE Awb IS NOT NULL AND Awb <> ''
                GROUP BY Awb
                HAVING COUNT(*) > 1
            )
            BEGIN
                CREATE UNIQUE INDEX UX_Jobs_ABCourier_Awb
                    ON dbo.Jobs_ABCourier(Awb)
                    WHERE Awb IS NOT NULL AND Awb <> '';
            END
            """, ct);
    }

    public async Task EnsureBookingCarrierColumnAsync(CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.Bookings_ABCourier', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Bookings_ABCourier', N'CarrierCode') IS NULL
            BEGIN
                ALTER TABLE dbo.Bookings_ABCourier
                    ADD CarrierCode varchar(20) NOT NULL
                        CONSTRAINT DF_Bookings_ABCourier_CarrierCode DEFAULT '';
            END
            """, ct);
    }

    public async Task EnsureBookingCountryColumnsAsync(CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.Bookings_ABCourier', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.Bookings_ABCourier', N'FromCountryCode') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Bookings_ABCourier', N'FromCountryCode') < 160
                BEGIN
                    ALTER TABLE dbo.Bookings_ABCourier ALTER COLUMN FromCountryCode nvarchar(80) NOT NULL;
                END
                IF COL_LENGTH(N'dbo.Bookings_ABCourier', N'ToCountryCode') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Bookings_ABCourier', N'ToCountryCode') < 160
                BEGIN
                    ALTER TABLE dbo.Bookings_ABCourier ALTER COLUMN ToCountryCode nvarchar(80) NOT NULL;
                END
            END
            """, ct);
    }

    private async Task<string> GenerateUniqueAwbAsync(CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            var awb = $"7723{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
            if (!await AwbTakenAsync(awb, null, ct)) return awb;
        }

        throw new InvalidOperationException("Không sinh được số AWB chưa trùng. Thử lại.");
    }

    private async Task<bool> AwbTakenAsync(string awb, Guid? exceptJobId, CancellationToken ct)
    {
        var query = db.Jobs.AsQueryable().Where(x => x.Awb != null);
        if (exceptJobId.HasValue) query = query.Where(x => x.Id != exceptJobId.Value);
        var existing = await query.Select(x => x.Awb!).ToListAsync(ct);
        return existing.Any(x => string.Equals(NormalizeAwb(x, false), awb, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeAwb(string value, bool validateLength = true)
    {
        var raw = value.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");
        if (validateLength && raw.Length > 60)
        {
            throw new InvalidOperationException("Số vận đơn tối đa 60 ký tự.");
        }

        return raw;
    }

    public async Task UpdateBookingStatusAsync(string bookingIdOrNo, string status, string? note, CancellationToken ct = default)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(
            x => x.BookingNo == bookingIdOrNo || x.Id.ToString() == bookingIdOrNo, ct)
            ?? throw new InvalidOperationException("Không tìm thấy booking.");
        booking.Status = status;
        if (!string.IsNullOrWhiteSpace(note)) booking.CustomerNote = note;
        booking.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdatePickupTaskAsync(string pickupNo, UpdatePickupTaskRequest request, CancellationToken ct = default)
    {
        var task = await db.PickupTasks.FirstOrDefaultAsync(x => x.PickupNo == pickupNo, ct)
            ?? throw new InvalidOperationException("Không tìm thấy pickup task.");
        task.Status = request.Status;
        if (request.ActualPieces.HasValue) task.ActualPieces = request.ActualPieces;
        if (string.Equals(request.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(request.Status, "SyncPending", StringComparison.OrdinalIgnoreCase))
        {
            task.CompletedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<PickupTaskDto> ConfirmPickupCaptureAsync(
        string pickupNo,
        IReadOnlyList<IFormFile> photos,
        IFormFile? signature,
        Guid? userId,
        bool restrictToAssignedUser,
        CancellationToken ct = default)
    {
        var task = await db.PickupTasks
            .Include(x => x.Booking).ThenInclude(b => b.Customer)
            .Include(x => x.Booking).ThenInclude(b => b.Packages)
            .Include(x => x.Booking).ThenInclude(b => b.SenderAddress)
            .Include(x => x.Job)
            .Include(x => x.Evidence)
            .Include(x => x.ConfirmationLogs)
            .FirstOrDefaultAsync(x => x.PickupNo == pickupNo, ct)
            ?? throw new InvalidOperationException("Không tìm thấy pickup task.");

        if (restrictToAssignedUser && task.AssignedUserId != userId)
            throw new InvalidOperationException("Việc này không giao cho bạn.");

        var photoFiles = photos.Where(x => x.Length > 0).Take(12).ToList();
        var hasSignature = signature is { Length: > 0 };
        var firstConfirm = task.ConfirmationCount <= 0;
        var existingSignature = task.Evidence.Any(x => x.Kind == "Signature" && x.IsCurrent);

        if (firstConfirm)
        {
            if (photoFiles.Count == 0) throw new InvalidOperationException("Cần ít nhất một ảnh bằng chứng.");
            if (!hasSignature) throw new InvalidOperationException("Cần chữ ký người giao hàng.");
        }
        else if (photoFiles.Count == 0 && !hasSignature)
        {
            throw new InvalidOperationException("Thêm ảnh mới hoặc ký lại để lưu thay đổi.");
        }

        if (photoFiles.Any(x => x.Length > 8 * 1024 * 1024) || (signature?.Length ?? 0) > 8 * 1024 * 1024)
            throw new InvalidOperationException("Mỗi file tối đa 8MB.");

        foreach (var photo in photoFiles)
        {
            task.Evidence.Add(await SaveEvidenceAsync(task, "Photo", photo, userId, ct));
        }

        if (hasSignature)
        {
            foreach (var old in task.Evidence.Where(x => x.Kind == "Signature" && x.IsCurrent).ToList())
                old.IsCurrent = false;
            var saved = await SaveEvidenceAsync(task, "Signature", signature!, userId, ct);
            task.Evidence.Add(saved);
            task.SignatureFileUrl = $"/api/platform/pickup-evidence/{saved.Id}";
        }

        var pieces = task.Booking.Packages.Sum(p => p.Pieces);
        task.ActualPieces = pieces > 0 ? pieces : task.ExpectedPieces;
        task.ConfirmationCount += 1;
        task.LastConfirmedAt = DateTime.UtcNow;
        task.LastConfirmedByUserId = userId;
        if (string.Equals(task.Status, "Assigned", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(task.Status, "InProgress", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(task.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            task.Status = "SyncPending";
        }
        task.CompletedAt ??= DateTime.UtcNow;

        var parts = new List<string>();
        if (photoFiles.Count > 0) parts.Add($"thêm {photoFiles.Count} ảnh");
        if (hasSignature) parts.Add(existingSignature ? "đổi chữ ký" : "lưu chữ ký");
        var detail = firstConfirm
            ? $"Xác nhận lần 1: {string.Join(", ", parts)}."
            : $"Cập nhật lần {task.ConfirmationCount}: {string.Join(", ", parts)}.";

        var log = new PickupConfirmationLog
        {
            Id = Guid.NewGuid(),
            PickupTaskId = task.Id,
            SequenceNo = task.ConfirmationCount,
            Action = firstConfirm ? "Confirmed" : "Updated",
            Detail = detail,
            PhotoCountAdded = photoFiles.Count,
            SignatureChanged = hasSignature,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        db.PickupConfirmationLogs.Add(log);
        task.ConfirmationLogs.Add(log);

        await db.SaveChangesAsync(ct);

        var nameIds = new List<Guid>();
        if (task.AssignedUserId.HasValue) nameIds.Add(task.AssignedUserId.Value);
        if (userId.HasValue) nameIds.Add(userId.Value);
        nameIds.AddRange(task.ConfirmationLogs.Where(x => x.CreatedByUserId.HasValue).Select(x => x.CreatedByUserId!.Value));
        var names = nameIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(x => nameIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        return MapPickupTask(task, names);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)?> OpenPickupEvidenceAsync(
        Guid evidenceId,
        Guid? userId,
        bool restrictToAssignedUser,
        CancellationToken ct = default)
    {
        var evidence = await db.PickupEvidence
            .Include(x => x.PickupTask)
            .Include(x => x.Blob)
            .FirstOrDefaultAsync(x => x.Id == evidenceId, ct);
        if (evidence is null) return null;
        if (restrictToAssignedUser && evidence.PickupTask.AssignedUserId != userId)
            throw new InvalidOperationException("Không có quyền xem file này.");

        if (evidence.Blob?.FileContent is { Length: > 0 } bytes)
            return (new MemoryStream(bytes), evidence.ContentType, evidence.FileName);

        var path = Path.Combine(PickupCaptureFolder(evidence.PickupTask.PickupNo), evidence.StoredFileName);
        if (!File.Exists(path)) return null;
        return (File.OpenRead(path), evidence.ContentType, evidence.FileName);
    }

    public async Task<PickupTaskDto> DeletePickupEvidenceAsync(
        Guid evidenceId,
        Guid? userId,
        bool restrictToAssignedUser,
        CancellationToken ct = default)
    {
        var evidence = await db.PickupEvidence
            .Include(x => x.PickupTask)
            .FirstOrDefaultAsync(x => x.Id == evidenceId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy ảnh.");

        if (restrictToAssignedUser && evidence.PickupTask.AssignedUserId != userId)
            throw new InvalidOperationException("Không có quyền xoá file này.");
        if (!string.Equals(evidence.Kind, "Photo", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Chỉ xoá được ảnh. Chữ ký thì ký lại để thay.");

        var remainingPhotos = await db.PickupEvidence.CountAsync(
            x => x.PickupTaskId == evidence.PickupTaskId
                 && x.Kind == "Photo"
                 && x.IsCurrent
                 && x.Id != evidence.Id,
            ct);
        if (evidence.PickupTask.ConfirmationCount > 0 && remainingPhotos == 0)
            throw new InvalidOperationException("Đã xác nhận thì phải giữ ít nhất một ảnh.");

        var pickupNo = evidence.PickupTask.PickupNo;
        var fileName = evidence.FileName;
        var task = evidence.PickupTask;
        db.PickupEvidence.Remove(evidence);

        task.ConfirmationCount += 1;
        task.LastConfirmedAt = DateTime.UtcNow;
        task.LastConfirmedByUserId = userId;
        db.PickupConfirmationLogs.Add(new PickupConfirmationLog
        {
            Id = Guid.NewGuid(),
            PickupTaskId = task.Id,
            SequenceNo = task.ConfirmationCount,
            Action = "PhotoDeleted",
            Detail = $"Xoá ảnh: {fileName}.",
            PhotoCountAdded = 0,
            SignatureChanged = false,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);

        var reloaded = await db.PickupTasks
            .Include(x => x.Booking).ThenInclude(b => b.Customer)
            .Include(x => x.Booking).ThenInclude(b => b.Packages)
            .Include(x => x.Booking).ThenInclude(b => b.SenderAddress)
            .Include(x => x.Job)
            .Include(x => x.Evidence)
            .Include(x => x.ConfirmationLogs)
            .FirstAsync(x => x.PickupNo == pickupNo, ct);

        var nameIds = new List<Guid>();
        if (reloaded.AssignedUserId.HasValue) nameIds.Add(reloaded.AssignedUserId.Value);
        if (userId.HasValue) nameIds.Add(userId.Value);
        nameIds.AddRange(reloaded.ConfirmationLogs.Where(x => x.CreatedByUserId.HasValue).Select(x => x.CreatedByUserId!.Value));
        var names = nameIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(x => nameIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        return MapPickupTask(reloaded, names);
    }

    public async Task EnsurePickupCaptureSchemaAsync(CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.PickupTasks_ABCourier', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.PickupTasks_ABCourier', N'ConfirmationCount') IS NULL
            BEGIN
                ALTER TABLE dbo.PickupTasks_ABCourier
                    ADD ConfirmationCount int NOT NULL
                        CONSTRAINT DF_PickupTasks_ABCourier_ConfirmationCount DEFAULT 0,
                        LastConfirmedAt datetime2(0) NULL,
                        LastConfirmedByUserId uniqueidentifier NULL;
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.PickupEvidence_ABCourier', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PickupEvidence_ABCourier
                (
                    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PickupEvidence_ABCourier PRIMARY KEY,
                    PickupTaskId UNIQUEIDENTIFIER NOT NULL,
                    Kind VARCHAR(20) NOT NULL,
                    FileName NVARCHAR(260) NOT NULL,
                    StoredFileName VARCHAR(80) NOT NULL,
                    ContentType VARCHAR(80) NOT NULL,
                    IsCurrent BIT NOT NULL CONSTRAINT DF_PickupEvidence_ABCourier_IsCurrent DEFAULT 1,
                    CreatedByUserId UNIQUEIDENTIFIER NULL,
                    CreatedAt DATETIME2(0) NOT NULL CONSTRAINT DF_PickupEvidence_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_PickupEvidence_ABCourier_Tasks
                        FOREIGN KEY (PickupTaskId) REFERENCES dbo.PickupTasks_ABCourier(Id)
                );
                CREATE INDEX IX_PickupEvidence_ABCourier_Task ON dbo.PickupEvidence_ABCourier (PickupTaskId);
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.PickupConfirmationLogs_ABCourier', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PickupConfirmationLogs_ABCourier
                (
                    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PickupConfirmationLogs_ABCourier PRIMARY KEY,
                    PickupTaskId UNIQUEIDENTIFIER NOT NULL,
                    SequenceNo INT NOT NULL,
                    Action VARCHAR(40) NOT NULL,
                    Detail NVARCHAR(500) NOT NULL,
                    PhotoCountAdded INT NOT NULL CONSTRAINT DF_PickupConfirmationLogs_ABCourier_Photos DEFAULT 0,
                    SignatureChanged BIT NOT NULL CONSTRAINT DF_PickupConfirmationLogs_ABCourier_Sig DEFAULT 0,
                    CreatedByUserId UNIQUEIDENTIFIER NULL,
                    CreatedAt DATETIME2(0) NOT NULL CONSTRAINT DF_PickupConfirmationLogs_ABCourier_CreatedAt DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_PickupConfirmationLogs_ABCourier_Tasks
                        FOREIGN KEY (PickupTaskId) REFERENCES dbo.PickupTasks_ABCourier(Id),
                    CONSTRAINT UQ_PickupConfirmationLogs_ABCourier_Seq UNIQUE (PickupTaskId, SequenceNo)
                );
            END
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.PickupEvidenceBlobs_ABCourier', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PickupEvidenceBlobs_ABCourier
                (
                    EvidenceId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PickupEvidenceBlobs_ABCourier PRIMARY KEY,
                    FileContent VARBINARY(MAX) NOT NULL,
                    CONSTRAINT FK_PickupEvidenceBlobs_ABCourier_Evidence
                        FOREIGN KEY (EvidenceId) REFERENCES dbo.PickupEvidence_ABCourier(Id) ON DELETE CASCADE
                );
            END
            """, ct);
    }

    public async Task SyncPendingPickupTasksAsync(CancellationToken ct = default)
    {
        var pending = await db.PickupTasks.Where(x => x.Status == "SyncPending").ToListAsync(ct);
        foreach (var task in pending)
        {
            task.Status = "Completed";
            task.CompletedAt ??= DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<CustomerProfileDto?> GetCustomerProfileAsync(Guid customerId, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == customerId, ct);
        if (customer is null) return null;
        return new CustomerProfileDto(
            customer.Id,
            customer.CustomerCode,
            customer.CompanyName,
            customer.TaxCode,
            customer.BillingEmail,
            customer.CreditLimit,
            customer.BillingCycle,
            customer.RatePlanCode);
    }

    public async Task<IReadOnlyList<InvoiceSummaryDto>> GetInvoicesAsync(Guid? customerId, CancellationToken ct = default, Guid? salesUserId = null)
    {
        var query = db.Invoices.Include(x => x.Customer).AsQueryable();
        if (customerId.HasValue) query = query.Where(x => x.CustomerId == customerId);
        if (salesUserId.HasValue) query = query.Where(x => x.Customer.SalesUserId == salesUserId);
        var rows = await query.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct);
        return rows.Select(x => new InvoiceSummaryDto(
            x.InvoiceNo ?? x.Id.ToString(),
            x.Customer.CompanyName,
            $"{x.InvoiceDate:MM/yyyy}",
            x.Subtotal,
            x.VatAmount,
            x.TotalAmount,
            x.Status,
            x.InvoiceDate,
            x.DueDate,
            x.PdfFileUrl,
            x.XmlFileUrl)).ToList();
    }

    private static JobChargeLine Line(Guid jobId, string kind, string code, string desc, decimal amount) => new()
    {
        Id = Guid.NewGuid(),
        JobId = jobId,
        ChargeKind = kind,
        ChargeCode = code,
        Description = desc,
        Quantity = 1,
        UnitPrice = amount,
        Amount = amount,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static BookingSummaryDto MapBooking(Booking booking)
    {
        var declared = DeclaredQuote(booking);
        var scale = ScaleQuote(booking);
        var weight = booking.Packages.Sum(p => p.ChargeableWeight);
        return new BookingSummaryDto(
            booking.BookingNo,
            booking.Customer.CompanyName,
            booking.Direction,
            booking.ServiceCode,
            $"{booking.FromCountryCode} → {booking.ToCountryCode}",
            weight,
            declared?.TotalAmount ?? 0,
            booking.Status,
            booking.Documents.Count,
            booking.CustomerNote ?? "",
            booking.CreatedAt,
            booking.ShipDate,
            scale?.TotalAmount,
            string.IsNullOrWhiteSpace(booking.CarrierCode) ? null : booking.CarrierCode);
    }

    private static BookingDetailDto MapBookingDetail(Booking booking)
    {
        var declared = DeclaredQuote(booking);
        var scale = ScaleQuote(booking);
        var canEdit = booking.Status is not "JobCreated" and not "Rejected";
        return new BookingDetailDto(
            booking.BookingNo,
            booking.Customer.CompanyName,
            booking.Direction,
            booking.ServiceCode,
            booking.FromCountryCode,
            booking.ToCountryCode,
            booking.ShipDate,
            booking.Status,
            booking.CustomerNote ?? "",
            booking.RequiresComplianceReview,
            canEdit,
            MapAddress(booking.SenderAddress),
            MapAddress(booking.ReceiverAddress),
            booking.Packages.OrderBy(p => p.PackageNo).Select(p => new BookingPackageDto(
                p.Pieces, p.ActualWeight, p.LengthCm, p.WidthCm, p.HeightCm, p.PackageType,
                p.DimWeight, p.ChargeableWeight, p.IsStackable)).ToList(),
            booking.Commodities.Select(c => new BookingCommodityDto(
                c.ItemName, c.DescriptionEnglish, c.Quantity, c.UnitValue, c.Currency, c.HsCode,
                c.ContainsBattery, c.ContainsLiquid, c.IsFood, c.IsDangerousGoods, c.CountryOfOrigin)).ToList(),
            booking.Documents.Select(d => d.FileName).ToList(),
            MapQuoteDto(declared),
            booking.CreatedAt,
            MapQuoteDto(scale),
            string.IsNullOrWhiteSpace(booking.CarrierCode) ? null : booking.CarrierCode);
    }

    private PickupTaskDto MapPickupTask(PickupTask t, IReadOnlyDictionary<Guid, string> names)
    {
        var photos = t.Evidence
            .Where(x => x.Kind == "Photo" && x.IsCurrent)
            .OrderBy(x => x.CreatedAt)
            .Select(MapEvidence)
            .ToList();
        var signature = t.Evidence
            .Where(x => x.Kind == "Signature" && x.IsCurrent)
            .OrderByDescending(x => x.CreatedAt)
            .Select(MapEvidence)
            .FirstOrDefault();
        var logs = t.ConfirmationLogs
            .OrderBy(x => x.SequenceNo)
            .Select(x => new PickupConfirmationLogDto(
                x.SequenceNo,
                x.Action,
                x.Detail,
                x.CreatedAt,
                x.CreatedByUserId is { } id && names.TryGetValue(id, out var logName) ? logName : null,
                x.PhotoCountAdded,
                x.SignatureChanged))
            .ToList();

        return new PickupTaskDto(
            t.PickupNo,
            t.Booking.BookingNo,
            t.Booking.Customer.CompanyName,
            $"{t.Booking.FromCountryCode} → {t.Booking.ToCountryCode}",
            t.ExpectedPieces,
            t.ScheduledAt,
            t.Status,
            t.JobId?.ToString(),
            t.Job?.Awb,
            t.AssignedUserId?.ToString(),
            t.AssignedUserId is { } uid && names.TryGetValue(uid, out var name) ? name : null,
            FormatPickupAddress(t.Booking.SenderAddress),
            t.Booking.Packages
                .OrderBy(p => p.PackageNo)
                .Select(p => new BookingPackageDto(
                    p.Pieces, p.ActualWeight, p.LengthCm, p.WidthCm, p.HeightCm, p.PackageType,
                    p.DimWeight, p.ChargeableWeight, p.IsStackable))
                .ToList(),
            t.ConfirmationCount,
            t.LastConfirmedAt,
            signature,
            photos,
            logs);
    }

    private static PickupEvidenceDto MapEvidence(PickupEvidence evidence) =>
        new(evidence.Id.ToString(), evidence.Kind, evidence.FileName,
            $"/api/platform/pickup-evidence/{evidence.Id}", evidence.CreatedAt, evidence.IsCurrent);

    private async Task<PickupEvidence> SaveEvidenceAsync(
        PickupTask task,
        string kind,
        IFormFile file,
        Guid? userId,
        CancellationToken ct)
    {
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 8)
            ext = kind == "Signature" ? ".png" : ".jpg";
        ext = ext.ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            throw new InvalidOperationException("Chỉ nhận ảnh jpg, png hoặc webp.");

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
            throw new InvalidOperationException("File ảnh/chữ ký rỗng.");

        var stored = $"{Guid.NewGuid():N}{ext}";
        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? (ext == ".png" ? "image/png" : ext == ".webp" ? "image/webp" : "image/jpeg")
            : file.ContentType;
        var fileName = string.IsNullOrWhiteSpace(file.FileName) ? stored : Path.GetFileName(file.FileName);

        var evidence = new PickupEvidence
        {
            Id = Guid.NewGuid(),
            PickupTaskId = task.Id,
            Kind = kind,
            FileName = fileName.Length > 260 ? fileName[..260] : fileName,
            StoredFileName = stored,
            ContentType = contentType.Length > 80 ? contentType[..80] : contentType,
            IsCurrent = true,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            Blob = new PickupEvidenceBlob
            {
                FileContent = bytes
            }
        };
        db.PickupEvidence.Add(evidence);
        return evidence;
    }

    private string PickupCaptureFolder(string pickupNo)
    {
        var safe = new string(pickupNo.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (string.IsNullOrWhiteSpace(safe)) safe = "unknown";
        return Path.Combine(env.ContentRootPath, "App_Data", "pickup-captures", safe);
    }

    private static string? FormatPickupAddress(CustomerAddress? address)
    {
        if (address is null) return null;
        var parts = new[]
        {
            address.ContactName,
            address.Phone,
            address.AddressLine1,
            address.City,
            address.PostalCode,
            address.CountryCode
        }.Where(x => !string.IsNullOrWhiteSpace(x));
        var text = string.Join(" • ", parts);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static BookingQuote? DeclaredQuote(Booking booking) =>
        booking.Quotes
            .Where(x => !IsScaleQuote(x))
            .OrderBy(x => x.VersionNo)
            .FirstOrDefault()
        ?? booking.Quotes.OrderBy(x => x.VersionNo).FirstOrDefault();

    private static BookingQuote? ScaleQuote(Booking booking) =>
        booking.Quotes
            .Where(IsScaleQuote)
            .OrderByDescending(x => x.VersionNo)
            .FirstOrDefault();

    private static bool IsScaleQuote(BookingQuote quote) =>
        string.Equals(TryReadSnapshot(quote.CalculationSnapshot, "source"), "Scale", StringComparison.OrdinalIgnoreCase);

    private static BookingQuoteDto? MapQuoteDto(BookingQuote? quote) =>
        quote is null ? null : new BookingQuoteDto(
            quote.ZoneCode, quote.ChargeableWeight, quote.Freight, quote.FuelSurcharge,
            quote.DemandSurcharge, quote.HandlingCharge, quote.VatAmount, quote.TotalAmount,
            TryReadSnapshot(quote.CalculationSnapshot, "RateSetCode"),
            TryReadSnapshot(quote.CalculationSnapshot, "source"),
            TryReadSnapshot(quote.CalculationSnapshot, "carrierCode"));

    private static BookingAddressDto? MapAddress(CustomerAddress? address) =>
        address is null ? null : new BookingAddressDto(
            address.ContactName,
            address.CompanyName,
            address.Phone,
            address.Email,
            address.AddressLine1,
            address.City,
            address.StateProvince,
            address.PostalCode,
            address.CountryCode,
            address.IsResidential);

    private static JobSummaryDto MapJob(Job job)
    {
        var revenue = job.ChargeLines.Where(c => c.ChargeKind == "Debit").Sum(c => c.Amount);
        var cost = job.ChargeLines.Where(c => c.ChargeKind == "Credit").Sum(c => c.Amount);
        var profit = revenue - cost;
        var margin = revenue == 0 ? 0 : Math.Round(profit / revenue * 100m, 2);
        return new JobSummaryDto(
            job.JobNo,
            job.Booking.BookingNo,
            job.Customer.CompanyName,
            job.Awb ?? "",
            job.Direction,
            job.ServiceCode,
            job.Status,
            revenue,
            cost,
            profit,
            margin,
            job.BillingStatus);
    }

    public Task<BookingRateQuoteDto> PreviewQuoteAsync(
        BookingQuotePreviewRequest request,
        Guid? customerId,
        CancellationToken ct = default)
    {
        var packages = request.Packages is { Count: > 0 }
            ? request.Packages
            : [new BookingPackageDto(1, request.ActualWeight, request.Length, request.Width, request.Height, request.PackageType)];
        return quotes.QuoteAsync(
            customerId,
            request.Direction,
            request.Service,
            request.FromCountry,
            request.ToCountry,
            request.ShipDate,
            packages,
            request.Residential,
            request.CarrierCode,
            ct: ct);
    }

    public Task<IReadOnlyList<CountryOptionDto>> ListRateCountriesAsync(
        Guid customerId,
        string direction,
        string service,
        DateTime shipDate,
        string? carrierCode,
        CancellationToken ct = default)
        => quotes.ListCountriesAsync(
            customerId,
            direction,
            service,
            shipDate,
            carrierCode,
            ct);

    private static string? TryReadSnapshot(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase)) continue;
                return prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                    ? prop.Value.GetString()
                    : prop.Value.ToString();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
