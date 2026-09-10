namespace CourierApi.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "CourierApi";
    public string Audience { get; set; } = "CourierFlutter";
    public string Key { get; set; } = "ChangeThis_DevOnly_JwtSigningKey_AtLeast32Chars!";
    public int ExpireMinutes { get; set; } = 720;
}
