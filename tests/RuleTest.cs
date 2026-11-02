// Checks which binding fires when several share a key (src/Rule.cs), outside the game.
//   csc -out:RuleTest.exe tests\RuleTest.cs src\Rule.cs && RuleTest.exe
using System;
using BetterKeyBindings;

static class RuleTest
{
    const int None = 0, Ctrl = 1, Shift = 2, Alt = 4;
    static int failed;

    static void Check(string what, bool expected, int want, int held, params int[] others)
    {
        bool got = Rule.MayFire(want, held, others);
        if (got != expected) failed++;
        Console.WriteLine((got == expected ? "ok    " : "FAIL  ") + what);
    }

    static int Main()
    {
        // E rotates, Ctrl + E is something else.
        Check("E alone fires E", true, None, None, Ctrl);
        Check("Ctrl + E does not fire E", false, None, Ctrl, Ctrl);
        Check("Ctrl + E fires Ctrl + E", true, Ctrl, Ctrl, None);
        Check("E alone does not fire Ctrl + E", false, Ctrl, None, None);
        // Nothing on Shift + E: E still fires with Shift held, as in the game.
        Check("Shift + E fires E when Shift + E is free", true, None, Shift, Ctrl);
        // Ctrl + Shift + E fires only the most specific binding.
        Check("Ctrl + Shift + E does not fire Ctrl + E", false, Ctrl, Ctrl | Shift, None, Ctrl | Shift);
        Check("Ctrl + Shift + E fires Ctrl + Shift + E", true, Ctrl | Shift, Ctrl | Shift, None, Ctrl);
        Check("Ctrl + Shift + E does not fire E", false, None, Ctrl | Shift, Ctrl, Ctrl | Shift);
        // Unrelated modifiers do not make a binding more specific.
        Check("Ctrl + Alt + E fires Ctrl + E when Shift + Alt + E exists", true, Ctrl, Ctrl | Alt, Shift | Alt);
        Check("Alt + E fires E when only Ctrl + E exists", true, None, Alt, Ctrl);
        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
