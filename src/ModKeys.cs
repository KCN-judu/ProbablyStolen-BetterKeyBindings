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
    // reading assembly decides: a read by the mod that owns the binding counts only if the binding may fire (see
    // Rule), any other read is left alone. The game's own direct reads are never touched. Releases are not gated.
    public static class ModKeys
    {
        const string ModsPref = "BetterKeyBindings.optmods.";
        const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        sealed class Binding { public string OptionId; public Assembly Owner; public KeyCode Key; public int Mods; }

        static readonly Dictionary<KeyCode, List<Binding>> byKey = new Dictionary<KeyCode, List<Binding>>();
        static bool dirty = true, checking;
        static Assembly self;
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

        // Modifiers of the other mods' bindings on a key, as competitors of the game's bindings.
        public static void AddCompetitors(KeyCode key, List<int> into)
        {
            Refresh();
            List<Binding> list;
            if (byKey.TryGetValue(key, out list)) foreach (var b in list) into.Add(b.Mods);
        }

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

        static void ReadPostfix(KeyCode key, ref bool __result)
        {
            if (!__result || checking) return;
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
                    if (MayFire(b)) return;
                }
                if (owns) __result = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning(BetterKeyBindingsMod.Tag + "key check for another mod failed: " + e.Message);
            }
            finally { checking = false; }
        }

        static bool MayFire(Binding b)
        {
            int held = Combo.Held(b.Key);
            var others = new List<int>();
            foreach (var o in byKey[b.Key]) if (o != b) others.Add(o.Mods);
            Bindings.AddCompetitors(b.Key, others);
            return Rule.MayFire(b.Mods, held, others.ToArray());
        }

        // The first assembly up the stack that is not this mod, Harmony or the engine: whoever asked for the key.
        static Assembly Caller()
        {
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
