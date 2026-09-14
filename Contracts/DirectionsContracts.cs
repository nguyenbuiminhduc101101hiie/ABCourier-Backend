namespace CourierApi.Contracts;

public sealed record GeoPointDto(double Lat, double Lng);

public sealed record DirectionsRouteDto(
    double OriginLat,
    double OriginLng,
    string OriginAddress,
    double DestinationLat,
    double DestinationLng,
    string DestinationAddress,
    IReadOnlyList<GeoPointDto> Polyline,
    double DistanceMeters,
    double DurationSeconds);

public sealed record SaveCourierLocationRequest(double Latitude, double Longitude, DateTime? RecordedAtUtc);
