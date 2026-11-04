using System.Collections.Generic;

namespace BetterKeyBindings
{
    // What a binding becomes while it waits for keys in the options window. The caller reports each key that goes
    // down, with its modifier group (0 for a key that is not a modifier) and the modifiers held at that moment;
    // nothing is decided until every reported key is released.
    //   The last key that is not a modifier is the main key, with the modifiers held when it went down.
    //   With only modifiers pressed, the last of them is the main key and the others its modifiers.
    public sealed class Capture
    {
        readonly List<int> keys = new List<int>();
        readonly List<int> groups = new List<int>();
        int main, mainMods, lastModifier, lastModifierGroup;

        public void Reset()
        {
            keys.Clear();
            groups.Clear();
            main = mainMods = lastModifier = lastModifierGroup = 0;
        }

        // `heldMods` is what was held when this key went down; it is kept only for a main key, so Ctrl + E pressed
        // with Ctrl first and E second still gives Ctrl + E.
        public void Down(int key, int group, int heldMods)
        {
            if (!keys.Contains(key)) { keys.Add(key); groups.Add(group); }
            if (group != 0) { lastModifier = key; lastModifierGroup = group; }
            else { main = key; mainMods = heldMods & Rule.AllMods; }
        }

        public bool Started { get { return keys.Count > 0; } }
        public IList<int> Pressed { get { return keys; } }
        public int Main { get { return main; } }
        public int MainMods { get { return mainMods; } }

        public int PressedModifiers
        {
            get { int m = 0; foreach (var g in groups) m |= g; return m; }
        }

        // The binding, once every pressed key is up. False if nothing was pressed.
        public bool Decide(out int key, out int mods)
        {
            key = mods = 0;
            if (keys.Count == 0) return false;
            if (main != 0) { key = main; mods = mainMods; return true; }
            key = lastModifier;
            mods = PressedModifiers & ~lastModifierGroup;
            return true;
        }
    }
}
