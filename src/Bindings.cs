using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BetterKeyBindings
{
    // What the game reads at play time.
    //
    // The game keeps one KeyCode per binding (KeyBindHelper); this mod keeps the modifiers next to it. A binding fires
    // when its key is pressed, its modifiers are held, and no other binding on the same key asks for more of the
    // held modifiers. So with E bound to one action and Ctrl + E to another, Ctrl + E runs only the second; E alone,
    // or Shift + E when nothing is bound to it, still runs the first, as in the game without the mod.
    //
    // The game reads bindings in three ways, and each is covered:
    //   KeyBindHelper.GetKeyDown / GetKey(id)        replaced (toolbox, bell, options, trash, hints, ...);
    //   KeyBindHelper.Get(id) inside OnEventPress    returns None while the binding may not fire (dragging,
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

        static int gate;
        static bool evaluating;
        static readonly List<int> sameKey = new List<int>();

        // ---------------------------------------------------------------- stored values

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

        // May this binding fire now, given its key is pressed? See the class comment.
        public static bool MayFire(string id, KeyCode key)
        {
            if (key == KeyCode.None) return false;
            int want = ModsOf(id), held = Combo.Held(key);
            if ((want & ~held) != 0) return false;
            if (evaluating) return true;
            evaluating = true;
            try
            {
                sameKey.Clear();
                foreach (var b in KeyBindHelper.Bindings)
                    if (b.id != id && KeyBindHelper.Get(b.id) == key) sameKey.Add(ModsOf(b.id));
                ModKeys.AddCompetitors(key, sameKey);
                return Rule.MayFire(want, held, sameKey.ToArray());
            }
            finally { evaluating = false; }
        }

        // Modifiers of the game's bindings on a key, as competitors of other mods' bindings.
        public static void AddCompetitors(KeyCode key, List<int> into)
        {
            bool was = evaluating;
            evaluating = true;
            try
            {
                foreach (var b in KeyBindHelper.Bindings)
                    if (KeyBindHelper.Get(b.id) == key) into.Add(ModsOf(b.id));
            }
            finally { evaluating = was; }
        }

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

        static void GateEnter() { gate++; }
        static void GateLeave() { if (gate > 0) gate--; }

        static bool GetKeyDownPrefix(string id, ref bool __result)
        {
            if (KeyBindHelper.SuppressHotkeys) { __result = false; return false; }
            KeyCode k = KeyBindHelper.Get(id);
            __result = k != KeyCode.None && Input.GetKeyDown(k) && MayFire(id, k);
            return false;
        }

        static bool GetKeyPrefix(string id, ref bool __result)
        {
            if (KeyBindHelper.SuppressHotkeys) { __result = false; return false; }
            KeyCode k = KeyBindHelper.Get(id);
            __result = k != KeyCode.None && Input.GetKey(k) && MayFire(id, k);
            return false;
        }

        static void GetPostfix(string id, ref KeyCode __result)
        {
            if (gate > 0 && !evaluating && __result != KeyCode.None && !MayFire(id, __result)) __result = KeyCode.None;
        }

        static void KeyNamePostfix(KeyCode key, ref string __result)
        {
            string name = Combo.KeyName(key);
            if (name != null) __result = name;
        }

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
            if (replaced != 2) Debug.LogWarning(BetterKeyBindingsMod.Tag + "drag keys: " + replaced + " of 2 found");
            return list;
        }

        static bool DragKeyHeld(KeyCode gameKey)
        {
            return HoldActive(gameKey == KeyCode.LeftAlt ? TakeOne : TakeHalf);
        }
    }
}
