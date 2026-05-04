using System;
using System.IO;

namespace JackBridge.GUI.Services;

public static class AppLogger
{
    private static readonly object _lock = new();
    private static string? _logPath;

    public static void Initialize(string baseDirectory)
    {
        var logsDir = Path.Combine(baseDirectory, "logs");
        Directory.CreateDirectory(logsDir);
        _logPath = Path.Combine(logsDir, $"jackbridge-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        Write("=== JackBridge session started ===");
    }

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
        lock (_lock)
        {
            try { File.AppendAllText(_logPath!, line); }
            catch { }
        }
    }
}
