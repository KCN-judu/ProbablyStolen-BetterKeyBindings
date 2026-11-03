namespace BetterKeyBindings
{
    // The mod's decisions, kept apart from the game and from Unity so tests/PureTest.cs can run them headless.
    // Keys are KeyCode values as ints, modifiers are Combo masks (Ctrl 1, Shift 2, Alt 4). The wheel is two keys
    // outside Unity's KeyCode values (see Combo.WheelUp), and is decided like any key.
    public static class Rule
    {
        public const int AllMods = 7;

        // Who a press belongs to, decided once, when the key goes down with `held` modifiers, among the modifiers of
        // every binding on that key (`modsOnKey`). Returned as a set of modifier masks (bit 1 << mods), 0 for nobody:
        //   the bindings asking for exactly the held modifiers;
        //   else, dropping held modifiers, the bindings asking for the most of them (Ctrl + Shift + E goes to
        //   Ctrl + E and Shift + E together when both are bound, as one press of both);
        //   else nobody: the press goes to the game, untouched.
        // The bindings in the set trigger for the whole press; every other binding on the key stays quiet, whatever the
        // modifiers do after the key went down.
        public static int Resolve(int held, int[] modsOnKey)
        {
            int best = -1, owners = 0;
            foreach (int m in modsOnKey)
            {
                if ((m & ~held) != 0) continue;
                int n = Count(m);
                if (n > best) { best = n; owners = 0; }
                if (n == best) owners |= 1 << m;
            }
            return owners;
        }

        public static bool Owns(int owners, int mods) { return (owners & (1 << (mods & AllMods))) != 0; }

        static int Count(int m) { return (m & 1) + ((m >> 1) & 1) + ((m >> 2) & 1); }

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
