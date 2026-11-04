using System;
using HarmonyLib;
using UnityEngine;

namespace BetterKeyBindings
{
    // Key combinations for the game's key bindings.
    //
    // Every binding in the options menu takes Ctrl, Shift and Alt combinations, the middle mouse button, mouse
    // buttons 4 and 5, and the mouse wheel. While a binding waits for a key, nothing else gets the
    // input (see Consume). A combination never also triggers a binding on its main key alone (see Bindings).
    // The keys the game hard-codes while dragging and placing items become bindings of their own. A combination
    // already in use asks whether to unbind the other binding (see Options). Bindings that other mods add to the
    // options take combinations too (see ModKeys).
    //
    // This is the entry point the game loads (IMod, named in manifest.xml). The other files are static classes that
    // this one hooks up: Bindings and Consume patch what the game reads, Options patches the options window, ModKeys
    // covers other mods; Combo, Rule, Capture, Gate and Notches are the shared vocabulary and the decisions.
    public sealed class BetterKeyBindingsMod : IMod
    {
        public const string ModId = "better_key_bindings";
        internal const string Tag = "[BetterKeyBindings] ";
        const string HarmonyId = "mod." + ModId;

        static string modId = ModId;
        static Harmony harmony;

        // The game passes the manifest first; the ID it gives is used for localization and option ids, in case the
        // folder was installed under another ID than ModId.
        public void Init(ModManifest manifest)
        {
            if (manifest != null && !string.IsNullOrEmpty(manifest.ID)) modId = manifest.ID;
        }

        // Order matters: the stored modifiers are loaded before any patch can ask for them, and the options are
        // registered only after the patches are in. A failed patch leaves the game unpatched (UnpatchAll) so the mod
        // never runs half-installed. Patching happens once; later enables only register again.
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
                    Consume.Patch(harmony);
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

        // Removes every patch and clears the per-session state, so a mod toggled off leaves the keys as the game has them.
        public void OnDisable()
        {
            if (harmony != null) { harmony.UnpatchAll(HarmonyId); harmony = null; }
            Options.Reset();
            Consume.Reset();
        }

        internal static string L(string key, params object[] args)
        {
            return ModHelper.GetLocalized(modId, key, args);
        }
    }
}
