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

        public static string KeyName(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.Mouse2: return L("key_mouse_middle");
                case KeyCode.Mouse3: return L("key_mouse_4");
                case KeyCode.Mouse4: return L("key_mouse_5");
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

        public static int Pack(KeyCode key, int mods) { return Rule.Pack((int)key, mods); }

        public static void Unpack(int packed, out KeyCode key, out int mods)
        {
            int k;
            Rule.Unpack(packed, out k, out mods);
            key = Enum.IsDefined(typeof(KeyCode), k) ? (KeyCode)k : KeyCode.None;
        }
    }
}
