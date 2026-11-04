using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BetterKeyBindings
{
    // Key combinations for the key bindings that other mods add to the options.
    //
    // Those mods store a single key and read it themselves with Input.GetKeyDown / Input.GetKey. The modifiers of
    // their bindings are kept here, and when one of those reads comes back true for a key such a binding uses, the
    // reading assembly decides: a read by the mod that owns the binding counts only if the press belongs to that
    // binding (see Consume.OwnersOf), any other read is left alone. The game's own direct reads are never touched.
    // Releases are not gated.
    public static class ModKeys
    {
        const string ModsPref = "BetterKeyBindings.optmods.";
        const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // One option of another mod: its key as that mod reads it, the assembly that reads it, and the modifiers kept here.
        sealed class Binding { public string OptionId; public Assembly Owner; public KeyCode Key; public int Mods; }

        static readonly Dictionary<KeyCode, List<Binding>> byKey = new Dictionary<KeyCode, List<Binding>>();
        // `dirty`: the table below is rebuilt on the next read (options changed, mods loaded). `checking`: stops the
        // postfix from running on the Input reads it makes itself.
        static bool dirty = true, checking;
        static Assembly self;
        // Assemblies already logged, so the log names each other mod once.
        static readonly HashSet<Assembly> seen = new HashSet<Assembly>();

        public static void Patch(Harmony harmony)
        {
            self = typeof(ModKeys).Assembly;
            // The methods are taken from delegates rather than looked up by name, so no reflection goes through
            // an engine type.
            harmony.Patch(new Func<KeyCode, bool>(Input.GetKeyDown).Method, null, Hook("ReadPostfix"));
            harmony.Patch(new Func<KeyCode, bool>(Input.GetKey).Method, null, Hook("ReadPostfix"));
        }

        static HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(typeof(ModKeys).GetMethod(name, AnyStatic));
        }

        // An option added by another mod (not the game's, not one of this mod's own).
        public static bool IsModOption(string optionId, string selfId)
        {
            var e = GameOptionRegistry.Find(optionId);
            return e != null && e.kind == GameOptionRegistry.Kind.KeyBind && !string.IsNullOrEmpty(e.source) && e.source != selfId;
        }

        public static string SourceOf(string optionId)
        {
            var e = GameOptionRegistry.Find(optionId);
            return e == null ? null : e.source;
        }

        public static int SavedMods(string optionId)
        {
            try { return PlayerPrefs.GetInt(ModsPref + optionId, 0) & Combo.All; } catch (Exception) { return 0; }
        }

        public static void SetMods(string optionId, int mods)
        {
            mods &= Combo.All;
            try
            {
                if (mods == 0) PlayerPrefs.DeleteKey(ModsPref + optionId); else PlayerPrefs.SetInt(ModsPref + optionId, mods);
                PlayerPrefs.Save();
            }
            catch (Exception) { }
            dirty = true;
        }

        public static void MarkDirty() { dirty = true; }

        // Modifiers of the other mods' bindings on a key, to decide whom a press belongs to.
        public static void AddCompetitors(KeyCode key, List<int> into)
        {
            Refresh();
            List<Binding> list;
            if (byKey.TryGetValue(key, out list)) foreach (var b in list) into.Add(b.Mods);
        }

        // Rebuilds byKey from the option registry: every key binding option with a source mod that is loaded and is not
        // this mod. The owner assembly is found through ModLoader.Mods, because the caller check compares assemblies.
        static void Refresh()
        {
            if (!dirty) return;
            dirty = false;
            byKey.Clear();
            var owners = new Dictionary<string, Assembly>();
            foreach (var m in ModLoader.Mods)
                if (m != null && m.Manifest != null && m.Assembly != null && !string.IsNullOrEmpty(m.Manifest.ID)) owners[m.Manifest.ID] = m.Assembly;
            foreach (var section in GameOptionRegistry.Sections)
                foreach (var e in section.entries)
                {
                    Assembly owner;
                    if (e == null || e.kind != GameOptionRegistry.Kind.KeyBind || e.getKey == null || string.IsNullOrEmpty(e.source)) continue;
                    if (!owners.TryGetValue(e.source, out owner) || owner == self) continue;
                    KeyCode key;
                    try { key = e.getKey(); } catch (Exception) { continue; }
                    if (key == KeyCode.None) continue;
                    List<Binding> list;
                    if (!byKey.TryGetValue(key, out list)) byKey[key] = list = new List<Binding>();
                    list.Add(new Binding { OptionId = e.id, Owner = owner, Key = key, Mods = SavedMods(e.id) });
                }
        }

        // Runs after every Input.GetKeyDown / GetKey that returned true. Only reads by a mod that owns a binding on this
        // key are changed: they become false unless the press belongs to one of that mod's bindings on the key. Other
        // callers (the game, mods without a binding on the key) keep the real answer. Consume.Raw skips this mod's
        // own reads.
        static void ReadPostfix(KeyCode key, ref bool __result)
        {
            if (!__result || checking || Consume.Raw) return;
            checking = true;
            try
            {
                Refresh();
                List<Binding> list;
                if (!byKey.TryGetValue(key, out list)) return;
                var caller = Caller();
                if (caller == null) return;
                bool owns = false;
                foreach (var b in list)
                {
                    if (b.Owner != caller) continue;
                    if (!owns && seen.Add(caller)) Debug.Log(BetterKeyBindingsMod.Tag + "checking key combinations for " + caller.GetName().Name);
                    owns = true;
                    if (MayTrigger(b)) return;
                }
                if (owns) __result = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning(BetterKeyBindingsMod.Tag + "key check for another mod failed: " + e.Message);
            }
            finally { checking = false; }
        }

        static bool MayTrigger(Binding b) { return Consume.MayTrigger(b.Key, b.Mods); }

        // The first assembly up the stack that is not this mod, Harmony or the engine: whoever asked for the key.
        static Assembly Caller()
        {
            // Skips 2 frames: this method and the postfix. The stack is walked only for a true read of a key that
            // another mod binds, which is rare enough for the cost.
            var frames = new StackTrace(2, false).GetFrames();
            if (frames == null) return null;
            foreach (var f in frames)
            {
                var m = f.GetMethod();
                var t = m == null ? null : m.DeclaringType;
                if (t == null) continue;
                var a = t.Assembly;
                if (a == self || a == typeof(Harmony).Assembly || a == typeof(Input).Assembly) continue;
                return a;
            }
            return null;
        }
    }
}
