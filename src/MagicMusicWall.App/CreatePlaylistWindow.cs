using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace MagicMusicWall.App;

internal enum PlaylistSeed { Selected, Queue, Empty }

internal sealed record PlaylistDraft(string Name, PlaylistSeed Source);

internal sealed class CreatePlaylistWindow : Window
{
    private sealed record SeedOption(string Label, PlaylistSeed Source);

    private readonly TextBox _name = new() { MaxLength = 100, MinHeight = 36 };
    private readonly ComboBox _source = new() { DisplayMemberPath = nameof(SeedOption.Label), MinHeight = 34 };
    private PlaylistDraft? _result;

    private CreatePlaylistWindow(int selectedCount, int queueCount)
    {
        Ui.Theme(this);
        Title = "Create playlist";
        Width = 440; Height = 310;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var choices = new List<SeedOption>();
        if (selectedCount > 0) choices.Add(new($"Selected songs ({selectedCount:N0})", PlaylistSeed.Selected));
        if (queueCount > 0) choices.Add(new($"Current queue ({queueCount:N0})", PlaylistSeed.Queue));
        choices.Add(new("Empty playlist", PlaylistSeed.Empty));
        _source.ItemsSource = choices;
        _source.SelectedIndex = 0;
        AutomationProperties.SetName(_name, "Playlist name");
        AutomationProperties.SetName(_source, "Start playlist with");

        var panel = new StackPanel { Margin = new Thickness(20) };
        var nameLabel = Ui.Text("Playlist name", 12, true);
        nameLabel.Margin = new Thickness(0, 0, 0, 6);
        panel.Children.Add(nameLabel);
        panel.Children.Add(_name);
        var sourceLabel = Ui.Text("Start with", 12, true);
        sourceLabel.Margin = new Thickness(0, 12, 0, 6);
        panel.Children.Add(sourceLabel);
        panel.Children.Add(_source);
        var help = Ui.Text("Songs stay in your library. You can add or remove songs from this playlist later.", 12, true);
        help.Margin = new Thickness(0, 12, 0, 10);
        panel.Children.Add(help);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Ui.Button("Cancel", (_, _) => Close());
        cancel.IsCancel = true;
        var create = Ui.Button("Create", (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text) || _source.SelectedItem is not SeedOption choice) return;
            _result = new(_name.Text.Trim(), choice.Source);
            DialogResult = true;
        });
        create.IsDefault = true;
        create.IsEnabled = false;
        create.SetResourceReference(StyleProperty, "AccentButton");
        _name.TextChanged += (_, _) => create.IsEnabled = !string.IsNullOrWhiteSpace(_name.Text);
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => _name.Focus();
    }

    public static PlaylistDraft? Show(Window owner, int selectedCount, int queueCount)
    {
        var dialog = new CreatePlaylistWindow(selectedCount, queueCount) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}
