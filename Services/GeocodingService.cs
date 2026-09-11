using System.Globalization;
using System.Text.Json;

namespace CourierApi.Services;

public sealed class GeocodingService(HttpClient httpClient, IConfiguration configuration, ILogger<GeocodingService> logger)
    : IGeocodingService
{
    public async Task<(double Lat, double Lng)?> GeocodeAddressAsync(string address, CancellationToken ct = default)
    {
        var apiKey = configuration["Geocoding:GoongApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Goong API key chưa được cấu hình (Geocoding:GoongApiKey).");
            return null;
        }

        try
        {
            var url = $"https://rsapi.goong.io/Geocode?address={Uri.EscapeDataString(address)}&api_key={Uri.EscapeDataString(apiKey)}";
            using var response = await httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Goong Geocoding API trả về status={StatusCode} body={Body} cho địa chỉ {Address}", response.StatusCode, body, address);
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var status = root.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
            if (status != "OK")
            {
                logger.LogWarning("Goong Geocoding API trả về status={Status} cho địa chỉ {Address}", status, address);
                return null;
            }

            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            {
                return null;
            }

            var location = results[0].GetProperty("geometry").GetProperty("location");
            var lat = location.GetProperty("lat").GetDouble();
            var lng = location.GetProperty("lng").GetDouble();
            return (lat, lng);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Goong Geocoding API request failed cho địa chỉ {Address}", address);
            return null;
        }
    }

    public async Task<DirectionsResult?> GetDirectionsAsync(
        double originLat,
        double originLng,
        double destLat,
        double destLng,
        CancellationToken ct = default)
    {
        var apiKey = configuration["Geocoding:GoongApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Goong API key chưa được cấu hình (Geocoding:GoongApiKey).");
            return null;
        }

        try
        {
            var origin = FormatLatLng(originLat, originLng);
            var destination = FormatLatLng(destLat, destLng);
            var url = $"https://rsapi.goong.io/Direction?origin={origin}&destination={destination}&vehicle=car&api_key={Uri.EscapeDataString(apiKey)}";
            using var response = await httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Goong Direction API trả về status={StatusCode} body={Body}", response.StatusCode, body);
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Goong Direction API (khác Geocode API) không trả field "status" — chỉ cần kiểm tra "routes".
            if (!root.TryGetProperty("routes", out var routes) || routes.ValueKind != JsonValueKind.Array || routes.GetArrayLength() == 0)
            {
                logger.LogWarning("Goong Direction API không có routes. Body={Body}", responseBody);
                return null;
            }

            var route = routes[0];
            double distanceMeters = 0;
            double durationSeconds = 0;
            if (route.TryGetProperty("legs", out var legs) && legs.ValueKind == JsonValueKind.Array)
            {
                foreach (var leg in legs.EnumerateArray())
                {
                    if (leg.TryGetProperty("distance", out var distance) && distance.TryGetProperty("value", out var distanceValue))
                    {
                        distanceMeters += distanceValue.GetDouble();
                    }
                    if (leg.TryGetProperty("duration", out var duration) && duration.TryGetProperty("value", out var durationValue))
                    {
                        durationSeconds += durationValue.GetDouble();
                    }
                }
            }

            var encodedPolyline = route.TryGetProperty("overview_polyline", out var overviewPolyline) &&
                overviewPolyline.TryGetProperty("points", out var pointsProp)
                ? pointsProp.GetString()
                : null;
            var points = string.IsNullOrEmpty(encodedPolyline) ? [] : DecodePolyline(encodedPolyline);

            return new DirectionsResult(points, distanceMeters, durationSeconds);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Goong Direction API request failed");
            return null;
        }
    }

    private static string FormatLatLng(double lat, double lng) =>
        $"{lat.ToString(CultureInfo.InvariantCulture)},{lng.ToString(CultureInfo.InvariantCulture)}";

    /// Giải mã chuỗi polyline theo Google Encoded Polyline Algorithm Format (Goong dùng cùng chuẩn).
    private static List<(double Lat, double Lng)> DecodePolyline(string encoded)
    {
        var points = new List<(double, double)>();
        var index = 0;
        var lat = 0;
        var lng = 0;

        while (index < encoded.Length)
        {
            var shift = 0;
            var result = 0;
            int b;
            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);
            var dlat = (result & 1) != 0 ? ~(result >> 1) : result >> 1;
            lat += dlat;

            shift = 0;
            result = 0;
            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);
            var dlng = (result & 1) != 0 ? ~(result >> 1) : result >> 1;
            lng += dlng;

            points.Add((lat / 1e5, lng / 1e5));
        }

        return points;
    }
}
