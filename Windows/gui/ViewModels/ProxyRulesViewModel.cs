using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows.Input;
using Avalonia.Controls;
using JackBridge.GUI.Services;
using JackBridge.GUI.Common;

namespace JackBridge.GUI.ViewModels;

public class ProxyRulesViewModel : ViewModelBase
{
    private readonly Loc _loc = Loc.Instance;
    public Loc Loc => _loc;

    private bool _isAddRuleViewOpen;
    private bool _isEditMode;
    private uint _currentEditingRuleId;
    private string _newProcessName = "*";
    private string _newTargetHosts = "*";
    private string _newTargetPorts = "*";
    private string _newProtocol = "TCP"; // TCP, UDP, or BOTH
    private string _newProxyAction = "PROXY";
    private string _newRuleSection = "Active";
    private string _processNameError = "";
    private ProcessChoice? _selectedProcessChoice;
    private Action<ProxyRule>? _onAddRule;
    private Action? _onClose;
    private Action? _onConfigChanged;
    private JackBridgeService? _proxyService;
    private Window? _window;

    public ObservableCollection<ProxyRule> ProxyRules { get; }
    public ObservableCollection<ProcessChoice> RunningProcesses { get; } = new();
    public System.Collections.Generic.IEnumerable<ProxyRule> ActiveRules => ProxyRules.Where(rule => !rule.IsStatic);
    public System.Collections.Generic.IEnumerable<ProxyRule> StaticRules => ProxyRules.Where(rule => rule.IsStatic);
    public bool HasActiveRules => ActiveRules.Any();
    public bool HasStaticRules => StaticRules.Any();

    public bool IsAddRuleViewOpen
    {
        get => _isAddRuleViewOpen;
        set => SetProperty(ref _isAddRuleViewOpen, value);
    }

    public string NewProcessName
    {
        get => _newProcessName;
        set
        {
            SetProperty(ref _newProcessName, value);
            ProcessNameError = "";
        }
    }

    public string NewTargetHosts
    {
        get => _newTargetHosts;
        set => SetProperty(ref _newTargetHosts, value);
    }

    public string NewTargetPorts
    {
        get => _newTargetPorts;
        set => SetProperty(ref _newTargetPorts, value);
    }

    public string NewProtocol
    {
        get => _newProtocol;
        set => SetProperty(ref _newProtocol, value);
    }

    public string NewProxyAction
    {
        get => _newProxyAction;
        set => SetProperty(ref _newProxyAction, value);
    }

    public string NewRuleSection
    {
        get => _newRuleSection;
        set => SetProperty(ref _newRuleSection, value);
    }

    public int NewRuleSectionIndex
    {
        get => NewRuleSection.Equals("Static", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        set
        {
            NewRuleSection = value == 1 ? "Static" : "Active";
            OnPropertyChanged();
        }
    }

    public string ProcessNameError
    {
        get => _processNameError;
        set => SetProperty(ref _processNameError, value);
    }

    public ProcessChoice? SelectedProcessChoice
    {
        get => _selectedProcessChoice;
        set
        {
            if (SetProperty(ref _selectedProcessChoice, value) && value != null)
            {
                AppendProcessName(value.ExecutableName);
            }
        }
    }

    public ICommand AddRuleCommand { get; }
    public ICommand SaveNewRuleCommand { get; }
    public ICommand CancelAddRuleCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand BrowseProcessCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand EditRuleCommand { get; }
    public ICommand ToggleSelectAllCommand { get; }
    public ICommand ExportRulesCommand { get; }
    public ICommand ImportRulesCommand { get; }
    public ICommand DeleteSelectedRulesCommand { get; }
    public ICommand MoveRuleUpCommand { get; }
    public ICommand MoveRuleDownCommand { get; }

    public bool HasSelectedRules => ProxyRules.Any(r => r.IsSelected);
    public bool AllRulesSelected => ProxyRules.Any() && ProxyRules.All(r => r.IsSelected);

    public void SetWindow(Window window)
    {
        _window = window;
    }

    public bool MoveRuleToPosition(uint ruleId, uint newPosition)
    {
        var rule = ProxyRules.FirstOrDefault(r => r.RuleId == ruleId);
        if (rule == null || newPosition == 0)
            return false;

        return MoveRuleToIndex(rule, (int)newPosition - 1);
    }

    public bool MoveRuleToIndex(ProxyRule rule, int targetIndex)
    {
        if (_proxyService == null || rule == null)
            return false;

        int currentIndex = ProxyRules.IndexOf(rule);
        if (currentIndex < 0 || ProxyRules.Count == 0)
            return false;

        var sectionRules = ProxyRules
            .Where(existingRule => existingRule.IsStatic == rule.IsStatic)
            .ToList();
        int sectionIndex = sectionRules.IndexOf(rule);
        if (sectionIndex < 0)
            return false;

        int sectionTargetIndex = Math.Clamp(targetIndex, 0, sectionRules.Count - 1);
        if (sectionIndex == sectionTargetIndex)
            return false;

        var reorderedSection = sectionRules.ToList();
        reorderedSection.RemoveAt(sectionIndex);
        reorderedSection.Insert(sectionTargetIndex, rule);

        var reorderedRules = ProxyRules
            .Where(existingRule => existingRule.IsStatic != rule.IsStatic)
            .ToList();

        if (rule.IsStatic)
        {
            reorderedRules.AddRange(reorderedSection);
        }
        else
        {
            reorderedRules.InsertRange(0, reorderedSection);
        }

        targetIndex = reorderedRules.IndexOf(rule);
        if (currentIndex == targetIndex)
            return false;

        uint newPosition = (uint)(targetIndex + 1);
        if (!_proxyService.MoveRuleToPosition(rule.RuleId, newPosition))
            return false;

        ProxyRules.Clear();
        foreach (var reorderedRule in reorderedRules)
        {
            ProxyRules.Add(reorderedRule);
        }

        RefreshRulePriorities();
        NotifyRuleSectionsChanged();
        _onConfigChanged?.Invoke();
        return true;
    }

    private void RefreshRulePriorities()
    {
        int activeIndex = 1;
        int staticIndex = 1;

        for (int i = 0; i < ProxyRules.Count; i++)
        {
            ProxyRules[i].Index = i + 1;
            ProxyRules[i].SectionIndex = ProxyRules[i].IsStatic ? staticIndex++ : activeIndex++;
        }
    }

    private void ResetRuleForm()
    {
        NewProcessName = "*";
        NewTargetHosts = "*";
        NewTargetPorts = "*";
        NewProtocol = "TCP";
        NewProxyAction = "PROXY";
        NewRuleSection = "Active";
        ProcessNameError = "";
    }

    private void NotifyRuleSectionsChanged()
    {
        OnPropertyChanged(nameof(ActiveRules));
        OnPropertyChanged(nameof(StaticRules));
        OnPropertyChanged(nameof(HasActiveRules));
        OnPropertyChanged(nameof(HasStaticRules));
        OnPropertyChanged(nameof(HasSelectedRules));
        OnPropertyChanged(nameof(AllRulesSelected));
    }

    public ProxyRulesViewModel(ObservableCollection<ProxyRule> proxyRules, Action<ProxyRule> onAddRule, Action onClose, JackBridgeService? proxyService = null, Action? onConfigChanged = null)
    {
        ProxyRules = proxyRules;
        _onAddRule = onAddRule;
        _onClose = onClose;
        _proxyService = proxyService;
        _onConfigChanged = onConfigChanged;
        ProxyRules.CollectionChanged += ProxyRules_CollectionChanged;

        foreach (var rule in ProxyRules)
        {
            rule.PropertyChanged += Rule_PropertyChanged;
        }

        AddRuleCommand = new RelayCommand(() =>
        {
            ResetRuleForm();
            RefreshRunningProcesses();
            IsAddRuleViewOpen = true;
        });

        SaveNewRuleCommand = new RelayCommand(() =>
        {
            NewProcessName = ValidationHelper.DefaultIfEmpty(NewProcessName);
            NewTargetHosts = ValidationHelper.DefaultIfEmpty(NewTargetHosts);
            NewTargetPorts = ValidationHelper.DefaultIfEmpty(NewTargetPorts);

            if (!System.Text.RegularExpressions.Regex.IsMatch(NewProcessName, @"^[a-zA-Z0-9\s._\-*;""\\:()]+$"))
            {
                ProcessNameError = "Invalid characters in process name. Only letters, numbers, spaces, dots, dashes, underscores, semicolons, quotes, parentheses, and * are allowed";
                return;
            }

            if (NewProcessName != "*" && !NewProcessName.Equals("*", StringComparison.OrdinalIgnoreCase))
            {
                if (!NewProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !NewProcessName.Contains(".exe ", StringComparison.OrdinalIgnoreCase) &&
                    !NewProcessName.Contains(";", StringComparison.OrdinalIgnoreCase))
                {
                    NewProcessName += ".exe";
                }
            }

            if (_isEditMode && _proxyService != null)
            {
                if (_proxyService.EditRule(_currentEditingRuleId, NewProcessName, NewTargetHosts, NewTargetPorts, NewProtocol, NewProxyAction))
                {
                    var existingRule = ProxyRules.FirstOrDefault(r => r.RuleId == _currentEditingRuleId);
                    if (existingRule != null)
                    {
                        existingRule.ProcessName = NewProcessName;
                        existingRule.TargetHosts = NewTargetHosts;
                        existingRule.TargetPorts = NewTargetPorts;
                        existingRule.Protocol = NewProtocol;
                        existingRule.Action = NewProxyAction;
                        existingRule.IsStatic = NewRuleSection.Equals("Static", StringComparison.OrdinalIgnoreCase);
                        NormalizeRuleOrder();
                    }
                    _onConfigChanged?.Invoke();
                }

                _isEditMode = false;
                _currentEditingRuleId = 0;
            }
            else
            {
                var newRule = new ProxyRule
                {
                    ProcessName = NewProcessName,
                    TargetHosts = NewTargetHosts,
                    TargetPorts = NewTargetPorts,
                    Protocol = NewProtocol,
                    Action = NewProxyAction,
                    IsEnabled = true,
                    IsStatic = NewRuleSection.Equals("Static", StringComparison.OrdinalIgnoreCase)
                };

                newRule.PropertyChanged += Rule_PropertyChanged;
                _onAddRule?.Invoke(newRule);
            }

            IsAddRuleViewOpen = false;
            ResetRuleForm();
        });        CancelAddRuleCommand = new RelayCommand(() =>
        {
            ResetRuleForm();
            IsAddRuleViewOpen = false;
        });

        CloseCommand = new RelayCommand(() =>
        {
            _onClose?.Invoke();
        });

        BrowseProcessCommand = new RelayCommand(() =>
        {
            RefreshRunningProcesses();
        });

        DeleteRuleCommand = new RelayCommandWithParameter<ProxyRule>(async (rule) =>
        {
            if (rule == null || _window == null)
                return;

            var result = await ShowConfirmDialogAsync("Delete Rule",
                $"Are you sure you want to delete the rule for process '{rule.ProcessName}'?");

            if (result)
            {
                if (rule.RuleId > 0)
                    _proxyService?.DeleteRule(rule.RuleId);

                ProxyRules.Remove(rule);
                RefreshRulePriorities();
                NotifyRuleSectionsChanged();
                _onConfigChanged?.Invoke();
            }
        });

        EditRuleCommand = new RelayCommandWithParameter<ProxyRule>((rule) =>
        {
            if (rule == null)
                return;

            _isEditMode = true;
            _currentEditingRuleId = rule.RuleId;
            NewProcessName = rule.ProcessName;
            NewTargetHosts = rule.TargetHosts;
            NewTargetPorts = rule.TargetPorts;
            NewProtocol = rule.Protocol;
            NewProxyAction = rule.Action;
            NewRuleSection = rule.IsStatic ? "Static" : "Active";
            ProcessNameError = "";
            RefreshRunningProcesses();
            IsAddRuleViewOpen = true;
        });

        ToggleSelectAllCommand = new RelayCommand(() =>
        {
            bool selectAll = !AllRulesSelected;
            foreach (var rule in ProxyRules)
            {
                rule.IsSelected = selectAll;
            }
            OnPropertyChanged(nameof(HasSelectedRules));
            OnPropertyChanged(nameof(AllRulesSelected));
        });

        ExportRulesCommand = new RelayCommand(async () =>
        {
            try
            {
                await ExportSelectedRulesAsync();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Export Failed", $"Failed to export rules: {ex.Message}");
            }
        });

        ImportRulesCommand = new RelayCommand(async () =>
        {
            try
            {
                await ImportRulesAsync();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Import Failed", $"Failed to import rules: {ex.Message}");
            }
        });

        DeleteSelectedRulesCommand = new RelayCommand(async () =>
        {
            var selectedRules = ProxyRules.Where(r => r.IsSelected).ToList();
            if (selectedRules.Count == 0)
                return;

            var confirmMsg = selectedRules.Count == 1
                ? $"Delete 1 selected rule?"
                : $"Delete {selectedRules.Count} selected rules?";

            var confirmed = await ShowConfirmDialogAsync("Delete Selected Rules", confirmMsg);
            if (!confirmed)
                return;

            foreach (var rule in selectedRules)
            {
                if (rule.RuleId > 0)
                    _proxyService?.DeleteRule(rule.RuleId);

                ProxyRules.Remove(rule);
            }

            RefreshRulePriorities();
            NotifyRuleSectionsChanged();
            _onConfigChanged?.Invoke();
            OnPropertyChanged(nameof(HasSelectedRules));
            OnPropertyChanged(nameof(AllRulesSelected));
        });

        MoveRuleUpCommand = new RelayCommandWithParameter<ProxyRule>((rule) =>
        {
            MoveRuleToIndex(rule, GetSectionIndex(rule) - 1);
        });

        MoveRuleDownCommand = new RelayCommandWithParameter<ProxyRule>((rule) =>
        {
            MoveRuleToIndex(rule, GetSectionIndex(rule) + 1);
        });
    }

    private void ProxyRules_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotifyRuleSectionsChanged();
    }

    private int GetSectionIndex(ProxyRule rule)
    {
        return ProxyRules
            .Where(existingRule => existingRule.IsStatic == rule.IsStatic)
            .ToList()
            .IndexOf(rule);
    }

    private async System.Threading.Tasks.Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        if (_window == null)
            return false;

        var messageBox = new Window
        {
            Title = title,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        bool result = false;

        var stackPanel = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 10
        };

        stackPanel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 10
        };

        var yesButton = new Button
        {
            Content = "Yes",
            Width = 80
        };
        yesButton.Click += (s, e) =>
        {
            result = true;
            messageBox.Close();
        };

        var noButton = new Button
        {
            Content = "No",
            Width = 80
        };
        noButton.Click += (s, e) =>
        {
            result = false;
            messageBox.Close();
        };

        buttonPanel.Children.Add(yesButton);
        buttonPanel.Children.Add(noButton);
        stackPanel.Children.Add(buttonPanel);

        messageBox.Content = stackPanel;

        await messageBox.ShowDialog(_window);
        return result;
    }

    private void Rule_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProxyRule.IsEnabled) && sender is ProxyRule rule && _proxyService != null)
        {
            if (rule.IsEnabled)
            {
                _proxyService.EnableRule(rule.RuleId);
            }
            else
            {
                _proxyService.DisableRule(rule.RuleId);
            }
            _onConfigChanged?.Invoke();
        }
        else if (e.PropertyName == nameof(ProxyRule.IsSelected))
        {
            OnPropertyChanged(nameof(HasSelectedRules));
            OnPropertyChanged(nameof(AllRulesSelected));
        }
    }

    private async System.Threading.Tasks.Task ExportSelectedRulesAsync()
    {
        if (_window == null)
            return;

        var selectedRules = ProxyRules.Where(r => r.IsSelected).ToList();

        if (!selectedRules.Any())
        {
            await ShowMessageAsync("No Rules Selected", "Please select at least one rule to export.");
            return;
        }

        var saveDialog = new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export Proxy Rules",
            SuggestedFileName = "JackBridge-Rules.json",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("JSON Files")
                {
                    Patterns = new[] { "*.json" }
                }
            }
        };

        var result = await _window.StorageProvider.SaveFilePickerAsync(saveDialog);

        if (result != null)
        {
            var exportData = selectedRules.Select(r => new ProxyRuleExport
            {
                ProcessNames = r.ProcessName,
                TargetHosts = r.TargetHosts,
                TargetPorts = r.TargetPorts,
                Protocol = r.Protocol,
                Action = r.Action,
                Enabled = r.IsEnabled,
                Static = r.IsStatic
            }).ToList();

            var json = System.Text.Json.JsonSerializer.Serialize(exportData, ProxyRuleJsonContext.Default.ListProxyRuleExport);

            await System.IO.File.WriteAllTextAsync(result.Path.LocalPath, json);

            await ShowMessageAsync("Export Successful", $"Exported {selectedRules.Count} rule(s) to:\n{result.Path.LocalPath}");
        }
    }

    private async System.Threading.Tasks.Task ImportRulesAsync()
    {
        if (_window == null)
            return;

        if (_proxyService == null)
        {
            await ShowMessageAsync("Import Failed", "Proxy service is not available.");
            return;
        }

        var openDialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Import Proxy Rules",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("JSON Files")
                {
                    Patterns = new[] { "*.json" }
                }
            }
        };

        var result = await _window.StorageProvider.OpenFilePickerAsync(openDialog);

        if (result != null && result.Count > 0)
        {
            var filePath = result[0].Path.LocalPath;

            var json = await System.IO.File.ReadAllTextAsync(filePath);

            var importedRules = System.Text.Json.JsonSerializer.Deserialize(json, ProxyRuleJsonContext.Default.ListProxyRuleExport);

            if (importedRules != null && importedRules.Count > 0)
            {
                int successCount = 0;
                foreach (var ruleData in importedRules)
                {
                    var ruleId = _proxyService.AddRule(
                        ruleData.ProcessNames,
                        ruleData.TargetHosts,
                        ruleData.TargetPorts,
                        ruleData.Protocol,
                        ruleData.Action
                    );

                    if (ruleId > 0)
                    {
                        var newRule = new ProxyRule
                        {
                            RuleId = ruleId,
                            ProcessName = ruleData.ProcessNames,
                            TargetHosts = ruleData.TargetHosts,
                            TargetPorts = ruleData.TargetPorts,
                            Protocol = ruleData.Protocol,
                            Action = ruleData.Action,
                            IsEnabled = ruleData.Enabled,
                            IsStatic = ruleData.Static || ruleData.ProcessNames.Trim() == "*",
                            Index = ProxyRules.Count + 1
                        };

                        newRule.PropertyChanged += Rule_PropertyChanged;
                        InsertRuleInPriorityOrder(newRule);
                        RefreshRulePriorities();
                        _proxyService.MoveRuleToPosition(newRule.RuleId, (uint)newRule.Index);

                        if (!ruleData.Enabled)
                        {
                            _proxyService.DisableRule(ruleId);
                        }

                        successCount++;
                    }
                }

                await ShowMessageAsync("Import Successful", $"Imported {successCount} rule(s) from:\n{filePath}");
                _onConfigChanged?.Invoke();
            }
            else
            {
                await ShowMessageAsync("Import Failed", "No valid rules found in the selected file.");
            }
        }
    }

    private async System.Threading.Tasks.Task ShowMessageAsync(string title, string message)
    {
        if (_window == null)
            return;

        var messageBox = new Window
        {
            Title = title,
            Width = 450,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF2D2D30"))
        };

        var stackPanel = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 15
        };

        stackPanel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White),
            FontSize = 13
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 10
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF0E639C")),
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)
        };
        okButton.Click += (s, e) => messageBox.Close();

        buttonPanel.Children.Add(okButton);
        stackPanel.Children.Add(buttonPanel);

        messageBox.Content = stackPanel;

        await messageBox.ShowDialog(_window);
    }

    private void RefreshRunningProcesses()
    {
        var selectedName = SelectedProcessChoice?.ExecutableName;
        RunningProcesses.Clear();

        var processes = Process.GetProcesses()
            .Select(process =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(process.ProcessName))
                        return null;

                    var executableName = process.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? process.ProcessName
                        : $"{process.ProcessName}.exe";

                    return new ProcessChoice(executableName, process.Id);
                }
                catch
                {
                    return null;
                }
            })
            .Where(process => process != null)
            .Cast<ProcessChoice>()
            .GroupBy(process => process.ExecutableName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(process => process.ProcessId).First())
            .OrderBy(process => process.ExecutableName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var process in processes)
        {
            RunningProcesses.Add(process);
        }

        SelectedProcessChoice = RunningProcesses.FirstOrDefault(process =>
            process.ExecutableName.Equals(selectedName ?? "", StringComparison.OrdinalIgnoreCase));
    }

    private void AppendProcessName(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
            return;

        if (string.IsNullOrWhiteSpace(NewProcessName) || NewProcessName == "*")
        {
            NewProcessName = executableName;
            return;
        }

        var existingNames = NewProcessName
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (existingNames.Any(name => name.Equals(executableName, StringComparison.OrdinalIgnoreCase)))
            return;

        NewProcessName = $"{NewProcessName.TrimEnd(';', ' ')}; {executableName}";
    }

    private void InsertRuleInPriorityOrder(ProxyRule rule)
    {
        int insertIndex = rule.IsStatic
            ? ProxyRules.Count
            : ProxyRules.TakeWhile(existingRule => !existingRule.IsStatic).Count();

        ProxyRules.Insert(insertIndex, rule);
        RefreshRulePriorities();
        NotifyRuleSectionsChanged();
    }

    private void NormalizeRuleOrder()
    {
        var orderedRules = ProxyRules
            .Where(rule => !rule.IsStatic)
            .Concat(ProxyRules.Where(rule => rule.IsStatic))
            .ToList();

        ProxyRules.Clear();
        foreach (var rule in orderedRules)
        {
            ProxyRules.Add(rule);
        }

        RefreshRulePriorities();

        if (_proxyService != null)
        {
            foreach (var rule in ProxyRules.Where(rule => rule.RuleId > 0))
            {
                _proxyService.MoveRuleToPosition(rule.RuleId, (uint)rule.Index);
            }
        }

        NotifyRuleSectionsChanged();
    }
}

public class ProcessChoice
{
    public ProcessChoice(string executableName, int processId)
    {
        ExecutableName = executableName;
        ProcessId = processId;
    }

    public string ExecutableName { get; }
    public int ProcessId { get; }
    public string DisplayName => $"{ExecutableName} (PID {ProcessId})";
}

// JSON export/import model matching macOS format
public class ProxyRuleExport
{
    public string ProcessNames { get; set; } = "*";
    public string TargetHosts { get; set; } = "*";
    public string TargetPorts { get; set; } = "*";
    public string Protocol { get; set; } = "BOTH";
    public string Action { get; set; } = "DIRECT";
    public bool Enabled { get; set; } = true;
    public bool Static { get; set; } = false;
}

// JSON serialization context for NativeAOT compatibility
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(System.Collections.Generic.List<ProxyRuleExport>))]
[JsonSerializable(typeof(ProxyRuleExport))]
internal partial class ProxyRuleJsonContext : JsonSerializerContext
{
}
