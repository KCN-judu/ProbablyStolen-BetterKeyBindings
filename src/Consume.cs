using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BetterKeyBindings
{
    // Every read of a bindable input goes through here, and the Gate decides who gets it (see Consume.Pure.cs).
    //
    // The engine's key reads (Input.GetKeyDown, GetKey, GetKeyUp) are answered through the Gate for every bindable key,
    // the scroll (Input.mouseScrollDelta) likewise, and the game's own reads of mouse buttons 3 to 5 for its
    // item handlers likewise. The options window's capture reads the real input (Raw), as does the mod's own work here.
    // While a capture waits, the options window's keyboard navigation is off too, so Space or Enter does not press the
    // selected button and the arrows do not move the selection.
    //
    // The mouse wheel is two keys here, Combo.WheelUp and Combo.WheelDown. Unity has no key code for it, so the key reads
    // are answered for those two: a notch is down for one frame and released the next (see Notches). The game's
    // bindings, its item handlers, the options window and other mods' bindings all see it as a key.
    //
    // Who a press belongs to is decided once, when it is first seen going down (OwnersOf, Rule.Resolve): the binding
    // asking for exactly the held modifiers, else the bindings asking for the most of them, else nobody, and then the
    // press goes to the game untouched. The bindings it belongs to trigger for the whole press; every read of a binding
    // only asks this answer, so letting go of a modifier halfway changes nothing.
    //
    // Only bindings that apply where the player is take part (Bindings.Active): rotating, flipping and resetting an item
    // only while one is dragged. So a press is decided completely when it goes down. A notch that belongs to a binding
    // never scrolls; any other notch scrolls at once, as without the mod.
    public static class Consume
    {
        const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly Gate gate = new Gate();
        static readonly Notches notches = new Notches();
        static readonly List<int> competitors = new List<int>();
        // The presses seen so far, by key: the frame each went down and whom it belongs to (a Rule.Resolve set).
        static readonly Dictionary<KeyCode, int[]> presses = new Dictionary<KeyCode, int[]>();
        // OwnersOf returns this while the key is up.
        public const int NotPressed = -1;
        // `frame`: the Time.frameCount the Gate was last advanced for. `raw`: nesting depth of BeginRaw / EndRaw.
        static int frame = -1, raw;
        // The scroll the game is shown this frame instead of the wheel's movement.
        static float shownY;
        // The event system whose navigation a capture turned off, and what it was before.
        static EventSystem navigationOff;
        static bool navigationWas;

        public static void Patch(Harmony harmony)
        {
            // The engine's methods are taken from delegates rather than looked up by name, as in ModKeys, so no
            // reflection goes through an engine type. The Gate answers before anyone else looks at the result.
            Read(harmony, new Func<KeyCode, bool>(Input.GetKeyDown).Method, "KeyDownPrefix", "KeyDownPostfix");
            Read(harmony, new Func<KeyCode, bool>(Input.GetKey).Method, "KeyPrefix", "KeyPostfix");
            Read(harmony, new Func<KeyCode, bool>(Input.GetKeyUp).Method, "KeyUpPrefix", "KeyUpPostfix");
            // Keys read by name cannot be told apart cheaply; while a capture waits, none of them goes down.
            Read(harmony, new Func<string, bool>(Input.GetKeyDown).Method, null, "NamedKeyDownPostfix");

            // The game's input manager reads mouse buttons apart from the keys, for its item handlers.
            var mouseDown = typeof(InputActionManager).GetMethod("GetMouseKeyDown", AnyInstance);
            var mouseUp = typeof(InputActionManager).GetMethod("GetMouseKeyUp", AnyInstance);
            if (mouseDown != null && mouseUp != null)
            {
                Read(harmony, mouseDown, null, "MouseKeyDownPostfix");
                Read(harmony, mouseUp, null, "MouseKeyUpPostfix");
            }
            else Debug.LogWarning(BetterKeyBindingsMod.Tag + "the game's mouse button reads were not found; a capture leaves them to the game");

            // The scroll axis (Input.GetAxis) is native code with nothing to patch; the game reads only mouseScrollDelta.
            // Without the scroll patch the wheel bindings still work, a notch just also scrolls, so a failure there
            // leaves the rest of the mod on.
            try
            {
                var scroll = ScrollDeltaGetter();
                if (scroll == null) throw new MissingMethodException("Input.mouseScrollDelta");
                harmony.Patch(scroll, null, Hook("ScrollDeltaPostfix"));
            }
            catch (Exception e)
            {
                Debug.LogWarning(BetterKeyBindingsMod.Tag + "the wheel's scrolling could not be hooked; a notch bound to a key also scrolls: " + e.Message);
            }
        }

        // A property has no delegate to take it from, so the scroll property is taken from the code of the game's input
        // manager, which reads it every frame.
        static MethodInfo ScrollDeltaGetter()
        {
            var update = typeof(InputActionManager).GetMethod("Update", AnyInstance);
            if (update == null) return null;
            foreach (var ins in PatchProcessor.GetOriginalInstructions(update))
            {
                var m = ins.operand as MethodInfo;
                if (m != null && m.Name == "get_mouseScrollDelta" && m.DeclaringType == typeof(Input)) return m;
            }
            return null;
        }

        // Postfixes run First, so the Gate has answered before ModKeys or any other mod's postfix looks at the result.
        static void Read(Harmony harmony, MethodInfo m, string prefix, string postfix)
        {
            var post = Hook(postfix);
            post.priority = Priority.First;
            harmony.Patch(m, prefix == null ? null : Hook(prefix), post);
        }

        static HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(typeof(Consume).GetMethod(name, AnyStatic));
        }

        public static void Reset()
        {
            RestoreNavigation();
            gate.Reset();
            notches.Reset();
            presses.Clear();
            frame = -1;
            shownY = 0;
        }

        // Reads inside BeginRaw / EndRaw see the real input: the options window's capture, and the mod's own work.
        public static void BeginRaw() { raw++; }
        public static void EndRaw() { if (raw > 0) raw--; }
        public static bool Raw { get { return raw > 0; } }

        // ---------------------------------------------------------------- frames

        static int Direction(KeyCode key) { return key == Combo.WheelUp ? Notches.WheelUp : Notches.WheelDown; }

        // Begins the frame once, whoever asks first.
        static void Advance()
        {
            if (Time.frameCount == frame) return;
            float y;
            BeginRaw();
            try { y = Input.mouseScrollDelta.y; }
            finally { EndRaw(); }
            Advance(y);
        }

        // The frame's work: step the wheel, decide which notches belong to bindings, and switch the options window's
        // navigation with the capture state. Everything here reads the real input (raw). The scroll patch calls this
        // overload with the value it already has, so the property is not read a second time.
        static void Advance(float y)
        {
            if (Time.frameCount == frame) return;
            frame = Time.frameCount;
            BeginRaw();
            try
            {
                notches.Frame(y);
                bool capturing = KeyBindHelper.SuppressHotkeys;
                bool up = y > 0 && OwnersOf(Combo.WheelUp) > 0, down = y < 0 && OwnersOf(Combo.WheelDown) > 0;
                shownY = gate.Frame(capturing, y, up, down);
                if (capturing) TurnOffNavigation(); else RestoreNavigation();
            }
            finally { EndRaw(); }
        }

        // Whom the press of this key belongs to (a Rule.Resolve set, 0 for the game), or NotPressed. Decided the first
        // time the press is seen, with the modifiers held then, and kept until the key is up.
        public static int OwnersOf(KeyCode key)
        {
            Advance();
            bool down, held;
            BeginRaw();
            try { down = Input.GetKeyDown(key); held = down || Input.GetKey(key); }
            finally { EndRaw(); }
            int[] press;
            bool known = presses.TryGetValue(key, out press);
            if (!held)
            {
                if (known) presses.Remove(key);
                return NotPressed;
            }
            // Already decided: while the key stays held, and again for other readers in the frame it went down. A key
            // seen down in a later frame is a new press (released and pressed between two reads) and is decided anew.
            if (known && (!down || press[0] == Time.frameCount)) return press[1];
            int owners = Resolve(key);
            presses[key] = new[] { Time.frameCount, owners };
            return owners;
        }

        // Gathers the modifiers of every binding on the key, the game's and other mods', and asks Rule.Resolve.
        // Raw, because Combo.Held and the bindings' key lookups read input that the Gate would otherwise filter.
        static int Resolve(KeyCode key)
        {
            BeginRaw();
            try
            {
                competitors.Clear();
                Bindings.AddCompetitors(key, competitors);
                ModKeys.AddCompetitors(key, competitors);
                return Rule.Resolve(Combo.Held(key), competitors.ToArray());
            }
            finally { EndRaw(); }
        }

        // May a binding with these modifiers on this key trigger now? Yes while the key is up (a handler only asking which
        // key to listen to), and while it is down if the press belongs to it.
        public static bool MayTrigger(KeyCode key, int mods)
        {
            int owners = OwnersOf(key);
            return owners == NotPressed || Rule.Owns(owners, mods);
        }

        // The options window's capture took this key as it went down: from now on nobody else sees it held or released.
        // Called once per key by Options.Poll.
        public static void Captured(KeyCode key)
        {
            Advance();
            gate.Take((int)key);
        }

        // The EventSystem presses the selected button on Space or Enter by itself; that cannot be told apart from the
        // capture's own key, so navigation is switched off for the capture and its previous state is put back after.
        static void TurnOffNavigation()
        {
            if (navigationOff != null) return;
            var es = EventSystem.current;
            if (es == null) return;
            navigationWas = es.sendNavigationEvents;
            es.sendNavigationEvents = false;
            navigationOff = es;
        }

        static void RestoreNavigation()
        {
            if (navigationOff != null) navigationOff.sendNavigationEvents = navigationWas;
            navigationOff = null;
        }

        // ---------------------------------------------------------------- patches

        // The wheel keys have no state in the engine: they are answered here, and the Gate filters them as any key.
        static bool KeyDownPrefix(KeyCode key, ref bool __result)
        {
            if (!Combo.IsWheel(key)) return true;
            Advance();
            __result = notches.Pressed(Direction(key));
            return false;
        }

        static bool KeyPrefix(KeyCode key, ref bool __result)
        {
            return KeyDownPrefix(key, ref __result);
        }

        static bool KeyUpPrefix(KeyCode key, ref bool __result)
        {
            if (!Combo.IsWheel(key)) return true;
            Advance();
            __result = notches.Released(Direction(key));
            return false;
        }

        // The postfixes below skip raw reads (the mod's own and the capture's) and non-bindable keys (Escape, mouse
        // buttons 0 and 1, which stay the game's).
        static void KeyDownPostfix(KeyCode key, ref bool __result)
        {
            if (raw > 0 || !Combo.IsBindable(key)) return;
            Advance();
            __result = gate.Down((int)key, __result);
        }

        static void KeyPostfix(KeyCode key, ref bool __result)
        {
            if (raw > 0 || !__result || !Combo.IsBindable(key)) return;
            Advance();
            __result = gate.Held((int)key, __result);
        }

        static void KeyUpPostfix(KeyCode key, ref bool __result)
        {
            if (raw > 0 || !__result || !Combo.IsBindable(key)) return;
            Advance();
            __result = gate.Up((int)key, __result);
        }

        // Input.GetKeyDown(string): the name would have to be parsed to know which key it is, so while a capture waits
        // every named press reads false.
        static void NamedKeyDownPostfix(ref bool __result)
        {
            if (raw > 0 || !__result) return;
            Advance();
            if (gate.Capturing) __result = false;
        }

        static void MouseKeyDownPostfix(KeyCode code, ref bool __result)
        {
            if (raw > 0 || !Combo.IsBindable(code)) return;
            Advance();
            __result = gate.Down((int)code, __result);
        }

        static void MouseKeyUpPostfix(KeyCode code, ref bool __result)
        {
            if (raw > 0 || !__result || !Combo.IsBindable(code)) return;
            Advance();
            __result = gate.Up((int)code, __result);
        }

        // Replaces the scroll the game sees with shownY, which is 0 for a notch that belongs to a binding or a capture.
        static void ScrollDeltaPostfix(ref Vector2 __result)
        {
            if (raw > 0) return;
            Advance(__result.y);
            __result.y = shownY;
        }
    }
}
