using System.Collections.ObjectModel;
using System.Windows.Input;
using JackBridge.GUI.Common;

namespace JackBridge.GUI.ViewModels;

public class ConnectionsViewModel : ViewModelBase
{
    private string _searchText = "";
    private string _filteredLog = "";
    private bool _hasObservedProcesses;
    private bool _isTrafficLoggingEnabled = true;

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string FilteredLog
    {
        get => _filteredLog;
        set => SetProperty(ref _filteredLog, value);
    }

    public bool HasObservedProcesses
    {
        get => _hasObservedProcesses;
        set => SetProperty(ref _hasObservedProcesses, value);
    }

    public bool IsTrafficLoggingEnabled
    {
        get => _isTrafficLoggingEnabled;
        set
        {
            if (SetProperty(ref _isTrafficLoggingEnabled, value))
                OnPropertyChanged(nameof(TrafficLoggingLabel));
        }
    }

    public string TrafficLoggingLabel => IsTrafficLoggingEnabled ? "Traffic: On" : "Traffic: Off";

    public ObservableCollection<ObservedProcessRuleCandidate> ObservedProcesses { get; }

    public ICommand SearchCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand AddObservedProcessRuleCommand { get; }
    public ICommand ToggleTrafficLoggingCommand { get; }

    public ConnectionsViewModel(
        ObservableCollection<ObservedProcessRuleCandidate> observedProcesses,
        ICommand searchCommand,
        ICommand clearCommand,
        ICommand addObservedProcessRuleCommand,
        ICommand toggleTrafficLoggingCommand,
        bool isTrafficLoggingEnabled)
    {
        ObservedProcesses = observedProcesses;
        SearchCommand = searchCommand;
        ClearCommand = clearCommand;
        AddObservedProcessRuleCommand = addObservedProcessRuleCommand;
        ToggleTrafficLoggingCommand = toggleTrafficLoggingCommand;
        _isTrafficLoggingEnabled = isTrafficLoggingEnabled;
    }
}
