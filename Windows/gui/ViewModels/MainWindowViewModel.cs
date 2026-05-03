using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using JackBridge.GUI.Views;
using JackBridge.GUI.Services;
using JackBridge.GUI.Common;

namespace JackBridge.GUI.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private const int MAX_CONNECTION_LOG_LINES = 100;
    private const int MAX_ACTIVITY_LOG_LINES = 100;

    private string _title = "JackBridge v2.0 Beta";
    private int _selectedTabIndex;
    private string _connectionsLog = "";
    private string _activityLog = "";
    private string _connectionsSearchText = "";
    private string _activitySearchText = "";
    private string _filteredConnectionsLog = "";
    private string _filteredActivityLog = "";
    private bool _isProxyRulesDialogOpen;
    private bool _isProxySettingsDialogOpen;
    private bool _isAddRuleViewOpen;
    private bool _isPanelVisible;
    private object? _activePanelViewModel;
    private Thickness _panelMargin = new(0, 0, -560, 0);
    private double _panelWidth = 560;
    private int _panelTransitionVersion;
    private string _newProcessName = "";
    private string _newProxyAction = "PROXY";
    private bool _startWithWindows;
    private bool _isProxyEnabled = false;
    private Window? _mainWindow;
    private JackBridgeService? _proxyService;
    private MihomoService? _mihomoService;
    private bool _isServiceInitialized = false;
    private readonly SettingsService _settingsService = new SettingsService();

    private string _currentProxyType = "SOCKS5";
    private string _currentProxyIp = "";
    private string _currentProxyPort = "";
    private string _currentProxyUsername = "";
    private string _currentProxyPassword = "";
    private string _proxyEngine = "External";
    private BuiltInProxyConfig _builtInProxy = new();

    public ObservableCollection<ObservedProcessRuleCandidate> ObservedProcesses { get; } = new();
    public bool HasObservedProcesses => ObservedProcesses.Count > 0;

    private readonly List<string> _pendingConnectionLogs = new(128);
    private readonly List<string> _pendingActivityLogs = new(64);
    private readonly object _connectionLogLock = new();
    private readonly object _activityLogLock = new();
    private DispatcherTimer? _connectionLogTimer;
    private DispatcherTimer? _activityLogTimer;

    public void SetMainWindow(Window window)
    {
        _mainWindow = window;

        if (_isServiceInitialized)
            return;

        _isServiceInitialized = true;
        LoadConfiguration();

        try
        {
            _proxyService = new JackBridgeService();
            _mihomoService = new MihomoService();
            _proxyService.LogReceived += (msg) =>
            {
                lock (_activityLogLock)
                {
                    _pendingActivityLogs.Add($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
                }
            };

            _proxyService.ConnectionReceived += (processName, pid, destIp, destPort, proxyInfo) =>
            {
                if (!_isTrafficLoggingEnabled)
                    return;

                if (_connectionLogTimer?.IsEnabled == false)
                    _connectionLogTimer.Start();

                string logEntry = $"[{DateTime.Now:HH:mm:ss}] {processName} (PID:{pid}) -> {destIp}:{destPort} via {proxyInfo}\n";
                lock (_connectionLogLock)
                {
                    _pendingConnectionLogs.Add(logEntry);
                }

                Dispatcher.UIThread.Post(() =>
                {
                    RegisterObservedProcess(processName, pid, destIp, destPort);
                });
            };

            _connectionLogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _connectionLogTimer.Tick += (s, e) =>
            {
                List<string> logsToAdd;
                lock (_connectionLogLock)
                {
                    if (_pendingConnectionLogs.Count == 0) return;
                    logsToAdd = new List<string>(_pendingConnectionLogs);
                    _pendingConnectionLogs.Clear();
                }

                ConnectionsLog += string.Join("", logsToAdd);

                var lines = ConnectionsLog.Split('\n');
                if (lines.Length > MAX_CONNECTION_LOG_LINES)
                {
                    var linesToKeep = lines.Skip(lines.Length - MAX_CONNECTION_LOG_LINES).ToArray();
                    ConnectionsLog = string.Join("\n", linesToKeep);
                }
            };

            _activityLogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _activityLogTimer.Tick += (s, e) =>
            {
                List<string> logsToAdd;
                lock (_activityLogLock)
                {
                    if (_pendingActivityLogs.Count == 0) return;
                    logsToAdd = new List<string>(_pendingActivityLogs);
                    _pendingActivityLogs.Clear();
                }
                ActivityLog += string.Join("", logsToAdd);
            };
            _activityLogTimer.Start();

            ApplyProxyRuntimeConfiguration();
            LoadRulesIntoNativeService();

            if (!_isProxyEnabled)
            {
                QueueActivityLog("Proxy is disabled");
            }
            else if (StartProxyService())
            {
                QueueActivityLog("Proxy is enabled");
            }
            else
            {
                QueueActivityLog("ERROR: Failed to start JackBridge service");
            }
        }
        catch (Exception ex)
        {
            QueueActivityLog($"ERROR: {ex.Message}");
        }

    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public string ConnectionsLog
    {
        get => _connectionsLog;
        set
        {
            if (SetProperty(ref _connectionsLog, value))
            {
                if (string.IsNullOrWhiteSpace(_connectionsSearchText))
                    FilteredConnectionsLog = _connectionsLog;
            }
        }
    }

    public string ActivityLog
    {
        get => _activityLog;
        set
        {
            if (SetProperty(ref _activityLog, value))
            {
                if (!string.IsNullOrEmpty(_activityLog))
                {
                    var lines = _activityLog.Split('\n');
                    if (lines.Length > MAX_ACTIVITY_LOG_LINES)
                    {
                        var oldLog = _activityLog;
                        var linesToKeep = lines.Skip(lines.Length - MAX_ACTIVITY_LOG_LINES).ToArray();
                        _activityLog = string.Join("\n", linesToKeep);
                        oldLog = null!;
                    }
                }

                if (string.IsNullOrWhiteSpace(_activitySearchText))
                    FilteredActivityLog = _activityLog;
            }
        }
    }



    public bool IsProxyRulesDialogOpen
    {
        get => _isProxyRulesDialogOpen;
        set => SetProperty(ref _isProxyRulesDialogOpen, value);
    }

    public bool IsProxySettingsDialogOpen
    {
        get => _isProxySettingsDialogOpen;
        set => SetProperty(ref _isProxySettingsDialogOpen, value);
    }

    public bool IsAddRuleViewOpen
    {
        get => _isAddRuleViewOpen;
        set => SetProperty(ref _isAddRuleViewOpen, value);
    }

    public bool IsPanelVisible
    {
        get => _isPanelVisible;
        set => SetProperty(ref _isPanelVisible, value);
    }

    public object? ActivePanelViewModel
    {
        get => _activePanelViewModel;
        set => SetProperty(ref _activePanelViewModel, value);
    }

    public Thickness PanelMargin
    {
        get => _panelMargin;
        set => SetProperty(ref _panelMargin, value);
    }

    public double PanelWidth
    {
        get => _panelWidth;
        set => SetProperty(ref _panelWidth, value);
    }

    public string ConnectionsSearchText
    {
        get => _connectionsSearchText;
        set => SetProperty(ref _connectionsSearchText, value);
    }

    public string ActivitySearchText
    {
        get => _activitySearchText;
        set => SetProperty(ref _activitySearchText, value);
    }

    public string FilteredConnectionsLog
    {
        get => _filteredConnectionsLog;
        set => SetProperty(ref _filteredConnectionsLog, value);
    }

    public string FilteredActivityLog
    {
        get => _filteredActivityLog;
        set => SetProperty(ref _filteredActivityLog, value);
    }

    public string NewProcessName
    {
        get => _newProcessName;
        set => SetProperty(ref _newProcessName, value);
    }

    public string NewProxyAction
    {
        get => _newProxyAction;
        set => SetProperty(ref _newProxyAction, value);
    }

    public ObservableCollection<ProxyRule> ProxyRules { get; } = new();

    private bool _dnsViaProxy = true;
    public bool DnsViaProxy
    {
        get => _dnsViaProxy;
        set
        {
            if (SetProperty(ref _dnsViaProxy, value))
            {
                _proxyService?.SetDnsViaProxy(value);
                SaveConfigurationInternal();

            }
        }
    }

    private bool _localhostViaProxy = false;  // Default: disabled for security
    public bool LocalhostViaProxy
    {
        get => _localhostViaProxy;
        set
        {
            if (SetProperty(ref _localhostViaProxy, value))
            {
                _proxyService?.SetLocalhostViaProxy(value);
                SaveConfigurationInternal();
            }
        }
    }

    private bool _isTrafficLoggingEnabled = true;
    public bool IsTrafficLoggingEnabled
    {
        get => _isTrafficLoggingEnabled;
        set
        {
            if (SetProperty(ref _isTrafficLoggingEnabled, value))
            {
                if (value)
                {
                    JackBridgeService.SetTrafficLoggingEnabled(true);
                    _connectionLogTimer?.Start();
                }
                else
                {
                    _connectionLogTimer?.Stop();
                    lock (_connectionLogLock)
                    {
                        _pendingConnectionLogs.Clear();
                    }

                    JackBridgeService.SetTrafficLoggingEnabled(false);

                    ConnectionsLog = null!;
                    FilteredConnectionsLog = null!;
                    ConnectionsLog = "";
                    FilteredConnectionsLog = "";

                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                }
                SaveConfigurationInternal();
            }
        }
    }

    public bool IsProxyEnabled
    {
        get => _isProxyEnabled;
        set
        {
            if (SetProperty(ref _isProxyEnabled, value))
            {
                OnPropertyChanged(nameof(ProxyStatusText));
                OnPropertyChanged(nameof(ProxyToggleText));

                if (_proxyService != null)
                {
                    if (value)
                    {
                        ApplyProxyRuntimeConfiguration();
                        if (StartProxyService())
                            QueueActivityLog("Proxy enabled");
                        else
                            QueueActivityLog("ERROR: Failed to enable proxy");
                    }
                    else
                    {
                        _mihomoService?.Stop(QueueActivityLog);
                        if (_proxyService.Stop())
                            QueueActivityLog("Proxy disabled");
                        else
                            QueueActivityLog("ERROR: Failed to disable proxy");
                    }
                }

                SaveConfigurationInternal();
            }
        }
    }

    public string ProxyStatusText => IsProxyEnabled ? "Proxy On" : "Proxy Off";
    public string ProxyToggleText => IsProxyEnabled ? "Turn Proxy Off" : "Turn Proxy On";

    private bool _closeToTray = true;
    public bool CloseToTray
    {
        get => _closeToTray;
        set => SetProperty(ref _closeToTray, value);
    }

    private readonly Loc _loc = Loc.Instance;
    public Loc Loc => _loc;

    private string _currentLanguage = "en";
    private string _englishCheckmark = "✓";
    private string _chineseCheckmark = "";

    public string EnglishCheckmark
    {
        get => _englishCheckmark;
        set => SetProperty(ref _englishCheckmark, value);
    }

    public string ChineseCheckmark
    {
        get => _chineseCheckmark;
        set => SetProperty(ref _chineseCheckmark, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public ICommand ShowProxySettingsCommand { get; }
    public ICommand ShowProxyRulesCommand { get; }
    public ICommand ShowAboutCommand { get; }
    public ICommand ToggleDnsViaProxyCommand { get; }
    public ICommand ToggleLocalhostViaProxyCommand { get; }
    public ICommand ToggleTrafficLoggingCommand { get; }
    public ICommand ToggleProxyEnabledCommand { get; }
    public ICommand EnableProxyCommand { get; }
    public ICommand DisableProxyCommand { get; }
    public ICommand ToggleCloseToTrayCommand { get; }
    public ICommand ToggleStartWithWindowsCommand { get; }
    public ICommand CloseDialogCommand { get; }
    public ICommand ClosePanelCommand { get; }
    public ICommand ClearConnectionsLogCommand { get; }
    public ICommand ClearActivityLogCommand { get; }
    public ICommand SearchConnectionsCommand { get; }
    public ICommand SearchActivityCommand { get; }
    public ICommand AddRuleCommand { get; }
    public ICommand AddObservedProcessRuleCommand { get; }
    public ICommand SaveNewRuleCommand { get; }
    public ICommand CancelAddRuleCommand { get; }

    public MainWindowViewModel()
    {
        ShowProxySettingsCommand = new RelayCommand(() =>
        {
            var viewModel = new ProxySettingsViewModel(
                initialEngine: _proxyEngine,
                initialType: _currentProxyType,
                initialIp: _currentProxyIp,
                initialPort: _currentProxyPort,
                initialUsername: _currentProxyUsername,
                initialPassword: _currentProxyPassword,
                initialBuiltInProxy: _builtInProxy,
                onSave: (settings) =>
                {
                    _proxyEngine = settings.ProxyEngine;
                    _currentProxyType = settings.ProxyType;
                    _currentProxyIp = settings.ProxyIp;
                    _currentProxyPort = settings.ProxyPort;
                    _currentProxyUsername = settings.ProxyUsername;
                    _currentProxyPassword = settings.ProxyPassword;
                    _builtInProxy = settings.BuiltInProxy;

                    if (_proxyService != null && !_proxyEngine.Equals("BuiltIn", StringComparison.OrdinalIgnoreCase) && ushort.TryParse(_currentProxyPort, out ushort portNum))
                    {
                        if (!_proxyService.SetProxyConfig(_currentProxyType, _currentProxyIp, portNum, _currentProxyUsername, _currentProxyPassword))
                        {
                            QueueActivityLog("ERROR: Failed to set proxy config");
                        }
                    }

                    if (_isProxyEnabled)
                    {
                        _proxyService?.Stop();
                        _mihomoService?.Stop(QueueActivityLog);
                        ApplyProxyRuntimeConfiguration();
                        if (StartProxyService())
                            QueueActivityLog("Proxy settings applied");
                        else
                            QueueActivityLog("ERROR: Failed to apply proxy settings");
                    }

                    SaveConfigurationInternal();
                    ClosePanel();
                },
                onClose: () =>
                {
                    ClosePanel();
                },
                proxyService: _proxyService
            );

            OpenPanel(viewModel, 640);
        });

        ShowProxyRulesCommand = new RelayCommand(() =>
        {
            Window? rulesWindow = null;

            var viewModel = new ProxyRulesViewModel(
                proxyRules: ProxyRules,
                onAddRule: (rule) =>
                {
                    if (_proxyService != null)
                    {
                        uint ruleId = _proxyService.AddRule(
                            rule.ProcessName,
                            rule.TargetHosts,
                            rule.TargetPorts,
                            rule.Protocol,
                            rule.Action);
                        if (ruleId > 0)
                        {
                            rule.RuleId = ruleId;
                            InsertRuleInPriorityOrder(rule);
                            _proxyService.MoveRuleToPosition(rule.RuleId, (uint)rule.Index);
                            SaveConfigurationInternal();
                        }
                        else
                        {
                            QueueActivityLog("ERROR: Failed to add rule");
                        }
                    }
                },
                onClose: () =>
                {
                    rulesWindow?.Close();
                },
                proxyService: _proxyService,
                onConfigChanged: SaveConfigurationInternal
            );

            rulesWindow = new Window
            {
                Title = "Process Rules - JackBridge v2.0 Beta",
                Width = 720,
                Height = 560,
                MinWidth = 640,
                MinHeight = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = _mainWindow?.Icon,
                Content = new ProxyRulesWindow
                {
                    DataContext = viewModel
                }
            };

            viewModel.SetWindow(rulesWindow);

            if (_mainWindow != null)
            {
                rulesWindow.Show(_mainWindow);
            }
            else
            {
                rulesWindow.Show();
            }
        });

        ShowAboutCommand = new RelayCommand(() =>
        {
            OpenPanel(new AboutViewModel(ClosePanel), 520);
        });

        ToggleDnsViaProxyCommand = new RelayCommand(() =>
        {
            DnsViaProxy = !DnsViaProxy;
        });

        ToggleLocalhostViaProxyCommand = new RelayCommand(() =>
        {
            LocalhostViaProxy = !LocalhostViaProxy;
        });

        ToggleTrafficLoggingCommand = new RelayCommand(() =>
        {
            IsTrafficLoggingEnabled = !IsTrafficLoggingEnabled;
        });

        ToggleProxyEnabledCommand = new RelayCommand(() =>
        {
            IsProxyEnabled = !IsProxyEnabled;
        });

        EnableProxyCommand = new RelayCommand(() =>
        {
            IsProxyEnabled = true;
        });

        DisableProxyCommand = new RelayCommand(() =>
        {
            IsProxyEnabled = false;
        });

        ToggleCloseToTrayCommand = new RelayCommand(() =>
        {
            CloseToTray = !CloseToTray;
            SaveConfigurationInternal();
        });

        ToggleStartWithWindowsCommand = new RelayCommand(() =>
        {
            StartWithWindows = !StartWithWindows;
            var settings = _settingsService.LoadSettings();
            settings.StartWithWindows = StartWithWindows;
            _settingsService.SaveSettings(settings);
            _settingsService.SetStartupWithWindows(StartWithWindows);
        });

        CloseDialogCommand = new RelayCommand(CloseDialogs);
        ClosePanelCommand = new RelayCommand(ClosePanel);

        ClearConnectionsLogCommand = new RelayCommand(() =>
        {
            lock (_connectionLogLock)
            {
                _pendingConnectionLogs.Clear();
            }

            ConnectionsLog = null!;
            ConnectionsSearchText = null!;
            FilteredConnectionsLog = null!;

            ConnectionsLog = "";
            ConnectionsSearchText = "";
            FilteredConnectionsLog = "";
            ObservedProcesses.Clear();
            OnPropertyChanged(nameof(HasObservedProcesses));

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        });

        ClearActivityLogCommand = new RelayCommand(() =>
        {
            lock (_activityLogLock)
            {
                _pendingActivityLogs.Clear();
            }

            ActivityLog = "";
            ActivitySearchText = "";
            FilteredActivityLog = "";

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        });

        SearchConnectionsCommand = new RelayCommand(() =>
        {
            FilteredConnectionsLog = FilterLog(_connectionsLog, _connectionsSearchText);
        });

        SearchActivityCommand = new RelayCommand(() =>
        {
            FilteredActivityLog = FilterLog(_activityLog, _activitySearchText);
        });

        AddRuleCommand = new RelayCommand(() =>
        {
            IsAddRuleViewOpen = true;
            NewProcessName = "";
            NewProxyAction = "PROXY";
        });

        AddObservedProcessRuleCommand = new RelayCommandWithParameter<ObservedProcessRuleCandidate>((candidate) =>
        {
            AddRuleFromObservedProcess(candidate);
        });

        SaveNewRuleCommand = new RelayCommand(() =>
        {
            if (string.IsNullOrWhiteSpace(NewProcessName))
            {
                return;
            }

            var rule = new ProxyRule
            {
                ProcessName = NewProcessName,
                TargetHosts = "*",
                TargetPorts = "*",
                Protocol = "TCP",
                Action = NewProxyAction,
                IsEnabled = true,
                IsStatic = false
            };

            if (_proxyService != null)
            {
                var ruleId = _proxyService.AddRule(NewProcessName, "*", "*", "TCP", NewProxyAction);
                if (ruleId > 0)
                {
                    rule.RuleId = ruleId;
                    InsertRuleInPriorityOrder(rule);
                    _proxyService.MoveRuleToPosition(rule.RuleId, (uint)rule.Index);
                    SaveConfigurationInternal();
                    IsAddRuleViewOpen = false;
                    NewProcessName = "";
                }
                else
                {
                    QueueActivityLog("ERROR: Failed to add rule");
                }
            }
        });

        CancelAddRuleCommand = new RelayCommand(() =>
        {
            IsAddRuleViewOpen = false;
            NewProcessName = "";
        });
    }

    public void ChangeLanguage(string languageCode)
    {
        if (string.IsNullOrEmpty(languageCode)) return;

        _currentLanguage = languageCode;
        EnglishCheckmark = languageCode == "en" ? "✓" : "";
        ChineseCheckmark = languageCode == "zh" ? "✓" : "";

        var config = ConfigManager.LoadConfig();
        config.Language = languageCode;
        ConfigManager.SaveConfig(config);

        _loc.CurrentCulture = new System.Globalization.CultureInfo(languageCode);
    }



    private void CloseDialogs()
    {
        IsProxyRulesDialogOpen = false;
        IsProxySettingsDialogOpen = false;
        ClosePanel();
    }

    private void OpenPanel(object viewModel, double width)
    {
        _panelTransitionVersion++;
        double resolvedWidth = ResolvePanelWidth(width);
        PanelWidth = resolvedWidth;
        ActivePanelViewModel = viewModel;
        IsPanelVisible = true;
        PanelMargin = new Thickness(0, 0, -resolvedWidth, 0);

        Dispatcher.UIThread.Post(() =>
        {
            PanelMargin = new Thickness(0);
        }, DispatcherPriority.Background);
    }

    private double ResolvePanelWidth(double requestedWidth)
    {
        if (_mainWindow == null)
            return requestedWidth;

        double availableWidth = Math.Max(420, _mainWindow.Bounds.Width - 56);
        return Math.Min(requestedWidth, availableWidth);
    }

    private async void ClosePanel()
    {
        int transitionVersion = ++_panelTransitionVersion;
        double width = PanelWidth;
        PanelMargin = new Thickness(0, 0, -width, 0);
        await Task.Delay(180);
        if (transitionVersion != _panelTransitionVersion)
            return;

        ActivePanelViewModel = null;
        IsPanelVisible = false;
    }

    private void RegisterObservedProcess(string processName, uint pid, string destIp, ushort destPort)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return;

        var executableName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : $"{processName}.exe";

        var existing = ObservedProcesses.FirstOrDefault(process =>
            process.ProcessName.Equals(executableName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.LastSeen = DateTime.Now;
            existing.HitCount++;
            existing.ProcessId = pid;
            existing.TargetHost = destIp;
            existing.TargetPort = destPort;
            return;
        }

        ObservedProcessRuleCandidate? createdCandidate = null;
        createdCandidate = new ObservedProcessRuleCandidate
        {
            ProcessName = executableName,
            ProcessId = pid,
            TargetHost = destIp,
            TargetPort = destPort,
            LastSeen = DateTime.Now,
            HitCount = 1,
            AddRuleCommand = new RelayCommand(() =>
            {
                if (createdCandidate != null)
                    AddRuleFromObservedProcess(createdCandidate);
            })
        };

        ObservedProcesses.Add(createdCandidate);

        while (ObservedProcesses.Count > 24)
        {
            ObservedProcesses.RemoveAt(ObservedProcesses.Count - 1);
        }

        OnPropertyChanged(nameof(HasObservedProcesses));
    }

    private void AddRuleFromObservedProcess(ObservedProcessRuleCandidate candidate)
    {
        if (candidate == null)
            return;

        var existingRule = ProxyRules.FirstOrDefault(rule =>
            rule.ProcessName.Equals(candidate.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            rule.TargetHosts.Equals(candidate.TargetHost, StringComparison.OrdinalIgnoreCase) &&
            rule.TargetPorts.Equals(candidate.TargetPort.ToString(), StringComparison.OrdinalIgnoreCase));

        if (existingRule != null)
        {
            QueueActivityLog($"Rule already exists for {candidate.ProcessName} -> {candidate.TargetHost}:{candidate.TargetPort}");
            return;
        }

        var rule = new ProxyRule
        {
            ProcessName = candidate.ProcessName,
            TargetHosts = candidate.TargetHost,
            TargetPorts = candidate.TargetPort.ToString(),
            Protocol = "TCP",
            Action = "PROXY",
            IsEnabled = true,
            IsStatic = false,
            Index = ProxyRules.Count + 1
        };

        if (_proxyService == null)
        {
            QueueActivityLog("ERROR: Proxy service is not available");
            return;
        }

        uint ruleId = _proxyService.AddRule(
            rule.ProcessName,
            rule.TargetHosts,
            rule.TargetPorts,
            rule.Protocol,
            rule.Action);

        if (ruleId == 0)
        {
            QueueActivityLog($"ERROR: Failed to add rule for {rule.ProcessName}");
            return;
        }

        rule.RuleId = ruleId;
        InsertRuleInPriorityOrder(rule);
        _proxyService.MoveRuleToPosition(rule.RuleId, (uint)rule.Index);
        SaveConfigurationInternal();
        QueueActivityLog($"Added rule for {rule.ProcessName} -> {rule.TargetHosts}:{rule.TargetPorts}");
    }

    private void InsertRuleInPriorityOrder(ProxyRule rule)
    {
        int insertIndex = rule.IsStatic
            ? ProxyRules.Count
            : ProxyRules.TakeWhile(existingRule => !existingRule.IsStatic).Count();

        ProxyRules.Insert(insertIndex, rule);

        for (int i = 0; i < ProxyRules.Count; i++)
        {
            ProxyRules[i].Index = i + 1;
        }

        RefreshRuleSectionPriorities();
    }

    private void RefreshRuleSectionPriorities()
    {
        int activeIndex = 1;
        int staticIndex = 1;

        foreach (var rule in ProxyRules)
        {
            rule.SectionIndex = rule.IsStatic ? staticIndex++ : activeIndex++;
        }
    }

    public void Cleanup()
    {
        try { SaveConfigurationInternal(); } catch { }
        try { _mihomoService?.Dispose(); _mihomoService = null; } catch { }
        try { _proxyService?.Dispose(); _proxyService = null; } catch { }
    }

    private void ApplyProxyRuntimeConfiguration()
    {
        if (_proxyService == null)
            return;

        _proxyService.SetDnsViaProxy(_dnsViaProxy);
        _proxyService.SetLocalhostViaProxy(_localhostViaProxy);

        if (_proxyEngine.Equals("BuiltIn", StringComparison.OrdinalIgnoreCase))
        {
            if (ushort.TryParse(_builtInProxy.MixedPort, out ushort builtInPort))
            {
                _proxyService.SetProxyConfig("SOCKS5", "127.0.0.1", builtInPort, "", "");
            }
            return;
        }

        if (!string.IsNullOrEmpty(_currentProxyIp) &&
            !string.IsNullOrEmpty(_currentProxyPort) &&
            ushort.TryParse(_currentProxyPort, out ushort portNum))
        {
            _proxyService.SetProxyConfig(
                _currentProxyType,
                _currentProxyIp,
                portNum,
                _currentProxyUsername,
                _currentProxyPassword);
        }
    }

    private void LoadRulesIntoNativeService()
    {
        if (_proxyService == null)
            return;

        for (int i = 0; i < ProxyRules.Count; i++)
        {
            var rule = ProxyRules[i];
            rule.Index = i + 1;

            if (rule.RuleId > 0)
                continue;

            uint ruleId = _proxyService.AddRule(
                rule.ProcessName,
                rule.TargetHosts,
                rule.TargetPorts,
                rule.Protocol,
                rule.Action);

            if (ruleId > 0)
            {
                rule.RuleId = ruleId;

                if (!rule.IsEnabled)
                    _proxyService.DisableRule(ruleId);
            }
        }

        RefreshRuleSectionPriorities();
    }

    private bool StartProxyService()
    {
        if (_proxyService == null)
            return false;

        if (_proxyEngine.Equals("BuiltIn", StringComparison.OrdinalIgnoreCase))
        {
            _mihomoService ??= new MihomoService();
            if (!_mihomoService.StartAsync(_builtInProxy, QueueActivityLog).GetAwaiter().GetResult())
                return false;

            ApplyProxyRuntimeConfiguration();
        }

        LoadRulesIntoNativeService();
        return _proxyService.Start();
    }

    private string FilterLog(string log, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return log;

        var sb = new StringBuilder(log.Length / 2);
        var lines = log.Split('\n');

        foreach (var line in lines)
        {
            if (line.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(line);
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    private void LoadConfiguration()
    {
        try
        {
            var settings = _settingsService.LoadSettings();
            StartWithWindows = settings.StartWithWindows && _settingsService.IsStartupEnabled();

            var config = ConfigManager.LoadConfig();

            _proxyEngine = string.IsNullOrWhiteSpace(config.ProxyEngine) ? "External" : config.ProxyEngine;
            _currentProxyType = ValidationHelper.DefaultIfEmpty(config.ProxyType, "SOCKS5");
            _currentProxyIp = ValidationHelper.DefaultIfEmpty(config.ProxyIp, "");
            _currentProxyPort = ValidationHelper.DefaultIfEmpty(config.ProxyPort, "");
            _currentProxyUsername = config.ProxyUsername ?? "";
            _currentProxyPassword = config.ProxyPassword ?? "";
            _builtInProxy = config.BuiltInProxy ?? new BuiltInProxyConfig();

            DnsViaProxy = config.DnsViaProxy;
            LocalhostViaProxy = config.LocalhostViaProxy;
            IsProxyEnabled = config.IsProxyEnabled;
            CloseToTray = config.CloseToTray;
            IsTrafficLoggingEnabled = config.IsTrafficLoggingEnabled;

            if (!string.IsNullOrWhiteSpace(config.Language))
            {
                _currentLanguage = config.Language;
                _loc.CurrentCulture = new System.Globalization.CultureInfo(config.Language);
                EnglishCheckmark = config.Language == "en" ? "✓" : "";
                ChineseCheckmark = config.Language == "zh" ? "✓" : "";
            }

            if (config.ProxyRules != null && config.ProxyRules.Count > 0)
            {
                foreach (var ruleConfig in config.ProxyRules)
                {
                    if (string.IsNullOrWhiteSpace(ruleConfig.ProcessName))
                        continue;

                    var rule = new ProxyRule
                    {
                        ProcessName = ruleConfig.ProcessName,
                        TargetHosts = ValidationHelper.DefaultIfEmpty(ruleConfig.TargetHosts),
                        TargetPorts = ValidationHelper.DefaultIfEmpty(ruleConfig.TargetPorts),
                        Protocol = ValidationHelper.DefaultIfEmpty(ruleConfig.Protocol, "TCP"),
                        Action = ValidationHelper.DefaultIfEmpty(ruleConfig.Action, "PROXY"),
                        IsEnabled = ruleConfig.IsEnabled,
                        IsStatic = ruleConfig.IsStatic || ruleConfig.ProcessName.Trim() == "*",
                        Index = ProxyRules.Count + 1
                    };
                    InsertRuleInPriorityOrder(rule);
                }
            }

            QueueActivityLog("Configuration loaded successfully");
        }
        catch (Exception ex)
        {
            QueueActivityLog($"Failed to load configuration: {ex.Message}");
        }
    }

    private void SaveConfigurationInternal()
    {
        Task.Run(() => SaveConfigurationInternalAsync());
    }

    private void SaveConfigurationInternalAsync()
    {
        try
        {
            var config = new AppConfig
            {
                ProxyEngine = _proxyEngine,
                ProxyType = _currentProxyType,
                ProxyIp = _currentProxyIp,
                ProxyPort = _currentProxyPort,
                ProxyUsername = _currentProxyUsername,
                ProxyPassword = _currentProxyPassword,
                IsProxyEnabled = _isProxyEnabled,
                DnsViaProxy = _dnsViaProxy,
                LocalhostViaProxy = _localhostViaProxy,
                IsTrafficLoggingEnabled = _isTrafficLoggingEnabled,
                Language = _currentLanguage,
                CloseToTray = _closeToTray,
                BuiltInProxy = _builtInProxy,
                ProxyRules = ProxyRules.Select(r => new ProxyRuleConfig
                {
                    ProcessName = r.ProcessName,
                    TargetHosts = r.TargetHosts,
                    TargetPorts = r.TargetPorts,
                    Protocol = r.Protocol,
                    Action = r.Action,
                    IsEnabled = r.IsEnabled,
                    IsStatic = r.IsStatic
                }).ToList()
            };

            ConfigManager.SaveConfig(config);
        }
        catch { }
    }

    private void QueueActivityLog(string message)
    {
        var logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}\n";

        if (_activityLogTimer == null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ActivityLog += logEntry;
            });
            return;
        }

        lock (_activityLogLock)
        {
            _pendingActivityLogs.Add(logEntry);
        }
    }
}

public class ProxyRule : ViewModelBase
{
    private string _processName = "*";
    private string _targetHosts = "*";
    private string _targetPorts = "*";
    private string _protocol = "TCP";
    private string _action = "PROXY";
    private bool _isEnabled = true;
    private bool _isSelected = false;
    private bool _isStatic = false;
    private int _index;
    private int _sectionIndex;
    private uint _ruleId;

    public int Index
    {
        get => _index;
        set => SetProperty(ref _index, value);
    }

    public int SectionIndex
    {
        get => _sectionIndex;
        set => SetProperty(ref _sectionIndex, value);
    }

    public uint RuleId
    {
        get => _ruleId;
        set => SetProperty(ref _ruleId, value);
    }

    public string ProcessName
    {
        get => _processName;
        set => SetProperty(ref _processName, value);
    }

    public string TargetHosts
    {
        get => _targetHosts;
        set => SetProperty(ref _targetHosts, value);
    }

    public string TargetPorts
    {
        get => _targetPorts;
        set => SetProperty(ref _targetPorts, value);
    }

    public string Protocol
    {
        get => _protocol;
        set => SetProperty(ref _protocol, value);
    }

    public string Action
    {
        get => _action;
        set => SetProperty(ref _action, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsStatic
    {
        get => _isStatic;
        set => SetProperty(ref _isStatic, value);
    }
}

public class ObservedProcessRuleCandidate : ViewModelBase
{
    private string _targetHost = "";
    private ushort _targetPort;
    private uint _processId;
    private int _hitCount;
    private DateTime _lastSeen;

    public string ProcessName { get; set; } = "";
    public ICommand? AddRuleCommand { get; set; }

    public string TargetHost
    {
        get => _targetHost;
        set
        {
            if (SetProperty(ref _targetHost, value))
            {
                OnPropertyChanged(nameof(TargetText));
            }
        }
    }

    public ushort TargetPort
    {
        get => _targetPort;
        set
        {
            if (SetProperty(ref _targetPort, value))
            {
                OnPropertyChanged(nameof(TargetText));
            }
        }
    }

    public uint ProcessId
    {
        get => _processId;
        set => SetProperty(ref _processId, value);
    }

    public int HitCount
    {
        get => _hitCount;
        set => SetProperty(ref _hitCount, value);
    }

    public DateTime LastSeen
    {
        get => _lastSeen;
        set
        {
            if (SetProperty(ref _lastSeen, value))
            {
                OnPropertyChanged(nameof(LastSeenText));
            }
        }
    }

    public string TargetText => $"{TargetHost}:{TargetPort}";
    public string LastSeenText => LastSeen.ToString("HH:mm:ss");
}
