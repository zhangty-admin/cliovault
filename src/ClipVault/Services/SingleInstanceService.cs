namespace ClipVault.Services;

/// <summary>
/// 单实例守护 — 通过命名 Mutex 防止应用多开
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = "Global\\ClipVault_SingleInstance_Mutex";
    private Mutex? _mutex;
    private bool _hasHandle;

    /// <summary>
    /// 尝试获取单实例锁
    /// </summary>
    /// <returns>如果是第一个实例返回 true，已有实例运行返回 false</returns>
    public bool TryAcquire()
    {
        _mutex = new Mutex(false, MutexName, out _);
        try
        {
            _hasHandle = _mutex.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            _hasHandle = true;
        }
        return _hasHandle;
    }

    public void Dispose()
    {
        if (_mutex != null)
        {
            if (_hasHandle)
            {
                _mutex.ReleaseMutex();
            }
            _mutex.Dispose();
        }
    }
}
