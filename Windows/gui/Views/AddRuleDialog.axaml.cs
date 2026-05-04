using Avalonia.Controls;
using Avalonia.Interactivity;
using JackBridge.GUI.ViewModels;

namespace JackBridge.GUI.Views;

public partial class AddRuleDialog : Window
{
    public ProxyRule? Result { get; private set; }

    public AddRuleDialog()
    {
        InitializeComponent();
        SaveButton.Click += OnSaveClick;
        CancelButton.Click += OnCancelClick;
    }

    public AddRuleDialog(ProxyRule prefill) : this()
    {
        ProcessNameBox.Text = prefill.ProcessName;
        TargetHostsBox.Text = prefill.TargetHosts;
        TargetPortsBox.Text = prefill.TargetPorts;

        for (int i = 0; i < ProtocolCombo.Items.Count; i++)
        {
            if (ProtocolCombo.Items[i] is ComboBoxItem item && (string?)item.Tag == prefill.Protocol)
            {
                ProtocolCombo.SelectedIndex = i;
                break;
            }
        }

        for (int i = 0; i < ActionCombo.Items.Count; i++)
        {
            if (ActionCombo.Items[i] is ComboBoxItem item && (string?)item.Tag == prefill.Action)
            {
                ActionCombo.SelectedIndex = i;
                break;
            }
        }

        for (int i = 0; i < SectionCombo.Items.Count; i++)
        {
            var tag = prefill.IsStatic ? "Static" : "Active";
            if (SectionCombo.Items[i] is ComboBoxItem item && (string?)item.Tag == tag)
            {
                SectionCombo.SelectedIndex = i;
                break;
            }
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var protocol = ((ComboBoxItem)ProtocolCombo.SelectedItem!).Tag as string ?? "TCP";
        var action = ((ComboBoxItem)ActionCombo.SelectedItem!).Tag as string ?? "PROXY";
        var isStatic = ((ComboBoxItem)SectionCombo.SelectedItem!).Tag as string == "Static";

        Result = new ProxyRule
        {
            ProcessName = string.IsNullOrWhiteSpace(ProcessNameBox.Text) ? "*" : ProcessNameBox.Text.Trim(),
            TargetHosts = string.IsNullOrWhiteSpace(TargetHostsBox.Text) ? "*" : TargetHostsBox.Text.Trim(),
            TargetPorts = string.IsNullOrWhiteSpace(TargetPortsBox.Text) ? "*" : TargetPortsBox.Text.Trim(),
            Protocol = protocol,
            Action = action,
            IsEnabled = true,
            IsStatic = isStatic
        };

        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close(false);
    }
}
