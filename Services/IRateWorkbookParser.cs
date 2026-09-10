using CourierApi.Contracts;

namespace CourierApi.Services;

public interface IRateWorkbookParser
{
    Task<ParsedRateWorkbook> ParseAsync(
        Stream source,
        string fileName,
        string carrierCode,
        DateTime? fallbackEffectiveFrom,
        CancellationToken cancellationToken);
}

public sealed class RateWorkbookParserResolver(
    FedExRateWorkbookParser fedExParser,
    DhlRateWorkbookParser dhlParser)
{
    public IRateWorkbookParser Resolve(string carrierCode)
    {
        var code = carrierCode.Trim().ToUpperInvariant();
        return code switch
        {
            "DHL" => dhlParser,
            _ => fedExParser,
        };
    }
}
