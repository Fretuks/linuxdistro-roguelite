using UnityEngine;

namespace KernelPanic.UI
{
    /// <summary>
    /// Maps number-row/keypad digits to menu slot indices. Shared by the command menu and the
    /// starter-selection modal so both keep the same 1-5 shortcut behavior.
    /// </summary>
    internal static class CommandKeyBindings
    {
        public static int GetDigitIndex(KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.Alpha1 or KeyCode.Keypad1 => 0,
                KeyCode.Alpha2 or KeyCode.Keypad2 => 1,
                KeyCode.Alpha3 or KeyCode.Keypad3 => 2,
                KeyCode.Alpha4 or KeyCode.Keypad4 => 3,
                KeyCode.Alpha5 or KeyCode.Keypad5 => 4,
                _ => -1
            };
        }
    }
}
