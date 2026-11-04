# How it works

The game stores one key per binding. The mod keeps the modifiers of each binding next to it, and changes the places where the game reads a binding.

## Storing a binding

The key stays in the game's own key binding store, so the game saves it as before. The modifiers go to separate player preferences, one per binding. The three dragging keys are stored by the mod, key and modifiers together.

The mouse wheel has no key code in the engine, so wheel up and wheel down get two numbers above all of the engine's keys. The game stores them like any other key.

Modifiers do not care about the side: Ctrl means either Ctrl key, or Cmd on a Mac keyboard. Shift and Alt work the same way.

## Reading a binding

Bindings are read in four ways, and each one is covered:

- **Pressed or held checks** (toolbox slots, the bell, trash, options, control hints, quick move): the mod answers these checks itself, with the modifiers taken into account.
- **Item and dialogue handlers** compare the pressed keys with the binding's key. While such a handler handles a key press, a binding that may not trigger reads as unbound, so two bindings on one key stay apart. Key releases keep the plain key, so letting go of a modifier first never leaves an action stuck.
- **Other mods' bindings**: those mods read their key straight from the engine. When such a read finds the key pressed, the mod checks who is asking: a read by the mod that owns the binding counts only if the binding may trigger. The game's own direct key reads are left alone.
- **Dragging keys** that the game checks directly (Left Alt and Left Shift when a drag starts, Shift for precise placement) are redirected to the mod's own bindings.

Whom a press belongs to is decided once, the first time it is seen going down, with the modifiers held then, among the bindings that apply where the player is: the bindings on that key asking for exactly those modifiers, else the ones asking for the most of them (several at once when they ask for as many), else nobody, and the press goes to the game untouched. Every read of a binding during the press only asks this answer, so the press stays with the same bindings until the key is up. Rotating, flipping and resetting an item only apply while an item is dragged, which the game shows in a public state; every other binding applies everywhere, since the game decides inside each action, from many private states, whether it does anything. Nothing is watched or handed back afterwards. The rule lives in `src/Rule.Pure.cs`, the record of each press in `src/Consume.cs`.

## Who gets an input

Every read of a bindable input asks one place, the mod's gate (`src/Consume.Pure.cs`), whether the reader gets it. Two things take an input away from everyone else:

- **A binding waiting for a key.** Every key, mouse button and wheel notch pressed while it waits goes only to the capture: nobody else sees it go down, held, or released, even when the release comes after the binding is taken. Keys already down when the capture began are left alone, so their release still reaches whoever saw them go down. The options window's keyboard navigation is off meanwhile, so Space or Enter does not press the selected button.
- **A binding owning a wheel notch.** The notch does not also scroll (see below).

The gate covers the engine's key reads by key code and by name, the game's own reads of the middle mouse button and mouse buttons 4 and 5, and the scroll.

## The mouse wheel

When anything asks the engine whether wheel up or wheel down is pressed, held or released, the mod answers. A notch is pressed for one frame and released in the next, so the game's bindings, its item handlers, the options window and other mods' bindings all see it as a key tapped once. Notches that arrive in the same frame are pressed one per frame, so each one triggers its binding. Fractions of a notch, as some touchpads send, add up until they make one.

A notch belongs to a binding by the same rule as any press. A notch that belongs to a binding never scrolls; a notch that belongs to nobody scrolls at once, untouched, as without the mod. While a binding waits for a key, every notch is dropped. The notches are counted in `src/Wheel.Pure.cs`, and kept from the scrolling by the gate.

## The options window

While a binding waits for a key, the mod takes over the game's key capture. Nothing is taken while keys are down: the button shows the combination so far, and the binding is decided once every key pressed since the capture began is released. The last key, mouse button or wheel notch that is not a modifier becomes the main key, with the modifiers held when it went down. When only modifiers were pressed, the last of them becomes the main key and the others its modifiers.

Conflicts are checked against every key binding in the window, including the ones not yet applied. The dragging keys only conflict with each other, because they are held during a drag while the other bindings are pressed.

## Interface

Everything the mod shows comes from the game's own UI assets. The option rows are the game's key binding entries. The unbind prompt is built with the game's own window builder, which creates the game's window, label and button prefabs, in the same style as the options window. The mod draws nothing itself, so a texture pack that restyles those game assets restyles the mod too.

## Tests

The mod's decisions are kept apart from the game in `src/*.Pure.cs`, which use nothing but plain C#: whom a press belongs to, what the pressed keys become when they are released, how wheel notches become presses, when a notch goes to a binding instead of scrolling, what a capture keeps from everyone else, when two bindings collide, and how a binding is stored. The code that talks to the game only reads keys and options and passes them on.

`tests/run.sh` runs the checks:

- `tests/PureTest.cs` runs those decisions headless, without the game or Unity: E against Ctrl + E, Shift held on the way to Shift + Space, Ctrl + Shift + A, a modifier on its own, Ctrl + Shift + E with Ctrl + E and Shift + E bound, a press no binding takes, Ctrl + wheel up, several notches in one frame, a key pressed while binding and released after it, collisions within one mod, and more.
- `tests/ScanTest.cs` runs the game's own mod scanner on the built DLL, when the game is installed. The result is no capabilities.
