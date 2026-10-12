using System.Diagnostics;

namespace HirRadar;

/// <summary>
/// Local diagnostics only, sampled at most every five minutes; no network use.
/// CPU rate is averaged over elapsed wall-clock time and normalized per logical CPU.
/// </summary>
public static class RuntimeHealth
{
    private static readonly object Gate = new();
    private static TimeSpan? _previousCpu;
    private static DateTimeOffset? _previousAt;
    private static string _last = "Első mérésre vár.";

    public static string Latest
    {
        get { lock (Gate) return _last; }
    }

    public static void Report(int newsCount, bool fetching)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var now = DateTimeOffset.UtcNow;
            var cpu = process.TotalProcessorTime;
            string rate = "első minta";
            lock (Gate)
            {
                if (_previousCpu.HasValue && _previousAt.HasValue)
                {
                    double elapsed = (now - _previousAt.Value).TotalSeconds;
                    if (elapsed > 0)
                    {
                        double used = (cpu - _previousCpu.Value).TotalSeconds;
                        rate = $"{Math.Clamp(100 * used / elapsed / Math.Max(1, Environment.ProcessorCount), 0, 100):0.0}%";
                    }
                }
                _previousCpu = cpu;
                _previousAt = now;
                _last = $"CPU: {rate}; privát memória: {process.PrivateMemorySize64 / 1048576.0:0.0} MB; " +
                    $"munkakészlet: {process.WorkingSet64 / 1048576.0:0.0} MB; szálak: {process.Threads.Count}; " +
                    $"GC: {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}; " +
                    $"hírek: {newsCount}; frissítés: {(fetching ? "folyamatban" : "nem")}";
            }
            AppLogger.Info("HEALTH " + Latest);
        }
        catch (Exception ex) { AppLogger.Warning("Egészségmérés kihagyva: " + ex.GetType().Name); }
    }
}
