namespace CourierApi.Options;

public sealed class PublicApiOptions
{
    public const string SectionName = "PublicApi";

    public string BaseUrl { get; set; } = "http://localhost:5088";
}
