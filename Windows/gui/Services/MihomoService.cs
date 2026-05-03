using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace JackBridge.GUI.Services;

public sealed class MihomoService : IDisposable
{
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    public async Task<bool> RefreshProfileAsync(BuiltInProxyConfig config, Action<string>? log = null)
    {
        var profilePath = ResolvePortablePath(config.ActiveProfilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath) ?? AppContext.BaseDirectory);

        if (!string.IsNullOrWhiteSpace(config.SubscriptionUrl))
        {
            log?.Invoke("Downloading built-in proxy subscription profile...");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            var yaml = await client.GetStringAsync(config.SubscriptionUrl);
            await File.WriteAllTextAsync(profilePath, yaml);
            log?.Invoke($"Built-in proxy profile updated: {ToPortablePath(profilePath)}");
            return true;
        }

        if (!string.IsNullOrWhiteSpace(config.LocalYamlPath))
        {
            var sourcePath = ResolvePortablePath(config.LocalYamlPath);
            if (!File.Exists(sourcePath))
            {
                log?.Invoke($"ERROR: YAML profile not found: {sourcePath}");
                return false;
            }

            File.Copy(sourcePath, profilePath, overwrite: true);
            log?.Invoke($"Built-in proxy profile copied: {ToPortablePath(profilePath)}");
            return true;
        }

        log?.Invoke("ERROR: Built-in proxy needs a subscription URL or local YAML profile");
        return false;
    }

    public async Task<bool> StartAsync(BuiltInProxyConfig config, Action<string>? log = null)
    {
        if (IsRunning)
            return true;

        EnsureSecret(config);
        EnsureCoreFolders();

        var corePath = ResolvePortablePath(config.CorePath);
        if (!File.Exists(corePath))
        {
            log?.Invoke($"ERROR: mihomo core not found: {corePath}");
            log?.Invoke("Place mihomo.exe in the portable core folder, then try again.");
            return false;
        }

        var profilePath = ResolvePortablePath(config.ActiveProfilePath);
        if (!File.Exists(profilePath) && !await RefreshProfileAsync(config, log))
            return false;

        WriteRuntimeConfig(config, profilePath);

        var runtimeConfigPath = ResolvePortablePath("core\\runtime.yaml");
        var processInfo = new ProcessStartInfo
        {
            FileName = corePath,
            Arguments = $"-f \"{runtimeConfigPath}\" -d \"{ResolvePortablePath("data")}\"",
            WorkingDirectory = Path.GetDirectoryName(corePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _process = new Process { StartInfo = processInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log?.Invoke($"mihomo: {e.Data}"); };
        _process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log?.Invoke($"mihomo: {e.Data}"); };
        _process.Exited += (_, _) => log?.Invoke("Built-in proxy core stopped");

        if (!_process.Start())
            return false;

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        log?.Invoke($"Built-in proxy core started on 127.0.0.1:{config.MixedPort}");
        return true;
    }

    public void Stop(Action<string>? log = null)
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
                log?.Invoke("Built-in proxy core stopped");
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"ERROR: Failed to stop built-in proxy core: {ex.Message}");
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    public static void EnsureCoreFolders()
    {
        Directory.CreateDirectory(ResolvePortablePath("core"));
        Directory.CreateDirectory(ResolvePortablePath("profiles"));
        Directory.CreateDirectory(ResolvePortablePath("data"));
        Directory.CreateDirectory(ResolvePortablePath("rules"));
        Directory.CreateDirectory(ResolvePortablePath("logs"));
    }

    public static string ResolvePortablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return AppContext.BaseDirectory;

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static string ToPortablePath(string path)
    {
        var relative = Path.GetRelativePath(AppContext.BaseDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }

    private static void EnsureSecret(BuiltInProxyConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.ControllerSecret))
            return;

        config.ControllerSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    private static void WriteRuntimeConfig(BuiltInProxyConfig config, string profilePath)
    {
        var profile = File.ReadAllText(profilePath);
        var suffix = $"""

mixed-port: {config.MixedPort}
allow-lan: false
external-controller: 127.0.0.1:{config.ControllerPort}
secret: "{config.ControllerSecret}"
""";

        File.WriteAllText(ResolvePortablePath("core\\runtime.yaml"), profile.TrimEnd() + suffix);
    }
}
