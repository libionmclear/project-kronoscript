namespace MyStoryTold.Helpers;

/// <summary>App-wide build metadata. Bump <see cref="Version"/> on each release;
/// it surfaces on the About page and anywhere else we want to show what's
/// running. Build date is read from the assembly's last-write timestamp so
/// it reflects the actual deployed bits, not when the constant was edited.</summary>
public static class AppInfo
{
    public const string Version = "0.9.0 beta";

    public static DateTime BuildDate
    {
        get
        {
            try
            {
                var asm = typeof(AppInfo).Assembly.Location;
                if (!string.IsNullOrEmpty(asm) && File.Exists(asm))
                    return File.GetLastWriteTimeUtc(asm);
            }
            catch { /* file-system access may be sandboxed in some hosts */ }
            return DateTime.UtcNow;
        }
    }
}
