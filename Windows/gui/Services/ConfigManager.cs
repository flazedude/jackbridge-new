using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using JackBridge.GUI.ViewModels;

namespace JackBridge.GUI.Services;

public class AppConfig
{
    public string ProxyEngine { get; set; } = "External";
    public string ProxyType { get; set; } = "SOCKS5";
    public string ProxyIp { get; set; } = "";
    public string ProxyPort { get; set; } = "";
    public string ProxyUsername { get; set; } = "";
    public string ProxyPassword { get; set; } = "";
    public bool IsProxyEnabled { get; set; } = false;
    public bool DnsViaProxy { get; set; } = true;
    public bool LocalhostViaProxy { get; set; } = false;  // Default: disabled
    public bool IsTrafficLoggingEnabled { get; set; } = true;
    public string Language { get; set; } = "en";
    public bool CloseToTray { get; set; } = true;
    public BuiltInProxyConfig BuiltInProxy { get; set; } = new();
    public List<ProxyRuleConfig> ProxyRules { get; set; } = new();
}

public class BuiltInProxyConfig
{
    public string CorePath { get; set; } = "core\\mihomo.exe";
    public string SubscriptionUrl { get; set; } = "";
    public string LocalYamlPath { get; set; } = "";
    public string ActiveProfilePath { get; set; } = "profiles\\mihomo.yaml";
    public string MixedPort { get; set; } = "7892";
    public string ControllerPort { get; set; } = "9090";
    public string ControllerSecret { get; set; } = "";
    public bool AutoUpdateSubscription { get; set; } = false;
}

public class ProxyRuleConfig
{
    public string ProcessName { get; set; } = "";
    public string TargetHosts { get; set; } = "*";
    public string TargetPorts { get; set; } = "*";
    public string Protocol { get; set; } = "TCP";
    public string Action { get; set; } = "PROXY";
    public bool IsEnabled { get; set; } = true;
    public bool IsStatic { get; set; } = false;
}

[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(BuiltInProxyConfig))]
[JsonSerializable(typeof(ProxyRuleConfig))]
[JsonSerializable(typeof(List<ProxyRuleConfig>))]
internal partial class AppConfigJsonContext : JsonSerializerContext
{
}

internal static class AtomicFileHelper
{
    public static bool AtomicWrite(string filePath, string content)
    {
        var tempPath = filePath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(tempPath, content);
            File.Move(tempPath, filePath, overwrite: true);
            return true;
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch { }
            return false;
        }
    }

    public static string? SafeReadFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var content = File.ReadAllText(filePath);
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }
        catch
        {
            return null;
        }
    }
}

public static class ConfigManager
{
    private static readonly string ConfigFilePath = Path.Combine(
        AppContext.BaseDirectory,
        "config.json"
    );

    private static readonly string LegacyConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JackBridge"
    );

    private static readonly string LegacyConfigFilePath = Path.Combine(LegacyConfigDirectory, "config.json");

    public static bool SaveConfig(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, AppConfigJsonContext.Default.AppConfig);
        return AtomicFileHelper.AtomicWrite(ConfigFilePath, json);
    }

    public static AppConfig LoadConfig()
    {
        MigrateLegacyConfigIfNeeded();

        var json = AtomicFileHelper.SafeReadFile(ConfigFilePath);
        if (json == null)
        {
            return new AppConfig();
        }

        try
        {
            var config = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig);
            if (config != null)
            {
                config.BuiltInProxy ??= new BuiltInProxyConfig();
                config.ProxyRules ??= new List<ProxyRuleConfig>();
                return config;
            }
        }
        catch { }

        return new AppConfig();
    }

    public static bool ConfigExists()
    {
        return File.Exists(ConfigFilePath);
    }

    private static void MigrateLegacyConfigIfNeeded()
    {
        if (File.Exists(ConfigFilePath) || !File.Exists(LegacyConfigFilePath))
            return;

        try
        {
            var legacyContent = AtomicFileHelper.SafeReadFile(LegacyConfigFilePath);
            if (legacyContent != null)
            {
                AtomicFileHelper.AtomicWrite(ConfigFilePath, legacyContent);
            }
        }
        catch { }
    }
}
