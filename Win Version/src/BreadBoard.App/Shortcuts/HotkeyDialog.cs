using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BreadBoard.Core.Hotkeys;
using BreadBoard.App.Services;
using BreadBoard.App.Theme;
using static BreadBoard.App.Theme.PageKit;

namespace BreadBoard.App.Shortcuts;

/// <summary>Records one shortcut for a sound or a global action, checking conflicts as you type.</summary>
public sealed class HotkeyDialog : DialogWindow
{
    private readonly HotkeyBox _box = new();
    private readonly TextBlock _error = Text("", 13);

    private HotkeyDialog(SoundboardController sb, string ownerKey, string label, string? current) : base("Set shortcut", 420)
    {
        var body = new StackPanel();
        body.Children.Add(Text($"Shortcut for “{label}”", 14, muted: true));
        _box.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(_box);
        var hint = Text("Click the box and press the keys. Backspace clears. Shortcuts work in every app, even in games.", 12, muted: true);
        hint.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(hint);
        _error.Foreground = Res<Brush>("BadBrush");
        _error.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(_error);
        Body = body;

        _box.Combo = current;
        _box.ComboChanged += c => _error.Text = c is null ? "" : HotkeyRules.Validate(sb.Config, c, ownerKey) ?? "";
        AddButton("Cancel");
        var save = AddButton("Save", primary: true, isDefault: true);
        save.Click += (_, _) =>
        {
            if (_box.Combo is { } c && HotkeyRules.Validate(sb.Config, c, ownerKey) is { } why) { _error.Text = why; return; }
            Combo = _box.Combo;
            DialogResult = true;
        };
        Loaded += (_, _) => _box.Focus();
    }

    public string? Combo { get; private set; }

    /// <summary>Shows the dialog with global shortcuts paused; applies the result.</summary>
    public static async Task EditAsync(SoundboardController sb, HotkeyState state)
    {
        var d = new HotkeyDialog(sb, state.OwnerKey, state.Label, state.Combo);
        bool saved;
        using (sb.SuspendHotkeys()) saved = d.ShowDialog() == true;
        if (!saved || d.Combo == state.Combo) return;
        await sb.SetHotkeyAsync(state.OwnerKey, d.Combo);
    }
}
