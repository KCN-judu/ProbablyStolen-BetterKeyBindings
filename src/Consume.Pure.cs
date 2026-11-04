using System.Collections.Generic;

namespace BetterKeyBindings
{
    // Who gets an input: the mod's consumption decisions in one place, kept apart from Unity so tests/PureTest.cs can
    // run them headless. Every read of a bindable input in the game asks it (see Consume); keys are KeyCode values as
    // ints, the wheel included. Two things take an input away from everyone else:
    //   A capture in the options window takes every bindable key, mouse button and wheel notch pressed while it waits:
    //   nobody else sees that press, the key held, or its release, even when the release comes after the capture.
    //   Keys already down when the capture began are left alone, so their release reaches whoever saw them go down.
    //   A wheel notch that belongs to a binding (see Consume.OwnersOf) never scrolls.
    public sealed class Gate
    {
        readonly HashSet<int> taken = new HashSet<int>();
        readonly List<int> releasing = new List<int>();
        bool capturing;

        // Begins a frame: whether a capture waits for keys, the wheel's movement, and whether the notch in each
        // direction belongs to a binding. Returns the scroll the game is shown: nothing of a notch that belongs to a
        // binding or to a capture, all of any other.
        public float Frame(bool capturing, float wheel, bool ownedUp, bool ownedDown)
        {
            this.capturing = capturing;
            foreach (int k in releasing) taken.Remove(k);
            releasing.Clear();
            if (capturing || (wheel > 0 && ownedUp) || (wheel < 0 && ownedDown)) return 0;
            return wheel;
        }

        public bool Capturing { get { return capturing; } }

        // What a reader sees of a bindable key, given what the key really did this frame. While capturing, the press is
        // hidden and remembered in `taken`, so the held state and the later release stay hidden from the same readers.
        public bool Down(int key, bool down)
        {
            if (!down) return false;
            if (capturing) { taken.Add(key); return false; }
            // Pressed again after a taken release that nobody asked about.
            taken.Remove(key);
            return true;
        }

        public bool Held(int key, bool held) { return held && !taken.Contains(key); }

        public bool Up(int key, bool up)
        {
            if (!up) return false;
            if (!taken.Contains(key)) return true;
            // `releasing` keeps the key taken until the next Frame, because several readers ask in one frame and all of them
            // must get the same answer. Every reader in this frame sees no release; the key is free again from the next frame.
            releasing.Add(key);
            return false;
        }

        // A capture took this key as it went down: nobody else sees it held or released.
        public void Take(int key) { taken.Add(key); }

        public void Reset()
        {
            taken.Clear();
            releasing.Clear();
            capturing = false;
        }
    }
}
