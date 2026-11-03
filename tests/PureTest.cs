// Checks the mod's decisions (src/*.Pure.cs) outside the game: no game or Unity assembly is needed.
//   csc -out:PureTest.exe tests\PureTest.cs src\Rule.Pure.cs src\Capture.Pure.cs src\Wheel.Pure.cs src\Consume.Pure.cs && PureTest.exe
using System;
using BetterKeyBindings;

static class PureTest
{
    const int None = 0, Ctrl = 1, Shift = 2, Alt = 4;
    // KeyCode values used below.
    const int A = 97, E = 101, Space = 32, LShift = 304, LCtrl = 306, Mouse3 = 326, WheelUp = 1001;
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static void Triggers(string what, bool expected, int want, int held, params int[] others)
    {
        var all = new int[others.Length + 1];
        others.CopyTo(all, 0);
        all[others.Length] = want;
        Check(what, Rule.Owns(Rule.Resolve(held, all), want) == expected);
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

    // Feeds the wheel's movement frame by frame and spells out what the up key did: D down, U released, - nothing,
    // B both (released and pressed again). Then the same for the down key, after a slash.
    static void Wheel(string what, string expected, params float[] frames)
    {
        var n = new Notches();
        string up = "", down = "";
        foreach (float f in frames)
        {
            n.Frame(f);
            up += Letter(n.Pressed(Notches.WheelUp), n.Released(Notches.WheelUp));
            down += Letter(n.Pressed(Notches.WheelDown), n.Released(Notches.WheelDown));
        }
        string got = up + "/" + down;
        Check(what + (got == expected ? "" : " (got " + got + ")"), got == expected);
    }

    static string Letter(bool pressed, bool released)
    {
        return pressed && released ? "B" : pressed ? "D" : released ? "U" : "-";
    }

    static float[] Concat(float[] a, float[] b)
    {
        var r = new float[a.Length + b.Length];
        a.CopyTo(r, 0);
        b.CopyTo(r, a.Length);
        return r;
    }

    static int Main()
    {
        Console.WriteLine("-- triggering: exact match, else downgrade, else the game");
        Triggers("E alone triggers E", true, None, None, Ctrl);
        Triggers("Ctrl + E does not trigger E", false, None, Ctrl, Ctrl);
        Triggers("Ctrl + E triggers Ctrl + E", true, Ctrl, Ctrl, None);
        Triggers("E alone does not trigger Ctrl + E", false, Ctrl, None, None);
        Triggers("Shift + E triggers E when Shift + E is free", true, None, Shift, Ctrl);
        Triggers("Ctrl + Shift + E does not trigger Ctrl + E", false, Ctrl, Ctrl | Shift, None, Ctrl | Shift);
        Triggers("Ctrl + Shift + E triggers Ctrl + Shift + E", true, Ctrl | Shift, Ctrl | Shift, None, Ctrl);
        Triggers("Ctrl + Shift + E does not trigger E", false, None, Ctrl | Shift, Ctrl, Ctrl | Shift);
        Triggers("Ctrl + Alt + E triggers Ctrl + E when Shift + Alt + E exists", true, Ctrl, Ctrl | Alt, Shift | Alt);
        Triggers("Alt + E triggers E when only Ctrl + E exists", true, None, Alt, Ctrl);
        Triggers("Ctrl + Shift + E triggers Ctrl + E and Shift + E together", true, Ctrl, Ctrl | Shift, Shift);
        Triggers("... and the other one too", true, Shift, Ctrl | Shift, Ctrl);
        Triggers("an exact match wins over a downgrade", false, Ctrl, Ctrl | Shift, Ctrl | Shift);
        Check("no binding left after downgrading: the press goes to the game", Rule.Resolve(Ctrl, new[] { Shift }) == 0);
        Check("no binding on the key: the game", Rule.Resolve(None, new int[0]) == 0);

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

        Captures("Ctrl + wheel up", WheelUp, Ctrl, new[] { LCtrl, Ctrl, None }, new[] { WheelUp, 0, Ctrl });

        Console.WriteLine("-- wheel: one press per notch");
        Wheel("one notch up: down one frame, released the next", "DU--/----", 1, 0, 0, 0);
        Wheel("one notch down", "----/DU--", -1, 0, 0, 0);
        Wheel("three notches in one frame: three presses, one per frame", "DBBU-/-----", 3, 0, 0, 0, 0);
        Wheel("a notch every frame: pressed again each frame", "DBBU/----", 1, 1, 1, 0);
        Wheel("tenths add up to a notch", "---------DU/-----------", .1f, .1f, .1f, .1f, .1f, .1f, .1f, .1f, .1f, .1f, 0);
        Wheel("half a notch is not a notch", "---/---", .5f, 0, 0);
        Wheel("turning back drops what was left over", "---/-DU", .5f, -1, .5f);
        Wheel("turning back drops queued notches", "DBU--/--DU-", 3, 0, -1, 0, 0);
        Wheel("a burst is capped", new string('D', 1) + new string('B', Notches.MaxQueued - 1) + "U/" + new string('-', Notches.MaxQueued + 1),
            Concat(new float[] { 100 }, new float[Notches.MaxQueued]));

        Console.WriteLine("-- wheel: a notch goes to a binding or to the game, as a key does");
        Check("nothing bound: the wheel scrolls", Rule.Resolve(None, new int[0]) == 0);
        Check("wheel up bound: the binding takes it", Rule.Resolve(None, new[] { None }) != 0);
        Check("Ctrl + wheel up bound, no Ctrl: the game", Rule.Resolve(None, new[] { Ctrl }) == 0);
        Check("Ctrl + wheel up bound, Ctrl held: the binding", Rule.Owns(Rule.Resolve(Ctrl, new[] { Ctrl }), Ctrl));
        Check("wheel up bound, Ctrl + wheel up: downgraded to the binding", Rule.Owns(Rule.Resolve(Ctrl, new[] { None }), None));
        Check("Ctrl + Shift + wheel up bound, only Ctrl held: the game", Rule.Resolve(Ctrl, new[] { Ctrl | Shift }) == 0);

        Console.WriteLine("-- consumption: a capture keeps what it takes from everyone else");
        var g = new Gate();
        g.Frame(true, 0, false, false);
        Check("a key going down during a capture is not seen", !g.Down(Space, true));
        Check("nor held", !g.Held(Space, true));
        g.Frame(true, 0, false, false);
        Check("nor released", !g.Up(Space, true) && !g.Up(Space, true));
        g.Frame(false, 0, false, false);
        Check("after its release it is free again", g.Down(Space, true) && g.Held(Space, true) && g.Up(Space, true));
        g = new Gate();
        g.Frame(false, 0, false, false);
        Check("no capture: keys pass", g.Down(E, true) && g.Held(E, true));
        g.Frame(true, 0, false, false);
        Check("a key down before the capture stays held", g.Held(E, true));
        Check("and its release still arrives", g.Up(E, true));
        g = new Gate();
        g.Frame(true, 0, false, false);
        g.Take(Mouse3);
        g.Frame(false, 0, false, false);
        Check("a key the capture took stays taken after the capture: held", !g.Held(Mouse3, true));
        Check("and released", !g.Up(Mouse3, true));
        g = new Gate();
        g.Frame(true, 0, false, false);
        g.Take(A);
        g.Frame(false, 0, false, false);
        Check("a taken key whose release nobody asked about is free once pressed again", g.Down(A, true) && g.Held(A, true));
        g = new Gate();
        Check("a capture takes every notch from the scrolling", g.Frame(true, 1, false, false) == 0 && g.Frame(false, 0, false, false) == 0);
        g = new Gate();
        Check("a notch that belongs to a binding never scrolls", g.Frame(false, 1, true, false) == 0 && g.Frame(false, 0, true, false) == 0);
        Check("a notch that belongs to nobody scrolls at once", g.Frame(false, 1, false, false) == 1);
        Check("a binding on the other direction does not keep it", g.Frame(false, -1, true, false) == -1);

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
        Rule.Unpack(Rule.Pack(WheelUp, Shift), out key, out mods);
        Check("the wheel packs like a key", key == WheelUp && mods == Shift);
        Rule.Unpack(Rule.Pack(Mouse3, 0xFF), out key, out mods);
        Check("unknown modifier bits are dropped", key == Mouse3 && mods == Rule.AllMods);

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
