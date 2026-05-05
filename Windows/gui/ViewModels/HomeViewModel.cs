using System;
using System.Collections.Generic;
using System.Windows.Input;
using JackBridge.GUI.Common;

namespace JackBridge.GUI.ViewModels;

public class HomeViewModel : ViewModelBase
{
    private bool _isProxyEnabled;
    private string _proxyStatusText = "Proxy Off";
    private string _proxyStatusColor = "#FFE81123";
    private string _engineType = "External";
    private int _activeRulesCount;
    private string _recentActivity = "";
    private string _recentConnections = "";
    private bool _isTrafficLoggingEnabled = true;
    private bool _isMihomoLoggingEnabled = true;
    private int _mihomoLogLevelIndex = 3; // "error"

    public bool IsProxyEnabled
    {
        get => _isProxyEnabled;
        set
        {
            SetProperty(ref _isProxyEnabled, value);
            ProxyStatusText = value ? "Proxy On" : "Proxy Off";
            ProxyStatusColor = value ? "#FF147A45" : "#FFE81123";
        }
    }

    public string ProxyStatusText
    {
        get => _proxyStatusText;
        set => SetProperty(ref _proxyStatusText, value);
    }

    public string ProxyStatusColor
    {
        get => _proxyStatusColor;
        set => SetProperty(ref _proxyStatusColor, value);
    }

    public string EngineType
    {
        get => _engineType;
        set => SetProperty(ref _engineType, value);
    }

    public int ActiveRulesCount
    {
        get => _activeRulesCount;
        set => SetProperty(ref _activeRulesCount, value);
    }

    public string RecentActivity
    {
        get => _recentActivity;
        set => SetProperty(ref _recentActivity, value);
    }

    public string RecentConnections
    {
        get => _recentConnections;
        set => SetProperty(ref _recentConnections, value);
    }

    public bool IsTrafficLoggingEnabled
    {
        get => _isTrafficLoggingEnabled;
        set
        {
            if (SetProperty(ref _isTrafficLoggingEnabled, value))
                OnTrafficLoggingChanged?.Invoke();
        }
    }

    public bool IsMihomoLoggingEnabled
    {
        get => _isMihomoLoggingEnabled;
        set
        {
            if (SetProperty(ref _isMihomoLoggingEnabled, value))
                OnPropertyChanged(nameof(MihomoLoggingLabel));
        }
    }

    public string MihomoLoggingLabel => IsMihomoLoggingEnabled ? "Mihomo Log: On" : "Mihomo Log: Off";

    public int MihomoLogLevelIndex
    {
        get => _mihomoLogLevelIndex;
        set
        {
            if (SetProperty(ref _mihomoLogLevelIndex, value))
                OnLogLevelChanged?.Invoke(LogLevels[value]);
        }
    }

    public List<string> LogLevels { get; } = new() { "debug", "info", "warning", "error", "silent" };

    public ICommand ToggleProxyCommand { get; }
    public ICommand OpenRulesCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public Action<string>? OnLogLevelChanged { get; set; }
    public Action? OnTrafficLoggingChanged { get; set; }

    public HomeViewModel(
        ICommand toggleProxyCommand,
        ICommand openRulesCommand,
        ICommand openSettingsCommand,
        Action<string> onLogLevelChanged,
        Action onTrafficLoggingChanged)
    {
        ToggleProxyCommand = toggleProxyCommand;
        OpenRulesCommand = openRulesCommand;
        OpenSettingsCommand = openSettingsCommand;
        OnLogLevelChanged = onLogLevelChanged;
        OnTrafficLoggingChanged = onTrafficLoggingChanged;
    }
}
