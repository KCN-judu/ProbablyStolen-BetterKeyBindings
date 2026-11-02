using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BetterKeyBindings
{
    // Key capture in the game's options window.
    //
    // While a binding waits for a key, nothing is taken until every key pressed is released; the button shows the
    // combination meanwhile. Ctrl, Shift and Alt held when the main key went down become part of the binding, and
    // modifiers pressed with no other key make the last of them the binding itself. The middle mouse button and
    // mouse buttons 4 and 5 count as keys too.
    // A combination already used by another binding asks whether to unbind that one. Everything stays pending, like
    // the game's own key choices, until Apply.
    // Bindings added by other mods take combinations as well: their modifiers are kept and checked by ModKeys.
    //
    // All UI the mod shows comes from the game's own assets: the option rows are the game's key binding entries, and
    // the unbind prompt is built with the game's CustomUI builder, which instantiates the game's window, label and
    // button prefabs for the layer. Nothing is drawn by the mod itself, so a texture pack that restyles those prefabs
    // restyles the mod too. Keep it that way: no GameObjects, images, colours or fonts of the mod's own.
    public static class Options
    {
        const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const string ConflictWindow = "better_key_bindings_conflict";
        const string PressGroup = "press";

        static FieldInfo pendingKeysField, windowField, consumedEscapeField;
        static MethodInfo endCapture, setStatus, refreshRules, refreshButton;

        // Modifiers chosen in the open window, by option id. Saved on Apply.
        static readonly Dictionary<string, int> pendingMods = new Dictionary<string, int>();

        // The capture in progress (see Capture), the option it is for, and the text last shown on its button.
        static string capturing;
        static readonly Capture capture = new Capture();
        static int lastPollFrame = -1;
        static string shownText;
        static KeyCode[] keys;

        sealed class Conflict { public string Id, Owner; public KeyCode Key; public int Mods; }
        static Conflict conflict;

        static string modId;
        static readonly Dictionary<string, Bindings.Hold> holdsByOption = new Dictionary<string, Bindings.Hold>();
        static bool registered;

        // ---------------------------------------------------------------- setup

        public static void Patch(Harmony harmony, string id)
        {
            modId = id;
            // Types are named at each call, as in Bindings.Patch.
            pendingKeysField = typeof(GameOptionUIManager).GetField("pendingKeys", AnyInstance);
            windowField = typeof(GameOptionUIManager).GetField("window", AnyInstance);
            consumedEscapeField = typeof(GameOptionUIManager).GetField("consumedEscapeFrame", AnyStatic);
            endCapture = typeof(GameOptionUIManager).GetMethod("EndCapture", AnyInstance);
            setStatus = typeof(GameOptionUIManager).GetMethod("SetKeyBindStatus", AnyInstance);
            refreshRules = typeof(GameOptionUIManager).GetMethod("RefreshRules", AnyInstance);
            refreshButton = typeof(GameOptionUIManager).GetMethod("RefreshKeyBindButton", AnyInstance);

            harmony.Patch(typeof(GameOptionUIManager).GetMethod("PollKeyCapture", AnyInstance), Hook("PollKeyCapturePrefix"));
            harmony.Patch(typeof(GameOptionUIManager).GetMethod("StartCapture", AnyInstance), Hook("StartCapturePrefix"));
            harmony.Patch(typeof(GameOptionUIManager).GetMethod("RefreshKeyBindButton", AnyInstance), null, Hook("RefreshKeyBindButtonPostfix"));
            harmony.Patch(typeof(GameOptionUIManager).GetMethod("OpenUI", AnyInstance), null, Hook("OpenUIPostfix"));
            harmony.Patch(typeof(GameOptionUIManager).GetMethod("OnWindowClosed", AnyInstance), null, Hook("OnWindowClosedPostfix"));
            harmony.Patch(typeof(GameOptionUIManager).GetMethod("OnApplySetting", AnyInstance), Hook("OnApplySettingPrefix"), Hook("OnApplySettingPostfix"));
        }

        static HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(typeof(Options).GetMethod(name, AnyStatic));
        }

        // The keys the game checks while dragging or placing, as entries of this mod's own sections: the ones in use,
        // then the deprecated ones under their own heading, each tooltip saying why.
        public static void Register(Func<string, string> L)
        {
            holdsByOption.Clear();
            foreach (var h in Bindings.Holds) holdsByOption[ModHelper.GameOptionId(modId, h.Id)] = h;
            if (registered) return;
            registered = true;
            try
            {
                ModHelper.AddGameOptionSection(modId, "hold", () => L("section_hold"));
                ModHelper.AddGameOptionSection(modId, "deprecated", () => L("section_deprecated"));
                foreach (var deprecated in new[] { false, true })
                    foreach (var hold in Bindings.Holds)
                    {
                        if (hold.Deprecated != deprecated) continue;
                        var h = hold;
                        Func<string> tip = h.Deprecated
                            ? (Func<string>)(() => L("deprecated_note") + "\n" + L("opt_" + h.Id + "_tip"))
                            : () => L("opt_" + h.Id + "_tip");
                        ModHelper.AddGameOptionKeyBind(modId, h.Deprecated ? "deprecated" : "hold", h.Id, () => L("opt_" + h.Id),
                            () => h.Key, k => Bindings.SetHold(h, k, h.Mods), tip);
                    }
            }
            catch (Exception e) { Debug.LogWarning(BetterKeyBindingsMod.Tag + "hold keys could not be added to the options: " + e.Message); }
        }

        public static void Reset()
        {
            CloseConflict();
            pendingMods.Clear();
            capturing = null;
            ResetCapture();
        }

        // ---------------------------------------------------------------- what an option is

        static string BindingOf(string optionId)
        {
            if (optionId == null || !optionId.StartsWith("key_", StringComparison.Ordinal)) return null;
            string id = optionId.Substring(4);
            return KeyBindHelper.Find(id) != null ? id : null;
        }

        static Bindings.Hold HoldOf(string optionId)
        {
            Bindings.Hold h;
            return optionId != null && holdsByOption.TryGetValue(optionId, out h) ? h : null;
        }

        // Options whose modifiers this mod reads at play time: the game's, this mod's own, and other mods' (ModKeys).
        static bool TakesCombos(string optionId)
        {
            return BindingOf(optionId) != null || HoldOf(optionId) != null || ModKeys.IsModOption(optionId, modId);
        }

        // Bindings only clash within a group: dragging keys are held while dragging, the rest are pressed.
        static string GroupOf(string optionId)
        {
            var h = HoldOf(optionId);
            return h != null ? h.Group : PressGroup;
        }

        static int SavedMods(string optionId)
        {
            string b = BindingOf(optionId);
            if (b != null) return Bindings.ModsOf(b);
            var h = HoldOf(optionId);
            if (h != null) return h.Mods;
            return ModKeys.IsModOption(optionId, modId) ? ModKeys.SavedMods(optionId) : 0;
        }

        static int PendingMods(string optionId)
        {
            int m;
            return pendingMods.TryGetValue(optionId, out m) ? m : SavedMods(optionId);
        }

        static Dictionary<string, KeyCode> PendingKeys(GameOptionUIManager ui)
        {
            return pendingKeysField == null ? null : pendingKeysField.GetValue(ui) as Dictionary<string, KeyCode>;
        }

        static KeyCode[] CaptureKeys()
        {
            if (keys != null) return keys;
            var list = Enum.GetValues(typeof(KeyCode)).Cast<KeyCode>()
                .Where(k => k != KeyCode.None && k != KeyCode.Escape && (int)k < (int)KeyCode.Mouse0)
                .Concat(Combo.MouseKeys).Distinct().ToList();
            // Modifiers last, so a key pressed in the same frame as its modifier is the main key.
            keys = list.Where(k => !Combo.IsModifier(k)).Concat(list.Where(Combo.IsModifier)).ToArray();
            return keys;
        }

        static void Call(MethodInfo m, GameOptionUIManager ui, params object[] args)
        {
            if (m != null && ui != null) m.Invoke(ui, args);
        }

        // ---------------------------------------------------------------- capture

        static bool PollKeyCapturePrefix(GameOptionUIManager __instance)
        {
            if (!KeyBindHelper.IsCapturing || !__instance.IsOpen) return true;
            int frame = Time.frameCount;
            if (frame == lastPollFrame) return false;
            lastPollFrame = frame;
            try { Poll(__instance); }
            catch (Exception e)
            {
                Debug.LogWarning(BetterKeyBindingsMod.Tag + "key capture failed: " + e);
                Call(setStatus, __instance, "");
                Call(endCapture, __instance);
            }
            return false;
        }

        static void Poll(GameOptionUIManager ui)
        {
            string id = KeyBindHelper.CapturingOptionId;
            if (id != capturing) { capturing = id; ResetCapture(); }
            bool combos = TakesCombos(id);

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (consumedEscapeField != null) consumedEscapeField.SetValue(null, Time.frameCount);
                Call(setStatus, ui, "");
                Call(endCapture, ui);
                return;
            }

            // Nothing is decided while keys are down: the button shows the combination so far, and the binding is
            // taken once every key pressed since the capture began is released.
            foreach (var k in CaptureKeys())
                if (Input.GetKeyDown(k)) capture.Down((int)k, Combo.GroupOf(k), Combo.Held(k));
            if (!capture.Started) return;

            bool anyDown = false;
            foreach (var k in capture.Pressed) if (Input.GetKey((KeyCode)k)) { anyDown = true; break; }
            int key, mods;
            if (!anyDown && capture.Decide(out key, out mods))
            {
                Finish(ui, id, (KeyCode)key, combos ? mods : 0);
                return;
            }

            var main = (KeyCode)capture.Main;
            string text = main != KeyCode.None ? Combo.Name(main, combos ? capture.MainMods : 0)
                : Combo.ModsPrefix(capture.PressedModifiers) + "...";
            if (text != shownText)
            {
                shownText = text;
                var window = windowField == null ? null : windowField.GetValue(ui) as CustomUIWindow;
                var button = window == null ? null : window.Get(id);
                if (button != null) button.SetText(text);
            }
        }

        static void ResetCapture()
        {
            capture.Reset();
            shownText = null;
        }

        static void Finish(GameOptionUIManager ui, string id, KeyCode key, int mods)
        {
            capturing = null;
            ResetCapture();
            if (KeyBindHelper.IsReserved(key))
            {
                Call(setStatus, ui, LocHelper.GetLocalizedMainMenu("option_keybind_status_reserved", new object[] { KeyBindHelper.KeyName(key) }));
                return;
            }
            var pending = PendingKeys(ui);
            if (pending == null) { Call(setStatus, ui, ""); Call(endCapture, ui); return; }

            string owner = FindOwner(pending, id, key, mods);
            if (owner != null)
            {
                Call(setStatus, ui, "");
                Call(endCapture, ui);
                AskUnbind(ui, new Conflict { Id = id, Owner = owner, Key = key, Mods = mods });
                return;
            }
            Assign(pending, id, key, mods);
            Call(setStatus, ui, "");
            Call(endCapture, ui);
            Call(refreshRules, ui);
        }

        static void Assign(Dictionary<string, KeyCode> pending, string id, KeyCode key, int mods)
        {
            pending[id] = key;
            if (TakesCombos(id)) pendingMods[id] = key == KeyCode.None ? 0 : mods;
        }

        // Another option, in the same group, that the new choice would collide with: the same combination, or for
        // two options of the same other mod the same key, since that mod reads only the key and could not tell them
        // apart.
        static string FindOwner(Dictionary<string, KeyCode> pending, string id, KeyCode key, int mods)
        {
            string group = GroupOf(id);
            bool combos = TakesCombos(id);
            foreach (var kv in pending)
            {
                if (kv.Key == id || kv.Value != key) continue;
                if (Rule.Collides(GroupOf(kv.Key) == group, (int)key, mods, combos, (int)kv.Value, PendingMods(kv.Key),
                        TakesCombos(kv.Key), SameOtherMod(id, kv.Key)))
                    return kv.Key;
            }
            return null;
        }

        static bool SameOtherMod(string a, string b)
        {
            if (!ModKeys.IsModOption(a, modId) || !ModKeys.IsModOption(b, modId)) return false;
            return ModKeys.SourceOf(a) == ModKeys.SourceOf(b);
        }

        static string LabelOf(string optionId)
        {
            var e = GameOptionRegistry.Find(optionId);
            if (e == null || e.label == null) return optionId;
            try { return e.label(); } catch (Exception) { return optionId; }
        }

        // ---------------------------------------------------------------- unbind prompt

        static void AskUnbind(GameOptionUIManager ui, Conflict c)
        {
            CloseConflict();
            var manager = CustomUIManager.Instance;
            if (manager == null) return;
            conflict = c;
            string combo = Combo.Name(c.Key, TakesCombos(c.Id) ? c.Mods : 0);
            string body = BetterKeyBindingsMod.L("conflict_body", combo, LabelOf(c.Owner), LabelOf(c.Id));
            // The options window's own frame and layer, as the game's mod settings window uses them, so the prompt
            // looks like part of the menu. Buttons get the width of the options window's Apply button.
            string layer = StoreUIManager.Instance != null ? "meta" : "menu";
            var b = manager.CreateWindow(ConflictWindow, BetterKeyBindingsMod.L("conflict_title"), layer,
                    manager.optionUIPrefab, CustomUIManager.Style.Auto)
                .SetCloseOnEscape(true)
                .OnClose(OnConflictClosed)
                .AddLabel(body, "body")
                .AddFlexibleSpace()
                .BeginRow(16f, TextAnchor.MiddleCenter)
                .AddFlexibleSpace()
                .AddButton(BetterKeyBindingsMod.L("conflict_unbind"), OnUnbind, "unbind");
            b.Last.SetFixedWidth(180f);
            b.AddButton(BetterKeyBindingsMod.L("conflict_keep"), CloseConflict, "keep");
            b.Last.SetFixedWidth(180f);
            var window = b.AddFlexibleSpace().End().Show();
            // That prefab is a full-screen dimming layer holding a fixed-size panel, with the content inside the
            // panel. Sizing the window would only resize the dimming layer, so the panel holding the content is
            // sized instead: a dialog's size, centred over the dimmed options window.
            var panel = window != null && window.ContentRoot != null ? window.ContentRoot.parent as RectTransform : null;
            if (panel != null && panel != window.Rect) panel.sizeDelta = new Vector2(PromptWidth, PromptHeight);
        }

        const float PromptWidth = 640f, PromptHeight = 320f;

        static void OnUnbind()
        {
            var c = conflict;
            conflict = null;
            var ui = GameOptionUIManager.Instance;
            if (c != null && ui != null && ui.IsOpen)
            {
                var pending = PendingKeys(ui);
                if (pending != null)
                {
                    Assign(pending, c.Owner, KeyCode.None, 0);
                    Assign(pending, c.Id, c.Key, c.Mods);
                    Call(refreshButton, ui, c.Owner);
                    Call(refreshButton, ui, c.Id);
                    Call(refreshRules, ui);
                }
            }
            CloseConflict();
        }

        static void OnConflictClosed()
        {
            conflict = null;
            // Escape closed the prompt: it must not also close the options or open the pause menu.
            if (Input.GetKeyDown(KeyCode.Escape) && consumedEscapeField != null) consumedEscapeField.SetValue(null, Time.frameCount);
        }

        static void CloseConflict()
        {
            conflict = null;
            var manager = CustomUIManager.Instance;
            if (manager != null && manager.IsOpen(ConflictWindow)) manager.CloseWindow(ConflictWindow);
        }

        // ---------------------------------------------------------------- window life

        static void StartCapturePrefix() { CloseConflict(); }

        static void RefreshKeyBindButtonPostfix(GameOptionUIManager __instance, string optionId)
        {
            if (!__instance.IsOpen || optionId == KeyBindHelper.CapturingOptionId || !TakesCombos(optionId)) return;
            var pending = PendingKeys(__instance);
            KeyCode key;
            if (pending == null || !pending.TryGetValue(optionId, out key)) return;
            var window = windowField == null ? null : windowField.GetValue(__instance) as CustomUIWindow;
            var button = window == null ? null : window.Get(optionId);
            if (button != null) button.SetText(Combo.Name(key, PendingMods(optionId)));
        }

        static void OpenUIPostfix(GameOptionUIManager __instance)
        {
            pendingMods.Clear();
            var pending = PendingKeys(__instance);
            if (pending == null) return;
            foreach (var id in pending.Keys.ToList())
            {
                if (!TakesCombos(id)) continue;
                pendingMods[id] = SavedMods(id);
                Call(refreshButton, __instance, id);
            }
        }

        static void OnWindowClosedPostfix() { Reset(); }

        static void OnApplySettingPrefix(GameOptionUIManager __instance, out bool __state)
        {
            __state = __instance.IsOpen && EmporiumOption.Instance != null;
        }

        // The game has just stored the keys; store the modifiers that go with them.
        static void OnApplySettingPostfix(bool __state)
        {
            if (!__state) return;
            foreach (var kv in pendingMods)
            {
                string b = BindingOf(kv.Key);
                if (b != null) { Bindings.SetMods(b, KeyBindHelper.Get(b) == KeyCode.None ? 0 : kv.Value); continue; }
                var h = HoldOf(kv.Key);
                if (h != null) { Bindings.SetHold(h, h.Key, h.Key == KeyCode.None ? 0 : kv.Value); continue; }
                if (ModKeys.IsModOption(kv.Key, modId)) ModKeys.SetMods(kv.Key, kv.Value);
            }
            ModKeys.MarkDirty();
        }
    }
}
