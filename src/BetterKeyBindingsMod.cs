using System;
using HarmonyLib;
using UnityEngine;

namespace BetterKeyBindings
{
    // Key combinations for the game's key bindings.
    //
    // Every binding in the options menu takes Ctrl, Shift and Alt combinations, and the middle mouse button and
    // mouse buttons 4 and 5. A combination never also fires a binding on its main key alone (see Bindings).
    // The keys the game hard-codes while dragging and placing items become bindings of their own. A combination
    // already in use asks whether to unbind the other binding (see Options). Bindings that other mods add to the
    // options take combinations too (see ModKeys).
    public sealed class BetterKeyBindingsMod : IMod
    {
        public const string ModId = "better_key_bindings";
        internal const string Tag = "[BetterKeyBindings] ";
        const string HarmonyId = "mod." + ModId;

        static string modId = ModId;
        static Harmony harmony;

        public void Init(ModManifest manifest)
        {
            if (manifest != null && !string.IsNullOrEmpty(manifest.ID)) modId = manifest.ID;
        }

        public void OnEnable()
        {
            Combo.L = k => L(k);
            Bindings.Load();
            if (harmony == null)
            {
                harmony = new Harmony(HarmonyId);
                try
                {
                    Bindings.Patch(harmony);
                    Options.Patch(harmony, modId);
                    ModKeys.Patch(harmony);
                }
                catch (Exception e)
                {
                    Debug.LogError(Tag + "could not hook the game's key bindings, the mod stays off: " + e);
                    harmony.UnpatchAll(HarmonyId);
                    harmony = null;
                    return;
                }
            }
            Options.Register(k => L(k));
            ModKeys.MarkDirty();
        }

        public void OnDisable()
        {
            if (harmony != null) { harmony.UnpatchAll(HarmonyId); harmony = null; }
            Options.Reset();
        }

        internal static string L(string key, params object[] args)
        {
            return ModHelper.GetLocalized(modId, key, args);
        }
    }
}
