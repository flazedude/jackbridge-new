using System;
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
    private string _mixedPort = "7892";
    private string _controllerPort = "9090";
    private string _controllerSecret = "";
    private bool _autoUpdateSubscription;
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
    public string ControllerPort { get => _controllerPort; set { SetProperty(ref _controllerPort, value); BuiltInError = ""; } }
    public bool AutoUpdateSubscription { get => _autoUpdateSubscription; set => SetProperty(ref _autoUpdateSubscription, value); }
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
        MixedPort = string.IsNullOrWhiteSpace(initialBuiltInProxy.MixedPort) ? "7892" : initialBuiltInProxy.MixedPort;
        ControllerPort = string.IsNullOrWhiteSpace(initialBuiltInProxy.ControllerPort) ? "9090" : initialBuiltInProxy.ControllerPort;
        _controllerSecret = initialBuiltInProxy.ControllerSecret;
        AutoUpdateSubscription = initialBuiltInProxy.AutoUpdateSubscription;

        UseExternalProxyCommand = new RelayCommand(() => ProxyEngine = "External");
        UseBuiltInProxyCommand = new RelayCommand(() => ProxyEngine = "BuiltIn");
        SaveCommand = new RelayCommand(() => { if (Validate()) _onSave?.Invoke(CreateResult()); });
        CancelCommand = new RelayCommand(() => _onClose?.Invoke());
        OpenTestCommand = new RelayCommand(() => { IsTestViewOpen = true; TestOutput = ""; });
        CloseTestCommand = new RelayCommand(() => IsTestViewOpen = false);
        RefreshProfileCommand = new RelayCommand(async () =>
        {
            if (!ValidateBuiltIn()) return;
            IsTesting = true;
            BuiltInError = "Refreshing profile...";
            try
            {
                var service = new MihomoService();
                var ok = await service.RefreshProfileAsync(CreateBuiltInConfig(), msg => BuiltInError = msg);
                if (ok) BuiltInError = "Profile refreshed.";
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

        if (string.IsNullOrWhiteSpace(SubscriptionUrl) && string.IsNullOrWhiteSpace(LocalYamlPath))
        {
            BuiltInError = "Add a subscription URL or local YAML profile.";
            return false;
        }

        return true;
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
            ControllerSecret = _controllerSecret,
            AutoUpdateSubscription = AutoUpdateSubscription
        };
    }

    private static bool IsValidIpOrDomain(string input)
    {
        if (IPAddress.TryParse(input, out _))
            return true;

        var domainRegex = new Regex(@"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)*[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?$");
        return domainRegex.IsMatch(input);
    }
}
