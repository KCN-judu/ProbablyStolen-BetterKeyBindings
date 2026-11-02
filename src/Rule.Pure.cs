namespace BetterKeyBindings
{
    // The mod's decisions, kept apart from the game and from Unity so tests/PureTest.cs can run them headless.
    // Keys are KeyCode values as ints, modifiers are Combo masks (Ctrl 1, Shift 2, Alt 4).
    public static class Rule
    {
        public const int AllMods = 7;

        // A binding asking for modifiers `want` may fire while `held` are down, unless another binding on the same
        // key asks for more modifiers and all of those are down too: the most specific binding wins.
        public static bool MayFire(int want, int held, int[] othersOnSameKey)
        {
            if ((want & ~held) != 0) return false;
            foreach (int other in othersOnSameKey)
                if (other != want && (other & want) == want && (other & ~held) == 0) return false;
            return true;
        }

        // Whether two options would collide if both kept their choice. Only options in the same group can collide,
        // and an unbound key (0) never does. Options that keep modifiers collide on the same combination; an option
        // that reads only its key collides with anything on that key; two options of the same other mod collide on
        // the same key, since that mod reads only the key and could not tell them apart.
        public static bool Collides(bool sameGroup, int keyA, int modsA, bool combosA, int keyB, int modsB, bool combosB,
            bool sameOtherMod)
        {
            if (!sameGroup || keyA == 0 || keyA != keyB) return false;
            return !combosA || !combosB || modsA == modsB || sameOtherMod;
        }

        // A key and its modifiers in one stored int: the key in the low 16 bits, the modifiers above.
        public static int Pack(int key, int mods) { return (key & 0xFFFF) | ((mods & AllMods) << 16); }

        public static void Unpack(int packed, out int key, out int mods)
        {
            key = packed & 0xFFFF;
            mods = (packed >> 16) & AllMods;
        }
    }
}
