using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Input;
using JackBridge.GUI.Common;
using JackBridge.GUI.Services;

namespace JackBridge.GUI.ViewModels;

public sealed class ProxySettingsResult
{
    public string ProxyEngine { get; init; } = "External";
    public string ProxyType { get; init; } = "SOCKS5";
    public string ProxyIp { get; init; } = "";
    public string ProxyPort { get; init; } = "";
    public string ProxyUsername { get; init; } = "";
    public string ProxyPassword { get; init; } = "";
    public BuiltInProxyConfig BuiltInProxy { get; init; } = new();
}

public class ProxySettingsViewModel : ViewModelBase
{
    private readonly Loc _loc = Loc.Instance;
    public Loc Loc => _loc;

    private string _proxyEngine = "External";
    private string _proxyIp = "";
    private string _proxyPort = "";
    private string _proxyType = "SOCKS5";
    private string _proxyUsername = "";
    private string _proxyPassword = "";
    private string _corePath = "core\\mihomo.exe";
    private string _subscriptionUrl = "";
    private string _localYamlPath = "";
    private string _activeProfilePath = "profiles\\mihomo.yaml";
    private string _mixedPort = "8888";
    private string _controllerPort = "9090";
    private string _controllerSecret = "";
    private string _mode = "rule";
    private string _logLevel = "info";
    private bool _allowLan;
    private bool _ipv6 = true;
    private bool _unifiedDelay = true;
    private bool _enableController = true;
    private bool _autoUpdateSubscription;
    private bool _geoAutoUpdate = true;
    private string _geoUpdateInterval = "24";
    private string _geoIpUrl = "https://testingcf.jsdelivr.net/gh/MetaCubeX/meta-rules-dat@release/geoip.dat";
    private string _geoSiteUrl = "https://testingcf.jsdelivr.net/gh/MetaCubeX/meta-rules-dat@release/geosite.dat";
    private string _mmdbUrl = "https://testingcf.jsdelivr.net/gh/MetaCubeX/meta-rules-dat@release/country.mmdb";
    private string _asnUrl = "https://github.com/xishang0128/geoip/releases/download/latest/GeoLite2-ASN.mmdb";
    private string _selectorGroup = "GLOBAL";
    private string _selectedProxyName = "";
    private string _rulePreset = "geo-cn-direct";
    private bool _overrideDns;
    private string _defaultNameservers = "1.1.1.1,8.8.8.8,223.5.5.5,119.29.29.29";
    private string _nameservers = "https://1.1.1.1/dns-query,https://8.8.8.8/dns-query,https://dns.alidns.com/dns-query,https://doh.pub/dns-query";
    private string _proxyServerNameservers = "1.1.1.1,8.8.8.8,223.5.5.5,119.29.29.29";
    private ProxyChoice? _selectedProxyChoice;
    private string _coreStatus = "Core not checked yet";
    private string _profileStatus = "No profile loaded yet";
    private string _ipError = "";
    private string _portError = "";
    private string _builtInError = "";
    private bool _isTestViewOpen;
    private string _testTargetHost = "google.com";
    private string _testTargetPort = "80";
    private string _testOutput = "";
    private bool _isTesting;
    private readonly Action<ProxySettingsResult>? _onSave;
    private readonly Action? _onClose;
    private readonly JackBridgeService? _proxyService;

    public string ProxyEngine
    {
        get => _proxyEngine;
        set
        {
            if (SetProperty(ref _proxyEngine, value))
            {
                OnPropertyChanged(nameof(IsExternalProxy));
                OnPropertyChanged(nameof(IsBuiltInProxy));
                BuiltInError = "";
            }
        }
    }

    public bool IsExternalProxy => ProxyEngine.Equals("External", StringComparison.OrdinalIgnoreCase);
    public bool IsBuiltInProxy => ProxyEngine.Equals("BuiltIn", StringComparison.OrdinalIgnoreCase);

    public string ProxyIp { get => _proxyIp; set { SetProperty(ref _proxyIp, value); IpError = ""; } }
    public string ProxyPort { get => _proxyPort; set { SetProperty(ref _proxyPort, value); PortError = ""; } }
    public string ProxyType { get => _proxyType; set => SetProperty(ref _proxyType, value); }
    public string ProxyUsername { get => _proxyUsername; set => SetProperty(ref _proxyUsername, value); }
    public string ProxyPassword { get => _proxyPassword; set => SetProperty(ref _proxyPassword, value); }
    public string CorePath { get => _corePath; set { SetProperty(ref _corePath, value); BuiltInError = ""; } }
    public string SubscriptionUrl { get => _subscriptionUrl; set { SetProperty(ref _subscriptionUrl, value); BuiltInError = ""; } }
    public string LocalYamlPath { get => _localYamlPath; set { SetProperty(ref _localYamlPath, value); BuiltInError = ""; } }
    public string ActiveProfilePath { get => _activeProfilePath; set { SetProperty(ref _activeProfilePath, value); BuiltInError = ""; } }
    public string MixedPort { get => _mixedPort; set { SetProperty(ref _mixedPort, value); BuiltInError = ""; } }
    public string ControllerPort
    {
        get => _controllerPort;
        set
        {
            if (SetProperty(ref _controllerPort, value))
            {
                BuiltInError = "";
                OnPropertyChanged(nameof(ControllerAddress));
            }
        }
    }
    public string ControllerSecret { get => _controllerSecret; set => SetProperty(ref _controllerSecret, value); }
    public string Mode { get => _mode; set => SetProperty(ref _mode, value); }
    public string LogLevel { get => _logLevel; set => SetProperty(ref _logLevel, value); }
    public bool AllowLan { get => _allowLan; set => SetProperty(ref _allowLan, value); }
    public bool Ipv6 { get => _ipv6; set => SetProperty(ref _ipv6, value); }
    public bool UnifiedDelay { get => _unifiedDelay; set => SetProperty(ref _unifiedDelay, value); }
    public bool EnableController { get => _enableController; set => SetProperty(ref _enableController, value); }
    public bool AutoUpdateSubscription { get => _autoUpdateSubscription; set => SetProperty(ref _autoUpdateSubscription, value); }
    public bool GeoAutoUpdate { get => _geoAutoUpdate; set => SetProperty(ref _geoAutoUpdate, value); }
    public string GeoUpdateInterval { get => _geoUpdateInterval; set => SetProperty(ref _geoUpdateInterval, value); }
    public string GeoIpUrl { get => _geoIpUrl; set => SetProperty(ref _geoIpUrl, value); }
    public string GeoSiteUrl { get => _geoSiteUrl; set => SetProperty(ref _geoSiteUrl, value); }
    public string MmdbUrl { get => _mmdbUrl; set => SetProperty(ref _mmdbUrl, value); }
    public string AsnUrl { get => _asnUrl; set => SetProperty(ref _asnUrl, value); }
    public string SelectorGroup { get => _selectorGroup; set => SetProperty(ref _selectorGroup, value); }
    public string SelectedProxyName { get => _selectedProxyName; set => SetProperty(ref _selectedProxyName, value); }
    public string RulePreset { get => _rulePreset; set => SetProperty(ref _rulePreset, value); }
    public bool OverrideDns { get => _overrideDns; set => SetProperty(ref _overrideDns, value); }
    public string DefaultNameservers { get => _defaultNameservers; set => SetProperty(ref _defaultNameservers, value); }
    public string Nameservers { get => _nameservers; set => SetProperty(ref _nameservers, value); }
    public string ProxyServerNameservers { get => _proxyServerNameservers; set => SetProperty(ref _proxyServerNameservers, value); }
    public ObservableCollection<ProxyChoice> ProxyChoices { get; } = new();
    public ProxyChoice? SelectedProxyChoice
    {
        get => _selectedProxyChoice;
        set
        {
            if (SetProperty(ref _selectedProxyChoice, value) && value != null)
            {
                SelectorGroup = value.GroupName;
                SelectedProxyName = value.ProxyName;
            }
        }
    }
    public string CoreStatus { get => _coreStatus; set => SetProperty(ref _coreStatus, value); }
    public string ProfileStatus { get => _profileStatus; set => SetProperty(ref _profileStatus, value); }
    public string ControllerAddress => $"127.0.0.1:{ControllerPort}";
    public bool IsRuleMode => Mode.Equals("rule", StringComparison.OrdinalIgnoreCase);

    public int ModeIndex
    {
        get => Mode.ToLowerInvariant() switch { "global" => 1, "direct" => 2, _ => 0 };
        set
        {
            Mode = value switch { 1 => "global", 2 => "direct", _ => "rule" };
            OnPropertyChanged(nameof(IsRuleMode));
            OnPropertyChanged();
        }
    }

    public int RulePresetIndex
    {
        get => RulePreset.ToLowerInvariant() switch { "global-proxy" => 1, "ads-direct-cn" => 2, "direct-all" => 3, _ => 0 };
        set
        {
            RulePreset = value switch
            {
                1 => "global-proxy",
                2 => "ads-direct-cn",
                3 => "direct-all",
                _ => "geo-cn-direct"
            };
            OnPropertyChanged();
        }
    }

    public int LogLevelIndex
    {
        get => LogLevel.ToLowerInvariant() switch { "debug" => 0, "warning" => 2, "error" => 3, "silent" => 4, _ => 1 };
        set
        {
            LogLevel = value switch { 0 => "debug", 2 => "warning", 3 => "error", 4 => "silent", _ => "info" };
            OnPropertyChanged();
        }
    }
    public string IpError { get => _ipError; set => SetProperty(ref _ipError, value); }
    public string PortError { get => _portError; set => SetProperty(ref _portError, value); }
    public string BuiltInError { get => _builtInError; set => SetProperty(ref _builtInError, value); }
    public bool IsTestViewOpen { get => _isTestViewOpen; set => SetProperty(ref _isTestViewOpen, value); }
    public string TestTargetHost { get => _testTargetHost; set => SetProperty(ref _testTargetHost, value); }
    public string TestTargetPort { get => _testTargetPort; set => SetProperty(ref _testTargetPort, value); }
    public string TestOutput { get => _testOutput; set => SetProperty(ref _testOutput, value); }
    public bool IsTesting { get => _isTesting; set => SetProperty(ref _isTesting, value); }

    public ICommand UseExternalProxyCommand { get; }
    public ICommand UseBuiltInProxyCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenTestCommand { get; }
    public ICommand CloseTestCommand { get; }
    public ICommand StartTestCommand { get; }
    public ICommand RefreshProfileCommand { get; }
    public ICommand CheckCoreCommand { get; }
    public ICommand UpdateCoreCommand { get; }
    public ICommand UpdateGeoAssetsCommand { get; }
    public ICommand LoadProxyChoicesCommand { get; }
    public ICommand ApplyProxyChoiceCommand { get; }
    public ICommand GenerateSecretCommand { get; }

    public ProxySettingsViewModel(
        string initialEngine,
        string initialType,
        string initialIp,
        string initialPort,
        string initialUsername,
        string initialPassword,
        BuiltInProxyConfig initialBuiltInProxy,
        Action<ProxySettingsResult> onSave,
        Action onClose,
        JackBridgeService? proxyService)
    {
        _onSave = onSave;
        _onClose = onClose;
        _proxyService = proxyService;

        ProxyEngine = string.IsNullOrWhiteSpace(initialEngine) ? "External" : initialEngine;
        ProxyType = initialType;
        ProxyIp = initialIp;
        ProxyPort = initialPort;
        ProxyUsername = initialUsername;
        ProxyPassword = initialPassword;
        CorePath = string.IsNullOrWhiteSpace(initialBuiltInProxy.CorePath) ? "core\\mihomo.exe" : initialBuiltInProxy.CorePath;
        SubscriptionUrl = initialBuiltInProxy.SubscriptionUrl;
        LocalYamlPath = initialBuiltInProxy.LocalYamlPath;
        ActiveProfilePath = string.IsNullOrWhiteSpace(initialBuiltInProxy.ActiveProfilePath) ? "profiles\\mihomo.yaml" : initialBuiltInProxy.ActiveProfilePath;
        MixedPort = string.IsNullOrWhiteSpace(initialBuiltInProxy.MixedPort) || initialBuiltInProxy.MixedPort == "7892"
            ? "8888"
            : initialBuiltInProxy.MixedPort;
        ControllerPort = string.IsNullOrWhiteSpace(initialBuiltInProxy.ControllerPort) ? "9090" : initialBuiltInProxy.ControllerPort;
        ControllerSecret = initialBuiltInProxy.ControllerSecret;
        Mode = string.IsNullOrWhiteSpace(initialBuiltInProxy.Mode) ? "rule" : initialBuiltInProxy.Mode;
        LogLevel = string.IsNullOrWhiteSpace(initialBuiltInProxy.LogLevel) ? "info" : initialBuiltInProxy.LogLevel;
        AllowLan = initialBuiltInProxy.AllowLan;
        Ipv6 = initialBuiltInProxy.Ipv6;
        UnifiedDelay = initialBuiltInProxy.UnifiedDelay;
        EnableController = initialBuiltInProxy.EnableController;
        AutoUpdateSubscription = initialBuiltInProxy.AutoUpdateSubscription;
        GeoAutoUpdate = initialBuiltInProxy.GeoAutoUpdate;
        GeoUpdateInterval = string.IsNullOrWhiteSpace(initialBuiltInProxy.GeoUpdateInterval) ? "24" : initialBuiltInProxy.GeoUpdateInterval;
        GeoIpUrl = string.IsNullOrWhiteSpace(initialBuiltInProxy.GeoIpUrl) ? _geoIpUrl : initialBuiltInProxy.GeoIpUrl;
        GeoSiteUrl = string.IsNullOrWhiteSpace(initialBuiltInProxy.GeoSiteUrl) ? _geoSiteUrl : initialBuiltInProxy.GeoSiteUrl;
        MmdbUrl = string.IsNullOrWhiteSpace(initialBuiltInProxy.MmdbUrl) ? _mmdbUrl : initialBuiltInProxy.MmdbUrl;
        AsnUrl = string.IsNullOrWhiteSpace(initialBuiltInProxy.AsnUrl) ? _asnUrl : initialBuiltInProxy.AsnUrl;
        SelectorGroup = string.IsNullOrWhiteSpace(initialBuiltInProxy.SelectorGroup) ? "GLOBAL" : initialBuiltInProxy.SelectorGroup;
        SelectedProxyName = initialBuiltInProxy.SelectedProxyName;
        RulePreset = string.IsNullOrWhiteSpace(initialBuiltInProxy.RulePreset) ? "geo-cn-direct" : initialBuiltInProxy.RulePreset;
        OverrideDns = initialBuiltInProxy.OverrideDns;
        DefaultNameservers = string.IsNullOrWhiteSpace(initialBuiltInProxy.DefaultNameservers) ? _defaultNameservers : initialBuiltInProxy.DefaultNameservers;
        Nameservers = string.IsNullOrWhiteSpace(initialBuiltInProxy.Nameservers) ? _nameservers : initialBuiltInProxy.Nameservers;
        ProxyServerNameservers = string.IsNullOrWhiteSpace(initialBuiltInProxy.ProxyServerNameservers) ? _proxyServerNameservers : initialBuiltInProxy.ProxyServerNameservers;
        _ = RefreshProfileStatusAsync();
        _ = LoadProxyChoicesAsync(updateStatus: false);

        UseExternalProxyCommand = new RelayCommand(() => ProxyEngine = "External");
        UseBuiltInProxyCommand = new RelayCommand(() => ProxyEngine = "BuiltIn");
        SaveCommand = new RelayCommand(() => { if (Validate()) _onSave?.Invoke(CreateResult()); });
        CancelCommand = new RelayCommand(() => _onClose?.Invoke());
        OpenTestCommand = new RelayCommand(() => { IsTestViewOpen = true; TestOutput = ""; });
        CloseTestCommand = new RelayCommand(() => IsTestViewOpen = false);
        GenerateSecretCommand = new RelayCommand(() => ControllerSecret = Guid.NewGuid().ToString("N"));
        CheckCoreCommand = new RelayCommand(async () =>
        {
            IsTesting = true;
            CoreStatus = "Checking mihomo core...";
            try
            {
                CoreStatus = await MihomoService.GetCoreStatusAsync(CreateBuiltInConfig());
            }
            catch (Exception ex)
            {
                CoreStatus = $"Core check failed: {ex.Message}";
            }
            finally
            {
                IsTesting = false;
            }
        });

        UpdateCoreCommand = new RelayCommand(async () =>
        {
            IsTesting = true;
            CoreStatus = "Installing mihomo core...";
            try
            {
                var ok = await MihomoService.InstallOrUpdateCoreAsync(CreateBuiltInConfig(), msg => CoreStatus = msg);
                if (ok) CoreStatus = await MihomoService.GetCoreStatusAsync(CreateBuiltInConfig());
            }
            catch (Exception ex)
            {
                CoreStatus = $"Core update failed: {ex.Message}";
            }
            finally
            {
                IsTesting = false;
            }
        });

        UpdateGeoAssetsCommand = new RelayCommand(async () =>
        {
            if (!ValidateGeoSettings()) return;

            IsTesting = true;
            BuiltInError = "Updating GEO assets...";
            try
            {
                var ok = await MihomoService.UpdateGeoAssetsAsync(CreateBuiltInConfig(), msg => BuiltInError = msg);
                if (ok) BuiltInError = "GEO assets updated.";
            }
            catch (Exception ex)
            {
                BuiltInError = $"GEO update failed: {ex.Message}";
            }
            finally
            {
                IsTesting = false;
            }
        });

        LoadProxyChoicesCommand = new RelayCommand(async () =>
        {
            await LoadProxyChoicesAsync(updateStatus: true);
        });

        ApplyProxyChoiceCommand = new RelayCommand(async () =>
        {
            if (!ValidateController()) return;

            if (string.IsNullOrWhiteSpace(SelectorGroup) || string.IsNullOrWhiteSpace(SelectedProxyName))
            {
                BuiltInError = "Choose a selector group and proxy first.";
                return;
            }

            IsTesting = true;
            BuiltInError = $"Selecting {SelectedProxyName}...";
            try
            {
                await MihomoService.SelectProxyAsync(CreateBuiltInConfig(), SelectorGroup, SelectedProxyName);
                BuiltInError = $"Selected {SelectedProxyName} for {SelectorGroup}.";
            }
            catch (Exception ex)
            {
                BuiltInError = $"Select proxy failed: {ex.Message}";
            }
            finally
            {
                IsTesting = false;
            }
        });

        RefreshProfileCommand = new RelayCommand(async () =>
        {
            if (!ValidateBuiltIn()) return;
            IsTesting = true;
            BuiltInError = "Refreshing profile...";
            try
            {
                var service = new MihomoService();
                var ok = await service.RefreshProfileAsync(CreateBuiltInConfig(), msg => BuiltInError = msg);
                if (ok)
                {
                    await RefreshProfileStatusAsync();
                    BuiltInError = "Profile refreshed.";
                }
            }
            catch (Exception ex)
            {
                BuiltInError = $"ERROR: {ex.Message}";
            }
            finally
            {
                IsTesting = false;
            }
        });

        StartTestCommand = new RelayCommand(async () =>
        {
            if (IsTesting) return;

            var testIp = IsBuiltInProxy ? "127.0.0.1" : ProxyIp;
            var testPort = IsBuiltInProxy ? MixedPort : ProxyPort;

            if (string.IsNullOrWhiteSpace(testIp))
            {
                TestOutput = "ERROR: Please configure proxy IP address or hostname first";
                return;
            }

            if (!ushort.TryParse(testPort, out ushort proxyPortNum))
            {
                TestOutput = "ERROR: Please configure valid proxy port first";
                return;
            }

            if (string.IsNullOrWhiteSpace(TestTargetHost))
            {
                TestOutput = "ERROR: Please enter target host";
                return;
            }

            if (!ushort.TryParse(TestTargetPort, out ushort targetPortNum))
            {
                TestOutput = "ERROR: Invalid target port";
                return;
            }

            IsTesting = true;
            TestOutput = "Testing connection...\n";

            try
            {
                if (_proxyService != null)
                {
                    _proxyService.SetProxyConfig(IsBuiltInProxy ? "SOCKS5" : ProxyType, testIp, proxyPortNum, ProxyUsername ?? "", ProxyPassword ?? "");
                    await System.Threading.Tasks.Task.Run(() => TestOutput = _proxyService.TestConnection(TestTargetHost, targetPortNum));
                }
                else
                {
                    TestOutput = "ERROR: Proxy service not available";
                }
            }
            catch (Exception ex)
            {
                TestOutput += $"\nERROR: {ex.Message}";
            }
            finally
            {
                IsTesting = false;
            }
        });
    }

    private bool Validate()
    {
        IpError = "";
        PortError = "";
        BuiltInError = "";
        return IsBuiltInProxy ? ValidateBuiltIn() : ValidateExternal();
    }

    private bool ValidateExternal()
    {
        return ValidationHelper.ValidateIpOrDomain(ProxyIp, IsValidIpOrDomain, msg => IpError = msg)
            && ValidationHelper.ValidatePort(ProxyPort, msg => PortError = msg);
    }

    private bool ValidateBuiltIn()
    {
        if (!ValidationHelper.ValidatePort(MixedPort, msg => BuiltInError = msg))
            return false;

        if (!ValidationHelper.ValidatePort(ControllerPort, msg => BuiltInError = msg))
            return false;

        if (EnableController && string.IsNullOrWhiteSpace(ControllerPort))
        {
            BuiltInError = "Controller port is required when controller API is enabled.";
            return false;
        }

        var hasProfileSource = !string.IsNullOrWhiteSpace(SubscriptionUrl) || !string.IsNullOrWhiteSpace(LocalYamlPath);
        var hasActiveProfile = !string.IsNullOrWhiteSpace(ActiveProfilePath) &&
            File.Exists(MihomoService.ResolvePortablePath(ActiveProfilePath));

        if (!hasProfileSource && !hasActiveProfile)
        {
            BuiltInError = "Add a subscription URL, import a local YAML profile, or keep a loaded active profile.";
            return false;
        }

        return true;
    }

    private bool ValidateController()
    {
        BuiltInError = "";
        if (!EnableController)
        {
            BuiltInError = "Enable Controller API first.";
            return false;
        }

        return ValidationHelper.ValidatePort(ControllerPort, msg => BuiltInError = msg);
    }

    private bool ValidateGeoSettings()
    {
        BuiltInError = "";
        return ValidationHelper.ValidatePort(GeoUpdateInterval, msg => BuiltInError = msg);
    }

    public async System.Threading.Tasks.Task ImportYamlProfileAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return;

        IsTesting = true;
        BuiltInError = "Importing YAML profile...";
        try
        {
            var config = CreateBuiltInConfig();
            if (await MihomoService.ImportLocalProfileAsync(sourcePath, config, msg => BuiltInError = msg))
            {
                LocalYamlPath = config.LocalYamlPath;
                ActiveProfilePath = config.ActiveProfilePath;
                await RefreshProfileStatusAsync();
                BuiltInError = "YAML profile imported.";
            }
        }
        catch (Exception ex)
        {
            BuiltInError = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    private async System.Threading.Tasks.Task LoadProxyChoicesAsync(bool updateStatus)
    {
        IsTesting = true;
        if (updateStatus)
            BuiltInError = "Loading proxy choices...";

        try
        {
            var config = CreateBuiltInConfig();
            ProxyChoice[] choices;

            if (EnableController && ushort.TryParse(ControllerPort, out _))
            {
                try
                {
                    choices = await MihomoService.GetProxyChoicesAsync(config);
                }
                catch
                {
                    choices = await MihomoService.GetProfileProxyChoicesAsync(config);
                }
            }
            else
            {
                choices = await MihomoService.GetProfileProxyChoicesAsync(config);
            }

            ProxyChoices.Clear();
            foreach (var choice in choices)
                ProxyChoices.Add(choice);

            if (ProxyChoices.Count > 0)
            {
                SelectedProxyChoice = ProxyChoices.FirstOrDefault(c =>
                    c.ProxyName.Equals(SelectedProxyName, StringComparison.OrdinalIgnoreCase))
                    ?? ProxyChoices[0];
            }

            if (updateStatus)
            {
                BuiltInError = ProxyChoices.Count == 0
                    ? "No selectable servers found. Check that the profile has proxies and proxy-groups."
                    : $"Loaded {ProxyChoices.Count} proxy choices.";
            }
        }
        catch (Exception ex)
        {
            if (updateStatus)
                BuiltInError = $"Load proxies failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    private ProxySettingsResult CreateResult()
    {
        return new ProxySettingsResult
        {
            ProxyEngine = ProxyEngine,
            ProxyType = ProxyType,
            ProxyIp = ProxyIp,
            ProxyPort = ProxyPort,
            ProxyUsername = ProxyUsername ?? "",
            ProxyPassword = ProxyPassword ?? "",
            BuiltInProxy = CreateBuiltInConfig()
        };
    }

    private BuiltInProxyConfig CreateBuiltInConfig()
    {
        return new BuiltInProxyConfig
        {
            CorePath = CorePath,
            SubscriptionUrl = SubscriptionUrl,
            LocalYamlPath = LocalYamlPath,
            ActiveProfilePath = ActiveProfilePath,
            MixedPort = MixedPort,
            ControllerPort = ControllerPort,
            ControllerSecret = ControllerSecret,
            Mode = Mode,
            LogLevel = LogLevel,
            AllowLan = AllowLan,
            Ipv6 = Ipv6,
            UnifiedDelay = UnifiedDelay,
            EnableController = EnableController,
            AutoUpdateSubscription = AutoUpdateSubscription,
            GeoAutoUpdate = GeoAutoUpdate,
            GeoUpdateInterval = GeoUpdateInterval,
            GeoIpUrl = GeoIpUrl,
            GeoSiteUrl = GeoSiteUrl,
            MmdbUrl = MmdbUrl,
            AsnUrl = AsnUrl,
            SelectorGroup = SelectorGroup,
            SelectedProxyName = SelectedProxyName,
            RulePreset = RulePreset,
            OverrideDns = OverrideDns,
            DefaultNameservers = DefaultNameservers,
            Nameservers = Nameservers,
            ProxyServerNameservers = ProxyServerNameservers
        };
    }

    private async System.Threading.Tasks.Task RefreshProfileStatusAsync()
    {
        try
        {
            ProfileStatus = await MihomoService.GetProfileSummaryAsync(CreateBuiltInConfig());
        }
        catch (Exception ex)
        {
            ProfileStatus = $"Profile status unavailable: {ex.Message}";
        }
    }

    private static bool IsValidIpOrDomain(string input)
    {
        if (IPAddress.TryParse(input, out _))
            return true;

        var domainRegex = new Regex(@"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)*[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?$");
        return domainRegex.IsMatch(input);
    }
}
