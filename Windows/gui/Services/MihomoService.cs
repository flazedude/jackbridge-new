using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JackBridge.GUI.Services;

public sealed class MihomoService : IDisposable
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(3)
    };

    private Process? _process;
    private WindowsProcessJob? _job;

    public bool IsRunning => _process is { HasExited: false };

    public bool IsListening(BuiltInProxyConfig config)
    {
        if (!IsRunning)
            return false;

        if (!ushort.TryParse(config.MixedPort, out var port))
            return false;

        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect("127.0.0.1", port, null, null);
            if (result.AsyncWaitHandle.WaitOne(500))
            {
                client.EndConnect(result);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string> GetCoreStatusAsync(BuiltInProxyConfig config)
    {
        var corePath = ResolvePortablePath(config.CorePath);
        if (!File.Exists(corePath))
            return $"Core missing: {corePath}";

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = corePath,
                Arguments = "-v",
                WorkingDirectory = Path.GetDirectoryName(corePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
                return "Core found, but version check could not start";

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var output = (await outputTask + await errorTask).Trim();
            return string.IsNullOrWhiteSpace(output) ? $"Core ready: {ToPortablePath(corePath)}" : output.Split('\n')[0].Trim();
        }
        catch (Exception ex)
        {
            return $"Core found, version check failed: {ex.Message}";
        }
    }

    public static async Task<bool> InstallOrUpdateCoreAsync(BuiltInProxyConfig config, Action<string>? log = null)
    {
        EnsureCoreFolders();
        var corePath = ResolvePortablePath(config.CorePath);
        Directory.CreateDirectory(Path.GetDirectoryName(corePath) ?? AppContext.BaseDirectory);

        log?.Invoke("Checking latest mihomo release...");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/MetaCubeX/mihomo/releases/latest");
        request.Headers.UserAgent.ParseAdd("JackBridge/2.0");
        using var response = await SharedHttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tag = document.RootElement.GetProperty("tag_name").GetString() ?? "latest";
        var assetUrl = FindWindowsAmd64Asset(document.RootElement);
        if (assetUrl == null)
        {
            log?.Invoke("ERROR: No Windows amd64 mihomo asset found in latest release.");
            return false;
        }

        log?.Invoke($"Downloading mihomo {tag}...");
        using var assetResponse = await SharedHttpClient.GetAsync(assetUrl);
        assetResponse.EnsureSuccessStatusCode();

        var tempPath = corePath + ".tmp";

        if (assetUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var zipPath = Path.Combine(Path.GetTempPath(), $"jackbridge-mihomo-{Guid.NewGuid():N}.zip");
            await using (var zipFile = File.Create(zipPath))
            await using (var assetStream = await assetResponse.Content.ReadAsStreamAsync())
            {
                await assetStream.CopyToAsync(zipFile);
            }

            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var exeEntry = archive.Entries.FirstOrDefault(e =>
                    e.Name.Equals("mihomo.exe", StringComparison.OrdinalIgnoreCase) ||
                    e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                if (exeEntry == null)
                {
                    log?.Invoke("ERROR: Downloaded mihomo ZIP did not contain an executable.");
                    return false;
                }

                exeEntry.ExtractToFile(tempPath, overwrite: true);
            }
            finally
            {
                try { File.Delete(zipPath); } catch { }
            }
        }
        else
        {
            await using var assetStream = await assetResponse.Content.ReadAsStreamAsync();
            await using var gzip = new GZipStream(assetStream, CompressionMode.Decompress);
            await using var output = File.Create(tempPath);
            await gzip.CopyToAsync(output);
        }

        if (File.Exists(corePath))
            File.Delete(corePath);

        File.Move(tempPath, corePath);
        log?.Invoke($"mihomo {tag} installed: {ToPortablePath(corePath)}");
        return true;
    }

    private static async Task EnsureGeoAssetsAsync(BuiltInProxyConfig config, Action<string>? log = null)
    {
        var essentialFiles = new[] { "geoip.dat", "geosite.dat", "country.mmdb" };
        var allPresent = true;

        foreach (var file in essentialFiles)
        {
            var path = ResolvePortablePath(Path.Combine("data", file));
            if (!File.Exists(path))
            {
                allPresent = false;
                break;
            }
        }

        if (allPresent)
            return;

        log?.Invoke("GEO assets missing — downloading...");
        await UpdateGeoAssetsAsync(config, log);
    }

    public static async Task<bool> UpdateGeoAssetsAsync(BuiltInProxyConfig config, Action<string>? log = null)
    {
        EnsureCoreFolders();
        var items = new[]
        {
            ("geoip.dat", config.GeoIpUrl),
            ("geosite.dat", config.GeoSiteUrl),
            ("country.mmdb", config.MmdbUrl),
            ("GeoLite2-ASN.mmdb", config.AsnUrl)
        };

        foreach (var (fileName, url) in items)
        {
            if (string.IsNullOrWhiteSpace(url))
                continue;

            var targetPath = ResolvePortablePath(Path.Combine("data", fileName));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory);
            log?.Invoke($"Updating {fileName}...");
            await using var stream = await SharedHttpClient.GetStreamAsync(url);
            var tempPath = targetPath + ".tmp";
            await using (var output = File.Create(tempPath))
            {
                await stream.CopyToAsync(output);
            }

            if (File.Exists(targetPath))
                File.Delete(targetPath);

            File.Move(tempPath, targetPath);
        }

        log?.Invoke("GEO assets updated.");
        return true;
    }

    public static async Task<ProxyChoice[]> GetProxyChoicesAsync(BuiltInProxyConfig config)
    {
        var url = $"http://127.0.0.1:{config.ControllerPort}/proxies";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddControllerAuth(request, config);
        using var response = await SharedHttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!document.RootElement.TryGetProperty("proxies", out var proxies))
            return [];

        var selectableServers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectorGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var proxy in proxies.EnumerateObject())
        {
            if (proxy.Value.TryGetProperty("all", out _))
            {
                selectorGroups.Add(proxy.Name);
                continue;
            }

            if (proxy.Name.Equals("DIRECT", StringComparison.OrdinalIgnoreCase) ||
                proxy.Name.Equals("REJECT", StringComparison.OrdinalIgnoreCase) ||
                proxy.Name.Equals("GLOBAL", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            selectableServers.Add(proxy.Name);
        }

        var choices = new System.Collections.Generic.List<ProxyChoice>();
        foreach (var group in proxies.EnumerateObject())
        {
            if (!group.Value.TryGetProperty("all", out var all) || all.ValueKind != JsonValueKind.Array)
                continue;

            var groupChoices = new List<ProxyChoice>();
            foreach (var proxy in all.EnumerateArray())
            {
                var proxyName = proxy.GetString();
                if (!string.IsNullOrWhiteSpace(proxyName) &&
                    selectableServers.Contains(proxyName) &&
                    !selectorGroups.Contains(proxyName))
                {
                    groupChoices.Add(new ProxyChoice(group.Name, proxyName));
                }
            }

            choices.AddRange(groupChoices.Count > 0
                ? groupChoices
                : selectableServers.Select(server => new ProxyChoice(group.Name, server)));
        }

        return choices
            .GroupBy(c => c.ProxyName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(c => c.ProxyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task<ProxyChoice[]> GetProfileProxyChoicesAsync(BuiltInProxyConfig config)
    {
        var profilePath = ResolvePortablePath(config.ActiveProfilePath);
        if (!File.Exists(profilePath))
            return [];

        var yaml = await File.ReadAllTextAsync(profilePath);
        return ParseProxyChoicesFromYaml(yaml);
    }

    public static async Task<string> GetProfileSummaryAsync(BuiltInProxyConfig config)
    {
        var profilePath = ResolvePortablePath(config.ActiveProfilePath);
        if (!File.Exists(profilePath))
            return $"No active profile at {ToPortablePath(profilePath)}";

        var yaml = await File.ReadAllTextAsync(profilePath);
        var choices = ParseProxyChoicesFromYaml(yaml);
        var groups = choices.Select(c => c.GroupName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var servers = choices.Select(c => c.ProxyName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var size = new FileInfo(profilePath).Length / 1024.0;
        return $"Loaded {ToPortablePath(profilePath)} • {servers} servers • {groups} groups • {size:0.#} KB";
    }

    public static async Task SelectProxyAsync(BuiltInProxyConfig config, string groupName, string proxyName)
    {
        var url = $"http://127.0.0.1:{config.ControllerPort}/proxies/{Uri.EscapeDataString(groupName)}";
        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        AddControllerAuth(request, config);
        request.Content = new StringContent(JsonSerializer.Serialize(new { name = proxyName }), Encoding.UTF8, "application/json");
        using var response = await SharedHttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

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

    public static async Task<bool> ImportLocalProfileAsync(string sourcePath, BuiltInProxyConfig config, Action<string>? log = null)
    {
        if (!File.Exists(sourcePath))
        {
            log?.Invoke($"ERROR: YAML profile not found: {sourcePath}");
            return false;
        }

        var extension = Path.GetExtension(sourcePath);
        if (!extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            log?.Invoke("ERROR: Only .yaml and .yml profiles can be imported.");
            return false;
        }

        var targetPath = ResolvePortablePath(config.ActiveProfilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory);
        await using var source = File.OpenRead(sourcePath);
        await using var target = File.Create(targetPath);
        await source.CopyToAsync(target);

        config.LocalYamlPath = ToPortablePath(targetPath);
        log?.Invoke($"Imported profile: {ToPortablePath(targetPath)}");
        return true;
    }

    public async Task<bool> StartAsync(BuiltInProxyConfig config, Action<string>? log = null)
    {
        if (IsRunning)
            return true;

        EnsureSecret(config);
        EnsureCoreFolders();

        await EnsureGeoAssetsAsync(config, log);

        var corePath = ResolvePortablePath(config.CorePath);
        if (!File.Exists(corePath))
        {
            log?.Invoke($"ERROR: mihomo core not found: {corePath}");
            log?.Invoke("Place mihomo.exe in the portable core folder, then try again.");
            return false;
        }
        var launchCorePath = EnsureJackBridgeCoreAlias(corePath, log);

        var profilePath = ResolvePortablePath(config.ActiveProfilePath);
        if ((config.AutoUpdateSubscription || !File.Exists(profilePath)) && !await RefreshProfileAsync(config, log))
            return false;

        WriteRuntimeConfig(config, profilePath);

        var runtimeConfigPath = ResolvePortablePath("core\\runtime.yaml");
        var processInfo = new ProcessStartInfo
        {
            FileName = launchCorePath,
            Arguments = $"-f \"{runtimeConfigPath}\" -d \"{ResolvePortablePath("data")}\"",
            WorkingDirectory = Path.GetDirectoryName(launchCorePath) ?? AppContext.BaseDirectory,
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

        try
        {
            _job = new WindowsProcessJob();
            _job.Add(_process);
        }
        catch (Exception ex)
        {
            log?.Invoke($"WARNING: Unable to attach mihomo to JackBridge lifetime: {ex.Message}");
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        if (!await IsLocalPortOpenAsync(config.MixedPort, TimeSpan.FromSeconds(5)))
        {
            log?.Invoke($"ERROR: Built-in proxy core did not start listening on 127.0.0.1:{config.MixedPort}");
            Stop(log);
            return false;
        }

        log?.Invoke($"Built-in proxy core listening on 127.0.0.1:{config.MixedPort}");
        await Task.Delay(2000);
        return true;
    }

    private static string EnsureJackBridgeCoreAlias(string corePath, Action<string>? log)
    {
        if (!Path.GetFileName(corePath).Equals("mihomo.exe", StringComparison.OrdinalIgnoreCase))
            return corePath;

        var aliasPath = Path.Combine(Path.GetDirectoryName(corePath) ?? AppContext.BaseDirectory, "jackbridge-mihomo.exe");
        try
        {
            var shouldCopy = !File.Exists(aliasPath) ||
                File.GetLastWriteTimeUtc(aliasPath) < File.GetLastWriteTimeUtc(corePath) ||
                new FileInfo(aliasPath).Length != new FileInfo(corePath).Length;

            if (shouldCopy)
                File.Copy(corePath, aliasPath, overwrite: true);

            return aliasPath;
        }
        catch (Exception ex)
        {
            log?.Invoke($"WARNING: Could not prepare JackBridge mihomo launcher: {ex.Message}");
            return corePath;
        }
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
            _job?.Dispose();
            _job = null;
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
        var profile = RemoveManagedTopLevelKeys(
            File.ReadAllText(profilePath),
            includeRules: NormalizeMode(config.Mode).Equals("rule", StringComparison.OrdinalIgnoreCase),
            includeDns: config.OverrideDns);
        var controller = config.EnableController
            ? $"external-controller: 127.0.0.1:{config.ControllerPort}\nsecret: \"{config.ControllerSecret}\"\n"
            : "";

        var suffix = $"""

mixed-port: {config.MixedPort}
allow-lan: {ToYamlBool(config.AllowLan)}
mode: {NormalizeMode(config.Mode)}
log-level: {NormalizeLogLevel(config.LogLevel)}
ipv6: {ToYamlBool(config.Ipv6)}
unified-delay: {ToYamlBool(config.UnifiedDelay)}
geo-auto-update: {ToYamlBool(config.GeoAutoUpdate)}
geo-update-interval: {NormalizeGeoInterval(config.GeoUpdateInterval)}
geox-url:
  geoip: "{config.GeoIpUrl}"
  geosite: "{config.GeoSiteUrl}"
  mmdb: "{config.MmdbUrl}"
  asn: "{config.AsnUrl}"
{BuildRulePreset(config)}
{BuildDnsOverride(config)}
{controller.TrimEnd()}
""";

        File.WriteAllText(ResolvePortablePath("core\\runtime.yaml"), profile.TrimEnd() + suffix);
    }

    private static async Task<bool> IsLocalPortOpenAsync(string port, TimeSpan timeout)
    {
        if (!ushort.TryParse(port, out var portNumber))
            return false;

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync("127.0.0.1", portNumber);
                var completed = await Task.WhenAny(connectTask, Task.Delay(250));
                if (completed == connectTask && client.Connected)
                    return true;
            }
            catch { }

            await Task.Delay(150);
        }

        return false;
    }

    private static string BuildDnsOverride(BuiltInProxyConfig config)
    {
        if (!config.OverrideDns)
            return "";

        return $"""

dns:
  enable: true
  listen: 0.0.0.0:1053
  ipv6: {ToYamlBool(config.Ipv6)}
  enhanced-mode: fake-ip
  fake-ip-range: 198.18.0.1/16
  default-nameserver:
{BuildYamlList(config.DefaultNameservers)}
  nameserver:
{BuildYamlList(config.Nameservers)}
  proxy-server-nameserver:
{BuildYamlList(config.ProxyServerNameservers)}
""";
    }

    private static string BuildYamlList(string values)
    {
        var items = values
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .DefaultIfEmpty("1.1.1.1")
            .Select(item => $"    - {item}");

        return string.Join(Environment.NewLine, items);
    }

    private static string RemoveManagedTopLevelKeys(string yaml, bool includeRules, bool includeDns)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mixed-port",
            "allow-lan",
            "mode",
            "log-level",
            "ipv6",
            "unified-delay",
            "geo-auto-update",
            "geo-update-interval",
            "geox-url",
            "external-controller",
            "secret"
        };

        if (includeRules)
            keys.Add("rules");

        if (includeDns)
            keys.Add("dns");

        var normalized = yaml.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var output = new List<string>(lines.Length);
        var skipping = false;

        foreach (var line in lines)
        {
            var topLevel = Regex.Match(line, @"^(?<key>[A-Za-z0-9_-]+)\s*:");
            if (topLevel.Success)
                skipping = keys.Contains(topLevel.Groups["key"].Value);

            if (!skipping)
                output.Add(line);
        }

        return string.Join(Environment.NewLine, output);
    }

    private static string ToYamlBool(bool value) => value ? "true" : "false";

    private static string NormalizeMode(string value)
    {
        value = value?.Trim().ToLowerInvariant() ?? "";
        return value is "global" or "direct" or "rule" ? value : "rule";
    }

    private static string NormalizeLogLevel(string value)
    {
        value = value?.Trim().ToLowerInvariant() ?? "";
        return value is "debug" or "info" or "warning" or "error" or "silent" ? value : "info";
    }

    private static string BuildRulePreset(BuiltInProxyConfig config)
    {
        if (!NormalizeMode(config.Mode).Equals("rule", StringComparison.OrdinalIgnoreCase))
            return "";

        var proxyPolicy = string.IsNullOrWhiteSpace(config.SelectedProxyName)
            ? "GLOBAL"
            : QuoteYamlValue(config.SelectedProxyName);

        return (config.RulePreset ?? "").Trim().ToLowerInvariant() switch
        {
            "global-proxy" => $"""

rules:
  - MATCH,{proxyPolicy}
""",
            "direct-all" => """

rules:
  - MATCH,DIRECT
""",
            "ads-direct-cn" => $"""

rules:
  - GEOSITE,category-ads-all,REJECT
  - GEOSITE,private,DIRECT
  - GEOIP,private,DIRECT,no-resolve
  - GEOSITE,cn,DIRECT
  - GEOIP,CN,DIRECT,no-resolve
  - MATCH,{proxyPolicy}
""",
            _ => $"""

rules:
  - GEOSITE,private,DIRECT
  - GEOIP,private,DIRECT,no-resolve
  - GEOSITE,cn,DIRECT
  - GEOIP,CN,DIRECT,no-resolve
  - MATCH,{proxyPolicy}
"""
        };
    }

    private static string QuoteYamlValue(string value)
    {
        return value.Contains(',') || value.Contains(':') || value.Contains('#')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }

    private static string NormalizeGeoInterval(string value)
    {
        return ushort.TryParse(value, out var interval) && interval > 0 ? interval.ToString() : "24";
    }

    private static void AddControllerAuth(HttpRequestMessage request, BuiltInProxyConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.ControllerSecret))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ControllerSecret);
    }

    private static string? FindWindowsAmd64Asset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets))
            return null;

        string? fallback = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.Contains("windows-amd64", StringComparison.OrdinalIgnoreCase) ||
                (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                 !name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var url = asset.GetProperty("browser_download_url").GetString();
            if (name.Contains("compatible", StringComparison.OrdinalIgnoreCase))
                return url;

            fallback ??= url;
        }

        return fallback;
    }

    private static ProxyChoice[] ParseProxyChoicesFromYaml(string yaml)
    {
        var proxyNames = ExtractTopLevelProxyNames(yaml);
        var groupNames = ExtractTopLevelGroupNames(yaml);

        var choices = new List<ProxyChoice>();
        var groupBlocks = Regex.Matches(
            yaml,
            @"(?ms)^proxy-groups\s*:\s*.*?(?=^\S|\z)");

        var groupEntryBlocks = groupBlocks.Count > 0
            ? Regex.Matches(groupBlocks[0].Value, @"(?ms)^\s*-\s*name\s*:\s*['""]?(?<group>[^'""\r\n#]+)['""]?.*?^\s+proxies\s*:\s*(?<items>(?:\r?\n\s+-\s*.*)+)")
            : Regex.Matches(yaml, @"(?ms)^\s*-\s*name\s*:\s*['""]?(?<group>[^'""\r\n#]+)['""]?.*?^\s+proxies\s*:\s*(?<items>(?:\r?\n\s+-\s*.*)+)");

        foreach (Match block in groupEntryBlocks)
        {
            var groupName = block.Groups["group"].Value.Trim();
            if (string.IsNullOrWhiteSpace(groupName))
                continue;

            foreach (Match item in Regex.Matches(block.Groups["items"].Value, @"(?m)^\s*-\s*['""]?(?<name>[^'""\r\n#]+)['""]?\s*(?:#.*)?$"))
            {
                var proxyName = item.Groups["name"].Value.Trim();
                if (proxyNames.Contains(proxyName) && !groupNames.Contains(proxyName))
                    choices.Add(new ProxyChoice(groupName, proxyName));
            }
        }

        foreach (var includeAllGroup in ExtractIncludeAllGroupNames(yaml))
        {
            if (choices.Any(c => c.GroupName.Equals(includeAllGroup, StringComparison.OrdinalIgnoreCase)))
                continue;

            choices.AddRange(proxyNames.Select(proxyName => new ProxyChoice(includeAllGroup, proxyName)));
        }

        if (choices.Count == 0)
        {
            var defaultGroup = "Profile";
            foreach (var name in proxyNames)
                choices.Add(new ProxyChoice(defaultGroup, name));
        }

        return choices
            .GroupBy(c => c.ProxyName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(c => c.ProxyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HashSet<string> ExtractTopLevelProxyNames(string yaml)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var match = Regex.Match(yaml, @"(?ms)^proxies\s*:\s*(?<body>.*?)(?=^\S|\z)");
        if (!match.Success)
            return names;

        foreach (Match item in Regex.Matches(match.Groups["body"].Value, @"(?m)^\s*-\s*name\s*:\s*['""]?(?<name>[^'""\r\n#]+)['""]?\s*(?:#.*)?$"))
        {
            var name = item.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        foreach (Match item in Regex.Matches(match.Groups["body"].Value, @"(?m)^\s*-\s*\{\s*name\s*:\s*['""]?(?<name>[^,'""\r\n#}]+)['""]?\s*,"))
        {
            var name = item.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        return names;
    }

    private static HashSet<string> ExtractTopLevelGroupNames(string yaml)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var match = Regex.Match(yaml, @"(?ms)^proxy-groups\s*:\s*(?<body>.*?)(?=^\S|\z)");
        if (!match.Success)
            return names;

        foreach (Match item in Regex.Matches(match.Groups["body"].Value, @"(?m)^\s*-\s*name\s*:\s*['""]?(?<name>[^'""\r\n#]+)['""]?\s*(?:#.*)?$"))
        {
            var name = item.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        return names;
    }

    private static HashSet<string> ExtractIncludeAllGroupNames(string yaml)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var match = Regex.Match(yaml, @"(?ms)^proxy-groups\s*:\s*(?<body>.*?)(?=^\S|\z)");
        if (!match.Success)
            return names;

        foreach (Match group in Regex.Matches(match.Groups["body"].Value, @"(?ms)^\s*-\s*name\s*:\s*['""]?(?<name>[^'""\r\n#]+)['""]?.*?(?=^\s*-\s*name\s*:|\z)"))
        {
            if (!Regex.IsMatch(group.Value, @"(?m)^\s*include-all\s*:\s*true\s*(?:#.*)?$", RegexOptions.IgnoreCase))
                continue;

            var name = group.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        return names;
    }
}

public sealed record ProxyChoice(string GroupName, string ProxyName)
{
    public string DisplayName => ProxyName;
    public override string ToString() => DisplayName;
}
