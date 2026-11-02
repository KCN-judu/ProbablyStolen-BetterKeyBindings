# Changelog

## 1.0.0 (pre-release)

First public release.

- Every key binding takes Ctrl, Shift and Alt combinations, and the middle mouse button and mouse buttons 4 and 5, including the bindings other mods add to the options.
- A binding is taken when every key is released, so holding a modifier on the way to a combination never binds the modifier alone.
- A combination never also triggers a binding on its key alone: the binding that asks for the most of the held modifiers wins.
- Choosing a combination that is already in use asks whether to unbind the other binding.
- A new binding for the key the game fixes for precise placement.
- Pick up only one and pick up half become bindings too, marked deprecated: nothing in the current game reaches them.
- All UI comes from the game's own assets, so texture packs restyle it.
- Text in all eleven languages of the game.
