# Changelog

## 1.1.0

- Wheel up and wheel down can be bound to any key binding, alone or with modifiers such as Ctrl + wheel up, including the bindings other mods add to the options. Each notch triggers the binding once.
- Whom a press belongs to is decided once, when the key goes down: the binding with exactly the held modifiers, else the ones with the most of them (all of them when several tie, so Ctrl + Shift + E runs both Ctrl + E and Shift + E), else nobody, and the press goes to the game untouched. The press stays with those bindings until the key is up, even if a modifier is let go first.
- A notch that goes to a binding does not also scroll. Rotating, flipping and resetting an item only take the wheel while you drag one, so elsewhere a notch bound to them still scrolls; where nothing is bound to the wheel it scrolls as before.
- While a binding waits for a key, the keys, mouse buttons and wheel you press go only to that binding: the game and other mods no longer react to them, Space or Enter no longer presses the selected button, and the wheel no longer scrolls the options.
- The documentation states that keys other mods read straight from the engine, without a key binding, are outside what this mod handles, and asks mod authors to offer key bindings instead.

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
