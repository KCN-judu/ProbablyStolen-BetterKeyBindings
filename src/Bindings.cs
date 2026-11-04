using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BetterKeyBindings
{
    // What the game reads at play time.
    //
    // The game keeps one KeyCode per binding (KeyBindHelper); this mod keeps the modifiers next to it. Whom a press
    // belongs to is decided once, when the key goes down (Consume.OwnersOf): the binding asking for exactly the held
    // modifiers, else the ones asking for the most of them. So with E bound to one action and Ctrl + E to another,
    // Ctrl + E runs only the second; E alone, or Shift + E when nothing is bound to it, still runs the first, as in
    // the game without the mod.
    //
    // The game reads bindings in three ways, and each is covered:
    //   KeyBindHelper.GetKeyDown / GetKey(id)        replaced (toolbox, bell, options, trash, hints, ...);
    //   KeyBindHelper.Get(id) inside OnEventPress    returns None while the binding may not trigger (dragging,
    //                                                sort menu, quick move), so two bindings on one key stay apart;
    //   hard-coded Alt / Shift while dragging        now the "hold" bindings below.
    public static class Bindings
    {
        const string ModsPref = "BetterKeyBindings.mods.";
        const string HoldPref = "BetterKeyBindings.hold.";

        const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // Modifiers of the game's own bindings, by binding id. Missing means none.
        static readonly Dictionary<string, int> mods = new Dictionary<string, int>();

        // Keys the game checks itself while an item is held, now bindable. Each is "hold this while ...".
        // Deprecated ones still work but nothing in the current game reaches them: picking up one or half only
        // matters for an item worth several units, and no item the game creates in play has more than one.
        public sealed class Hold
        {
            public string Id, Group;
            public KeyCode DefaultKey, Key;
            public int Mods;
            public bool Deprecated;
        }

        public const int TakeOne = 0, TakeHalf = 1, Precise = 2;
        public static readonly Hold[] Holds =
        {
            new Hold { Id = "take_one", Group = "drag", DefaultKey = KeyCode.LeftAlt, Deprecated = true },
            new Hold { Id = "take_half", Group = "drag", DefaultKey = KeyCode.LeftShift, Deprecated = true },
            new Hold { Id = "precise_place", Group = "select", DefaultKey = KeyCode.LeftShift },
        };

        // `gate`: how many gated handlers (see Patch) are running now; KeyBindHelper.Get answers None for a binding
        // that may not trigger only while it is above 0. `evaluating`: set while the mod itself lists the bindings on
        // a key (AddCompetitors), so those Get calls return the plain key and do not recurse into the decision.
        static int gate;
        static bool evaluating;

        // ---------------------------------------------------------------- stored values

        // Reads the saved modifiers into memory. The game's own keys are not touched: it stores and loads them itself.
        // Called on every enable, so a change made while the mod was off is picked up.
        public static void Load()
        {
            mods.Clear();
            foreach (var b in KeyBindHelper.Bindings)
            {
                int m = 0;
                try { m = PlayerPrefs.GetInt(ModsPref + b.id, 0) & Combo.All; } catch (Exception) { }
                if (m != 0) mods[b.id] = m;
            }
            foreach (var h in Holds)
            {
                h.Key = h.DefaultKey; h.Mods = 0;
                try
                {
                    if (PlayerPrefs.HasKey(HoldPref + h.Id)) Combo.Unpack(PlayerPrefs.GetInt(HoldPref + h.Id), out h.Key, out h.Mods);
                }
                catch (Exception) { }
            }
        }

        public static int ModsOf(string bindingId)
        {
            int m;
            return mods.TryGetValue(bindingId, out m) ? m : 0;
        }

        public static void SetMods(string bindingId, int m)
        {
            m &= Combo.All;
            if (m == 0) mods.Remove(bindingId); else mods[bindingId] = m;
            try
            {
                if (m == 0) PlayerPrefs.DeleteKey(ModsPref + bindingId); else PlayerPrefs.SetInt(ModsPref + bindingId, m);
                PlayerPrefs.Save();
            }
            catch (Exception) { }
        }

        public static void SetHold(Hold h, KeyCode key, int m)
        {
            h.Key = key; h.Mods = m & Combo.All;
            try { PlayerPrefs.SetInt(HoldPref + h.Id, Combo.Pack(h.Key, h.Mods)); PlayerPrefs.Save(); } catch (Exception) { }
        }

        // ---------------------------------------------------------------- deciding

        // May this binding trigger now? Not where it does not apply; elsewhere the press of its key decides (see
        // Consume.OwnersOf).
        public static bool MayTrigger(string id, KeyCode key)
        {
            if (key == KeyCode.None || !Active(id)) return false;
            if (evaluating) return true;
            return Consume.MayTrigger(key, ModsOf(id));
        }

        // Bindings that only apply in one situation; elsewhere their key goes to whoever else wants it, the game
        // included. Only a situation the game shows in a public, cheap state is used: an item being dragged. Every
        // other binding applies everywhere, since the game decides inside each action, from many private states,
        // whether it does anything.
        static readonly HashSet<string> whileDragging = new HashSet<string> { "rotate_cw", "rotate_ccw", "flip_item", "reset_orientation" };

        public static bool Active(string id)
        {
            if (!whileDragging.Contains(id)) return true;
            var drag = ItemMouseDragHandler.current;
            return drag != null && drag.IsDraggingItem;
        }

        // Modifiers of the game's bindings on a key, to decide whom a press belongs to.
        public static void AddCompetitors(KeyCode key, List<int> into)
        {
            bool was = evaluating;
            evaluating = true;
            try
            {
                foreach (var b in KeyBindHelper.Bindings)
                    if (Active(b.id) && KeyBindHelper.Get(b.id) == key) into.Add(ModsOf(b.id));
            }
            finally { evaluating = was; }
        }

        // Whether a "hold" binding is held now: its key (either side for a modifier) and exactly the modifiers it asks for
        // or more. Not routed through Consume.OwnersOf: a held key has no press to decide, and a wheel cannot be held.
        public static bool HoldActive(int index)
        {
            var h = Holds[index];
            if (h.Key == KeyCode.None || !Combo.KeyHeld(h.Key)) return false;
            return (h.Mods & ~Combo.Held(h.Key)) == 0;
        }

        // ---------------------------------------------------------------- patches

        public static void Patch(Harmony harmony)
        {
            // Types are named at each call: the game's mod scanner asks for a permission when reflection
            // goes through a Type it cannot see.
            // GetKeyDown and GetKey are replaced outright (prefix returns false), because the game's version would read the
            // plain key. Get only gets a postfix: it is called from too many places to replace.
            harmony.Patch(typeof(KeyBindHelper).GetMethod("GetKeyDown", AnyStatic), Hook("GetKeyDownPrefix"));
            harmony.Patch(typeof(KeyBindHelper).GetMethod("GetKey", AnyStatic), Hook("GetKeyPrefix"));
            harmony.Patch(typeof(KeyBindHelper).GetMethod("Get", AnyStatic), null, Hook("GetPostfix"));
            harmony.Patch(typeof(KeyBindHelper).GetMethod("KeyName", AnyStatic), null, Hook("KeyNamePostfix"));

            // Handlers that compare the pressed keys with KeyBindHelper.Get(id). Only their press is gated: their
            // key listeners and releases keep the plain key, so letting go of a modifier first never leaves one stuck.
            Gate(harmony, typeof(ItemMouseDragHandler).GetMethod("OnEventPress", AnyInstance));
            Gate(harmony, typeof(ItemContextHandler).GetMethod("OnEventPress", AnyInstance));
            Gate(harmony, typeof(ItemQuickTransferHandler).GetMethod("OnEventPress", AnyInstance));
            Gate(harmony, typeof(ItemQuickTransferAltHandler).GetMethod("OnEventPress", AnyInstance));
            Gate(harmony, typeof(ItemQuickTransferAltHandler1).GetMethod("OnEventPress", AnyInstance));
            // The dialogue handler reacts to any key it listens to, so its listener itself is gated.
            Gate(harmony, typeof(DialogueHandler).GetMethod("get_key2", AnyInstance));

            harmony.Patch(typeof(ItemMouseDragHandler).GetMethod("StartDrag", AnyInstance), null, null, Hook("StartDragTranspiler"));
            harmony.Patch(typeof(ItemMultiSelectHandler).GetMethod("IsPreciseModifierHeld", AnyInstance | AnyStatic),
                Hook("PreciseHeldPrefix"));
            harmony.Patch(typeof(ToolboxHelper).GetMethod("SlotLabel", AnyStatic), Hook("SlotLabelPrefix"));
        }

        static HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(typeof(Bindings).GetMethod(name, AnyStatic));
        }

        static void Gate(Harmony harmony, MethodInfo m)
        {
            if (m == null) { Debug.LogWarning(BetterKeyBindingsMod.Tag + "a key handler was not found; it keeps the game's behaviour"); return; }
            harmony.Patch(m, Hook("GateEnter"), null, null, Hook("GateLeave"));
        }

        // A counter, so nesting stays correct. GateLeave is patched in as a finalizer, so it runs on every way out of
        // the handler, exceptions included, and the count cannot stay raised.
        static void GateEnter() { gate++; }
        static void GateLeave() { if (gate > 0) gate--; }
        // SuppressHotkeys is the game's switch that turns hotkeys off (Consume reads it as "a capture waits"); no binding fires then.
        static bool GetKeyDownPrefix(string id, ref bool __result)
        {
            if (KeyBindHelper.SuppressHotkeys) { __result = false; return false; }
            KeyCode k = KeyBindHelper.Get(id);
            __result = k != KeyCode.None && Input.GetKeyDown(k) && MayTrigger(id, k);
            return false;
        }

        static bool GetKeyPrefix(string id, ref bool __result)
        {
            if (KeyBindHelper.SuppressHotkeys) { __result = false; return false; }
            KeyCode k = KeyBindHelper.Get(id);
            __result = k != KeyCode.None && Input.GetKey(k) && MayTrigger(id, k);
            return false;
        }

        // Inside a gated handler a binding that does not own the press reads as unbound, so the handler's comparison
        // with the pressed key fails for it. Outside those handlers (menus, labels) the key is reported as stored.
        static void GetPostfix(string id, ref KeyCode __result)
        {
            if (gate == 0 || evaluating || __result == KeyCode.None) return;
            if (!MayTrigger(id, __result)) __result = KeyCode.None;
        }

        static void KeyNamePostfix(KeyCode key, ref string __result)
        {
            string name = Combo.KeyName(key);
            if (name != null) __result = name;
        }

        // The game's check for the precise placement modifier (hard-coded Shift) answers for the mod's binding instead.
        static bool PreciseHeldPrefix(ref bool __result)
        {
            __result = HoldActive(Precise);
            return false;
        }

        // The toolbox shows a slot's key when it is not the default one; show the whole combination.
        static bool SlotLabelPrefix(int slotIndex, ref string __result)
        {
            string id = KeyBindHelper.ToolboxSlot(slotIndex);
            int m = ModsOf(id);
            if (m == 0) return true;
            string label = LocHelper.GetLocalizedMechanic("mech_toolbox_label_slot", new object[] { slotIndex + 1 });
            __result = label + " (" + Combo.Name(KeyBindHelper.Get(id), m) + ")";
            return false;
        }

        // A transpiler rather than a prefix, because StartDrag reads the keys in the middle of a long method.
        // StartDrag reads Input.GetKey(LeftAlt) for "pick up only one" and Input.GetKey(LeftShift) for "pick up half". Those
        // two calls go to DragKeyHeld instead, which gets the same key and answers for the binding behind it.
        static IEnumerable<CodeInstruction> StartDragTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var dragKeyHeld = typeof(Bindings).GetMethod("DragKeyHeld", AnyStatic);
            var list = new List<CodeInstruction>(instructions);
            int replaced = 0;
            for (int i = 1; i < list.Count; i++)
            {
                var called = list[i].operand as MethodInfo;
                if (called == null || called.Name != "GetKey" || called.DeclaringType != typeof(Input)) continue;
                if (!list[i - 1].LoadsConstant((long)KeyCode.LeftAlt) && !list[i - 1].LoadsConstant((long)KeyCode.LeftShift)) continue;
                list[i].operand = dragKeyHeld;
                replaced++;
            }
            // A game update that changes StartDrag shows up here as a count other than 2; the keys then keep the game's behaviour.
            if (replaced != 2) Debug.LogWarning(BetterKeyBindingsMod.Tag + "drag keys: " + replaced + " of 2 found");
            return list;
        }

        static bool DragKeyHeld(KeyCode gameKey)
        {
            return HoldActive(gameKey == KeyCode.LeftAlt ? TakeOne : TakeHalf);
        }
    }
}
