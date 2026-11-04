using System;
using System.Text;
using UnityEngine;

namespace BetterKeyBindings
{
    // A binding is a main key plus the modifiers that must be held with it. Modifiers do not care about the side:
    // Ctrl is either Ctrl (or Cmd), Shift either Shift, Alt either Alt.
    public static class Combo
    {
        public const int Ctrl = 1, Shift = 2, Alt = 4, All = Ctrl | Shift | Alt;

        // Mouse buttons a binding may use besides the keyboard: middle, 4 and 5. Left and right stay the game's.
        public static readonly KeyCode[] MouseKeys = { KeyCode.Mouse2, KeyCode.Mouse3, KeyCode.Mouse4 };

        // The wheel's two directions as keys. Unity has no key code for them, so they take values above all of its own;
        // the game stores a binding's key as a number, so it keeps them like any other key. Consume answers for them.
        public const KeyCode WheelUp = (KeyCode)1001, WheelDown = (KeyCode)1002;
        public static readonly KeyCode[] WheelKeys = { WheelUp, WheelDown };

        public static bool IsWheel(KeyCode k) { return k == WheelUp || k == WheelDown; }

        // Every input a binding can take: the keyboard but Escape, the mouse buttons above, and the wheel.
        public static bool IsBindable(KeyCode k)
        {
            if (k > KeyCode.None && k < KeyCode.Mouse0) return k != KeyCode.Escape;
            return IsWheel(k) || Array.IndexOf(MouseKeys, k) >= 0;
        }

        // Localizer, set by BetterKeyBindingsMod.OnEnable. Returns the key itself until then, which is also what a
        // test without the game gets.
        public static Func<string, string> L = k => k;

        // The modifier group a key belongs to, or 0 for any other key.
        public static int GroupOf(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.LeftControl: case KeyCode.RightControl:
                case KeyCode.LeftCommand: case KeyCode.RightCommand:
                    return Ctrl;
                case KeyCode.LeftShift: case KeyCode.RightShift:
                    return Shift;
                case KeyCode.LeftAlt: case KeyCode.RightAlt:
                    return Alt;
                default:
                    return 0;
            }
        }

        public static bool IsModifier(KeyCode k) { return GroupOf(k) != 0; }

        // Whether any key of a modifier group is held. It reads Input.GetKey, which Consume filters while a capture
        // waits, so code that needs the real state calls it inside Consume.BeginRaw / EndRaw (Consume.Resolve and the
        // options capture do).
        static bool Down(int group)
        {
            switch (group)
            {
                case Ctrl:
                    return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                        Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
                case Shift: return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                case Alt: return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                default: return false;
            }
        }

        // Modifiers held right now. The main key's own group is left out, so a binding whose main key is
        // Left Shift is not "Shift + Left Shift".
        public static int Held(KeyCode mainKey)
        {
            int held = 0;
            if (Down(Ctrl)) held |= Ctrl;
            if (Down(Shift)) held |= Shift;
            if (Down(Alt)) held |= Alt;
            return held & ~GroupOf(mainKey);
        }

        // Main key held, either side when it is a modifier, as the game's own hold checks accept.
        public static bool KeyHeld(KeyCode k)
        {
            int g = GroupOf(k);
            return g != 0 ? Down(g) : Input.GetKey(k);
        }

        // Names for the inputs the game cannot name; null for any other key, which keeps the game's own name
        // (see Bindings.KeyNamePostfix).
        public static string KeyName(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.Mouse2: return L("key_mouse_middle");
                case KeyCode.Mouse3: return L("key_mouse_4");
                case KeyCode.Mouse4: return L("key_mouse_5");
                case WheelUp: return L("key_wheel_up");
                case WheelDown: return L("key_wheel_down");
            }
            return null;
        }

        public static string ModsPrefix(int mods)
        {
            var sb = new StringBuilder();
            if ((mods & Ctrl) != 0) sb.Append(L("mod_ctrl")).Append(" + ");
            if ((mods & Shift) != 0) sb.Append(L("mod_shift")).Append(" + ");
            if ((mods & Alt) != 0) sb.Append(L("mod_alt")).Append(" + ");
            return sb.ToString();
        }

        public static string Name(KeyCode key, int mods)
        {
            string main = KeyBindHelper.KeyName(key);
            return key == KeyCode.None ? main : ModsPrefix(mods) + main;
        }

        // Key and modifiers in one int for PlayerPrefs (see Rule.Pack). Unpack drops values that are neither a
        // KeyCode nor a wheel key, so a stored value from a changed game version reads as unbound.
        public static int Pack(KeyCode key, int mods) { return Rule.Pack((int)key, mods); }

        public static void Unpack(int packed, out KeyCode key, out int mods)
        {
            int k;
            Rule.Unpack(packed, out k, out mods);
            key = Enum.IsDefined(typeof(KeyCode), k) || IsWheel((KeyCode)k) ? (KeyCode)k : KeyCode.None;
        }
    }
}
