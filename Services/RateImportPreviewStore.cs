using Microsoft.Extensions.Caching.Memory;

namespace CourierApi.Services;

public sealed class RateImportPreviewStore(IMemoryCache cache)
{
    public string Put(ParsedRateWorkbook workbook)
    {
        var token = Guid.NewGuid().ToString("N");
        cache.Set(token, workbook, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
            Size = 1
        });
        return token;
    }

    public bool TryGet(string token, out ParsedRateWorkbook workbook)
    {
        if (cache.TryGetValue(token, out ParsedRateWorkbook? value) && value is not null)
        {
            workbook = value;
            return true;
        }

        workbook = null!;
        return false;
    }

    public void Remove(string token) => cache.Remove(token);
}
