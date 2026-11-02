// Checks the mod's decisions (src/*.Pure.cs) outside the game: no game or Unity assembly is needed.
//   csc -out:PureTest.exe tests\PureTest.cs src\Rule.Pure.cs src\Capture.Pure.cs && PureTest.exe
using System;
using BetterKeyBindings;

static class PureTest
{
    const int None = 0, Ctrl = 1, Shift = 2, Alt = 4;
    // KeyCode values used below.
    const int A = 97, E = 101, Space = 32, LShift = 304, LCtrl = 306, Mouse3 = 326;
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static void Fire(string what, bool expected, int want, int held, params int[] others)
    {
        Check(what, Rule.MayFire(want, held, others) == expected);
    }

    // Presses keys in order (key, group, held modifiers at that moment) and decides once all are released.
    static void Captures(string what, int expectKey, int expectMods, params int[][] downs)
    {
        var c = new Capture();
        foreach (var d in downs) c.Down(d[0], d[1], d[2]);
        int key, mods;
        bool decided = c.Decide(out key, out mods);
        Check(what, decided && key == expectKey && mods == expectMods);
    }

    static int Main()
    {
        Console.WriteLine("-- firing: the most specific binding on a key wins");
        Fire("E alone fires E", true, None, None, Ctrl);
        Fire("Ctrl + E does not fire E", false, None, Ctrl, Ctrl);
        Fire("Ctrl + E fires Ctrl + E", true, Ctrl, Ctrl, None);
        Fire("E alone does not fire Ctrl + E", false, Ctrl, None, None);
        Fire("Shift + E fires E when Shift + E is free", true, None, Shift, Ctrl);
        Fire("Ctrl + Shift + E does not fire Ctrl + E", false, Ctrl, Ctrl | Shift, None, Ctrl | Shift);
        Fire("Ctrl + Shift + E fires Ctrl + Shift + E", true, Ctrl | Shift, Ctrl | Shift, None, Ctrl);
        Fire("Ctrl + Shift + E does not fire E", false, None, Ctrl | Shift, Ctrl, Ctrl | Shift);
        Fire("Ctrl + Alt + E fires Ctrl + E when Shift + Alt + E exists", true, Ctrl, Ctrl | Alt, Shift | Alt);
        Fire("Alt + E fires E when only Ctrl + E exists", true, None, Alt, Ctrl);

        Console.WriteLine("-- capture: decided once every key is released");
        Captures("Shift held, then Space: Shift + Space", Space, Shift,
            new[] { LShift, Shift, None }, new[] { Space, 0, Shift });
        Captures("Shift alone: Left Shift", LShift, None, new[] { LShift, Shift, None });
        Captures("Ctrl, then Left Shift, no other key: Ctrl + Left Shift", LShift, Ctrl,
            new[] { LCtrl, Ctrl, None }, new[] { LShift, Shift, Ctrl });
        Captures("Ctrl + Shift + A: all three", A, Ctrl | Shift,
            new[] { LCtrl, Ctrl, None }, new[] { LShift, Shift, Ctrl }, new[] { A, 0, Ctrl | Shift });
        Captures("Two main keys: the last one", E, None, new[] { A, 0, None }, new[] { E, 0, None });
        Captures("Mouse 4 with Alt", Mouse3, Alt, new[] { Mouse3, 0, Alt });
        Captures("A modifier pressed after the main key does not join it", A, None,
            new[] { A, 0, None }, new[] { LShift, Shift, None });
        var empty = new Capture();
        int k, m;
        Check("nothing pressed: nothing decided", !empty.Decide(out k, out m) && !empty.Started);
        var reset = new Capture();
        reset.Down(LShift, Shift, None);
        reset.Reset();
        Check("reset forgets earlier keys", !reset.Started && reset.PressedModifiers == 0);
        var repeat = new Capture();
        repeat.Down(LShift, Shift, None);
        repeat.Down(LShift, Shift, None);
        Check("a key reported twice counts once", repeat.Pressed.Count == 1);

        Console.WriteLine("-- conflicts");
        Check("same combination collides", Rule.Collides(true, E, Ctrl, true, E, Ctrl, true, false));
        Check("same key, other modifiers: no collision", !Rule.Collides(true, E, Ctrl, true, E, None, true, false));
        Check("other group: no collision", !Rule.Collides(false, E, Ctrl, true, E, Ctrl, true, false));
        Check("unbound keys never collide", !Rule.Collides(true, 0, None, true, 0, None, true, false));
        Check("an option reading only its key collides on the key", Rule.Collides(true, E, Ctrl, true, E, None, false, false));
        Check("same other mod, same key: collides", Rule.Collides(true, Mouse3, Ctrl, true, Mouse3, None, true, true));
        Check("different keys: no collision", !Rule.Collides(true, E, Ctrl, true, A, Ctrl, true, false));

        Console.WriteLine("-- storage");
        int key, mods;
        Rule.Unpack(Rule.Pack(LShift, Ctrl | Alt), out key, out mods);
        Check("pack and unpack round-trip", key == LShift && mods == (Ctrl | Alt));
        Rule.Unpack(Rule.Pack(Mouse3, 0xFF), out key, out mods);
        Check("unknown modifier bits are dropped", key == Mouse3 && mods == Rule.AllMods);

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
