using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Platform.Hotkeys;

namespace BreadBoard.App.Shortcuts;

/// <summary>
/// Records a shortcut: focus it and press the keys. Backspace/Delete clears it; Tab and Esc keep
/// their usual meaning (move focus / close the dialog). Global shortcuts should be suspended
/// while it has focus so the keys reach it.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    private string? _combo;

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        Style = (Style)Application.Current.FindResource("Field");
        InputMethod.SetIsInputMethodEnabled(this, false);
        GotKeyboardFocus += (_, _) => Show();
        LostKeyboardFocus += (_, _) => Show();
        Show();
    }

    /// <summary>Normalised shortcut text ("Ctrl+Shift+S", "Num1") or null for none.</summary>
    public string? Combo
    {
        get => _combo;
        set { _combo = value; Show(); ComboChanged?.Invoke(value); }
    }

    public event Action<string?>? ComboChanged;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true; // wait for the main key
            return;
        }
        if (mods == ModifierKeys.None && key is Key.Tab or Key.Escape) return;
        e.Handled = true;
        if (mods == ModifierKeys.None && key is Key.Back or Key.Delete) { Combo = null; return; }

        var name = GlobalHotkeyService.KeyName((uint)KeyInterop.VirtualKeyFromKey(key));
        if (name is null) return; // unsupported key: ignore
        var m = HotkeyModifiers.None;
        if (mods.HasFlag(ModifierKeys.Control)) m |= HotkeyModifiers.Ctrl;
        if (mods.HasFlag(ModifierKeys.Alt)) m |= HotkeyModifiers.Alt;
        if (mods.HasFlag(ModifierKeys.Shift)) m |= HotkeyModifiers.Shift;
        if (mods.HasFlag(ModifierKeys.Windows)) m |= HotkeyModifiers.Win;
        Combo = new Hotkey(m, name).ToString();
    }

    private void Show() =>
        Text = _combo ?? (IsKeyboardFocused ? "Press a key or combination…" : "None — click to set");
}
