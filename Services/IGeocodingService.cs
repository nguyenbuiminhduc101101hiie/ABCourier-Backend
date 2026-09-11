namespace CourierApi.Services;

public sealed record DirectionsResult(
    IReadOnlyList<(double Lat, double Lng)> Points,
    double DistanceMeters,
    double DurationSeconds);

public interface IGeocodingService
{
    Task<(double Lat, double Lng)?> GeocodeAddressAsync(string address, CancellationToken ct = default);

    Task<DirectionsResult?> GetDirectionsAsync(
        double originLat,
        double originLng,
        double destLat,
        double destLng,
        CancellationToken ct = default);
}
