namespace BetterKeyBindings
{
    // Which binding fires when several share a main key. Kept apart from the game so tests/RuleTest.cs can run it.
    public static class Rule
    {
        // A binding asking for modifiers `want` may fire while `held` are down, unless another binding on the same
        // key asks for more modifiers and all of those are down too: the most specific binding wins.
        public static bool MayFire(int want, int held, int[] othersOnSameKey)
        {
            if ((want & ~held) != 0) return false;
            foreach (int other in othersOnSameKey)
                if (other != want && (other & want) == want && (other & ~held) == 0) return false;
            return true;
        }
    }
}
