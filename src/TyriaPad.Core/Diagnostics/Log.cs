namespace TyriaPad.Core.Diagnostics;

/// <summary>
/// Text log for diagnosing on the Ally X without a debugger. Starts fresh on every launch (the
/// previous one is kept as <c>.old.log</c>) and, since TyriaPad can stay open for days (Start with
/// Windows), once past <see cref="MaxBytes"/> it is also renamed to <c>.old.log</c> and a new one starts.
/// If not initialized (tests), it does nothing.
/// </summary>
public static class Log
{
    public const long MaxBytes = 8 * 1024 * 1024;

    private static readonly Lock s_lock = new();
    private static StreamWriter? s_writer;

    public static string? FilePath { get; private set; }

    public static void Initialize(string path)
    {
        lock (s_lock)
        {
            s_writer?.Dispose();

            // The previous session's log is kept as .old.log, so a test run's log isn't lost if
            // TyriaPad is reopened before it was copied.
            if (File.Exists(path))
            {
                try
                {
                    File.Move(path, Path.ChangeExtension(path, ".old.log"), overwrite: true);
                }
                catch (IOException)
                {
                }
            }

            s_writer = Open(path);
            FilePath = path;
        }
    }

    public static void Write(string message)
    {
        lock (s_lock)
        {
            if (s_writer is null)
            {
                return;
            }

            s_writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {message}");
            if (s_writer.BaseStream.Length > MaxBytes && FilePath is { } path)
            {
                Rotate(path);
            }
        }
    }

    public static void Close()
    {
        lock (s_lock)
        {
            s_writer?.Dispose();
            s_writer = null;
        }
    }

    private static void Rotate(string path)
    {
        s_writer!.Dispose();
        string old = Path.ChangeExtension(path, ".old.log");
        try
        {
            File.Move(path, old, overwrite: true);
        }
        catch (IOException)
        {
            // If someone has it open without sharing, reopening truncates it anyway.
        }

        s_writer = Open(path);
        s_writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  (previous log in {Path.GetFileName(old)})");
    }

    // FileShare.ReadWrite | Delete: it can be opened or copied while TyriaPad is running.
    private static StreamWriter Open(string path)
        => new(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
}
