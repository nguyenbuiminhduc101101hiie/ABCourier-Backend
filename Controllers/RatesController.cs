using CourierApi.Contracts;
using CourierApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourierApi.Controllers;

[ApiController]
[Authorize]
[Route("api/rates")]
public sealed class RatesController(RateQuoteService quotes) : ControllerBase
{
    [HttpGet("lookup")]
    public async Task<ActionResult<RateLookupResponse>> Lookup(
        [FromQuery] string direction,
        [FromQuery] string serviceCode,
        [FromQuery] string packageType,
        [FromQuery] decimal weight,
        [FromQuery] string? zoneCode = null,
        [FromQuery] string? country = null,
        [FromQuery] DateTime? shipDate = null,
        [FromQuery] string carrierCode = "FEDEX",
        [FromQuery] Guid? customerId = null,
        CancellationToken cancellationToken = default)
    {
        if (weight <= 0m) return BadRequest(new { message = "Weight phải lớn hơn 0." });
        if (string.IsNullOrWhiteSpace(zoneCode) && string.IsNullOrWhiteSpace(country))
        {
            return BadRequest(new { message = "Cần truyền zoneCode hoặc country hợp lệ." });
        }

        try
        {
            var quote = await quotes.QuoteAsync(
                customerId,
                direction,
                serviceCode,
                country ?? "",
                country ?? "",
                shipDate ?? DateTime.Today,
                [new BookingPackageDto(1, weight, 0, 0, 0, packageType)],
                false,
                carrierCode,
                zoneCode,
                cancellationToken);

            return Ok(new RateLookupResponse(
                quote.CarrierCode,
                RateQuoteService.NormalizeDirection(direction),
                serviceCode.Trim().ToUpperInvariant(),
                RateQuoteService.NormalizePackageType(
                    packageType,
                    quote.ChargeableWeight,
                    serviceCode.Trim().ToUpperInvariant(),
                    quote.CarrierCode),
                quote.Zone,
                weight,
                quote.ChargeableWeight,
                quote.RateUnit,
                quote.UnitRate,
                quote.Freight,
                quote.Demand,
                quote.FuelMultiplier,
                quote.VatMultiplier,
                quote.Total,
                quote.EffectiveFrom,
                quote.ImportBatchId ?? Guid.Empty));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("countries")]
    public async Task<ActionResult<IReadOnlyList<CountryOptionDto>>> Countries(
        [FromQuery] string direction,
        [FromQuery] string serviceCode,
        [FromQuery] string carrierCode = "FEDEX",
        [FromQuery] DateTime? shipDate = null,
        [FromQuery] Guid? customerId = null,
        CancellationToken cancellationToken = default)
    {
        direction = RateQuoteService.NormalizeDirection(direction);
        serviceCode = serviceCode.Trim().ToUpperInvariant();
        var date = (shipDate ?? DateTime.Today).Date;
        return Ok(await quotes.ListCountriesAsync(
            customerId,
            direction,
            serviceCode,
            date,
            carrierCode,
            cancellationToken));
    }
}
