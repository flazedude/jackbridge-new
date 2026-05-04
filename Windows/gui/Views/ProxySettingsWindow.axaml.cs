using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using JackBridge.GUI.ViewModels;
using System.Linq;

namespace JackBridge.GUI.Views;

public partial class ProxySettingsWindow : UserControl
{
    public ProxySettingsWindow()
    {
        InitializeComponent();

        var dropZone = this.FindControl<Border>("YamlDropZone");
        if (dropZone != null)
        {
            DragDrop.SetAllowDrop(dropZone, true);
            dropZone.AddHandler(DragDrop.DragOverEvent, YamlDropZone_DragOver);
            dropZone.AddHandler(DragDrop.DropEvent, YamlDropZone_Drop);
        }

        this.DataContextChanged += (s, e) =>
        {
            if (DataContext is ProxySettingsViewModel vm)
            {
                var comboBox = this.FindControl<ComboBox>("ProxyTypeComboBox");
                if (comboBox != null)
                {
                    foreach (var obj in comboBox.Items)
                    {
                        if (obj is ComboBoxItem item && item.Tag is string tag && tag == vm.ProxyType)
                        {
                            comboBox.SelectedItem = item;
                            break;
                        }
                    }

                    comboBox.SelectionChanged += (sender, args) =>
                    {
                        if (DataContext is ProxySettingsViewModel vm2)
                        {
                            if (comboBox.SelectedItem is ComboBoxItem sel && sel.Tag is string selTag)
                            {
                                vm2.ProxyType = selTag;
                            }
                        }
                    };
                }
            }
        };
    }

    private async void BrowseYaml_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProxySettingsViewModel vm)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select mihomo YAML profile",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("YAML profiles")
                {
                    Patterns = ["*.yaml", "*.yml"],
                    MimeTypes = ["application/x-yaml", "text/yaml", "text/plain"]
                }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await vm.ImportYamlProfileAsync(path);
    }

    private void YamlDropZone_DragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
#pragma warning restore CS0618
        e.Handled = true;
    }

    private async void YamlDropZone_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ProxySettingsViewModel vm)
            return;

#pragma warning disable CS0618
        var file = e.Data.GetFiles()?.FirstOrDefault();
#pragma warning restore CS0618
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await vm.ImportYamlProfileAsync(path);

        e.Handled = true;
    }
}
