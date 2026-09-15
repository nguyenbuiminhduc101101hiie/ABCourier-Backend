using CourierApi.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace CourierApi.Services;

public enum CloneJobState
{
    Running,
    Completed,
    Failed
}

public enum CloneJobStep
{
    LoadingSource,
    Cloning,
    SavingZonesAndSurcharges,
    Committing,
    Done
}

public enum CloneJobErrorType
{
    NotFound,
    Validation,
    Unexpected
}

public sealed record CloneJobStatus(
    Guid JobId,
    CloneJobState Status,
    CloneJobStep CurrentStep,
    int ProcessedBands,
    int TotalBands,
    RateImportCloneResponse? Result = null,
    string? Error = null,
    CloneJobErrorType? ErrorType = null);

/// <summary>
/// Lưu tiến trình các job clone bộ giá đang chạy nền. Chạy 1 instance API
/// duy nhất nên in-memory cache là đủ — không cần bảng DB riêng.
/// </summary>
public sealed class CloneJobStore(IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    public void Create(Guid jobId, int totalBands)
    {
        Save(new CloneJobStatus(jobId, CloneJobState.Running, CloneJobStep.LoadingSource, 0, totalBands));
    }

    /// <summary>
    /// Read-then-write với không khóa — chỉ an toàn vì hiện tại mỗi jobId chỉ có
    /// đúng 1 writer (task nền chạy RunCloneJobAsync); các HTTP request khác chỉ
    /// gọi TryGet (đọc). Nếu sau này thêm chỗ gọi Update từ HTTP thread (vd. API
    /// hủy job), cần thêm khóa để tránh lost-update.
    /// </summary>
    public void Update(Guid jobId, Func<CloneJobStatus, CloneJobStatus> updater)
    {
        if (TryGet(jobId, out var current))
        {
            Save(updater(current));
        }
    }

    public bool TryGet(Guid jobId, out CloneJobStatus status)
    {
        if (cache.TryGetValue(jobId, out CloneJobStatus? value) && value is not null)
        {
            status = value;
            return true;
        }

        status = null!;
        return false;
    }

    private void Save(CloneJobStatus status)
    {
        cache.Set(status.JobId, status, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Ttl,
            Size = 1
        });
    }
}
