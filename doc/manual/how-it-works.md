# How it works

The game stores one key per binding. The mod keeps the modifiers of each binding next to it, and changes the places where the game reads a binding.

## Storing a binding

The key stays in the game's own key binding store, so the game saves it as before. The modifiers go to separate player preferences, one per binding. The three dragging keys are stored by the mod, key and modifiers together.

Modifiers do not care about the side: Ctrl means either Ctrl key, or Cmd on a Mac keyboard. Shift and Alt work the same way.

## Reading a binding

Bindings are read in four ways, and each one is covered:

- **Pressed or held checks** (toolbox slots, the bell, trash, options, control hints, quick move): the mod answers these checks itself, with the modifiers taken into account.
- **Item and dialogue handlers** compare the pressed keys with the binding's key. While such a handler handles a key press, a binding that may not fire reads as unbound, so two bindings on one key stay apart. Key releases keep the plain key, so letting go of a modifier first never leaves an action stuck.
- **Other mods' bindings**: those mods read their key straight from the engine. When such a read finds the key pressed, the mod checks who is asking: a read by the mod that owns the binding counts only if the binding may fire. The game's own direct key reads are left alone.
- **Dragging keys** that the game checks directly (Left Alt and Left Shift when a drag starts, Shift for precise placement) are redirected to the mod's own bindings.

A binding may fire when its key is pressed, its modifiers are held, and no other binding on the same key asks for more of the held modifiers. The rule lives in `src/Rule.cs`, and `tests/RuleTest.cs` checks it.

## The options window

While a binding waits for a key, the mod takes over the game's key capture. Nothing is taken while keys are down: the button shows the combination so far, and the binding is decided once every key pressed since the capture began is released. The last key or mouse button that is not a modifier becomes the main key, with the modifiers held when it went down. When only modifiers were pressed, the last of them becomes the main key and the others its modifiers.

Conflicts are checked against every key binding in the window, including the ones not yet applied. The dragging keys only conflict with each other, because they are held during a drag while the other bindings are pressed.

## Interface

Everything the mod shows comes from the game's own UI assets. The option rows are the game's key binding entries. The unbind prompt is built with the game's own window builder, which creates the game's window, label and button prefabs, in the same style as the options window. The mod draws nothing itself, so a texture pack that restyles those game assets restyles the mod too.

## Tests

- `tests/RuleTest.cs` runs the firing rule on combinations such as E against Ctrl + E.
- `tests/ScanTest.cs` runs the game's own mod scanner on the built DLL. The result is no capabilities.
