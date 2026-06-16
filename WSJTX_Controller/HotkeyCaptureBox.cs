using System;
using System.Windows.Forms;

namespace WSJTX_Controller
{
    // TextBox subclass that captures a key combination on KeyDown instead of typing.
    // Tab (with or without Shift) is passed through for normal focus navigation.
    // Pure modifier keypresses (Ctrl, Alt, Shift alone) are ignored.
    // Every other key fires the KeyCaptured event with the full key+modifier combo.
    public class HotkeyCaptureBox : TextBox
    {
        private Keys _committedValue = Keys.None;

        public Keys CapturedKeys => _committedValue;

        public event EventHandler<KeyCapturedEventArgs> KeyCaptured;

        public void SetValue(Keys keys)
        {
            _committedValue = keys;
            Text = HotkeyConfig.FormatKeys(keys);
        }

        protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e)
        {
            // Let Tab pass through so the dialog can move focus normally.
            if (e.KeyCode == Keys.Tab)
            {
                base.OnPreviewKeyDown(e);
                return;
            }
            // Tell WinForms to generate a KeyDown for all other keys (including
            // arrows, Escape, Enter, etc. which WinForms skips by default).
            e.IsInputKey = true;
            base.OnPreviewKeyDown(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Let Tab/Shift+Tab through for navigation.
            if (e.KeyCode == Keys.Tab)
            {
                base.OnKeyDown(e);
                return;
            }

            // Ignore modifier-only keypresses (Ctrl, Alt, Shift held alone).
            if (IsModifierOnly(e.KeyCode))
            {
                e.Handled = true;
                return;
            }

            // Suppress the key so no character is inserted in the TextBox.
            e.SuppressKeyPress = true;
            e.Handled = true;

            KeyCaptured?.Invoke(this, new KeyCapturedEventArgs(e.KeyData));
        }

        // Prevent the base class from processing characters — all input is handled in OnKeyDown.
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            e.Handled = true;
        }

        private static bool IsModifierOnly(Keys keyCode)
        {
            switch (keyCode)
            {
                case Keys.ControlKey:
                case Keys.LControlKey:
                case Keys.RControlKey:
                case Keys.ShiftKey:
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                case Keys.Menu:
                case Keys.LMenu:
                case Keys.RMenu:
                    return true;
                default:
                    return false;
            }
        }
    }

    public class KeyCapturedEventArgs : EventArgs
    {
        public Keys Keys { get; }
        public KeyCapturedEventArgs(Keys keys) { Keys = keys; }
    }
}
