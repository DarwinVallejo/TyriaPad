# Armoury Crate SE with TyriaPad

On the Ally X, Armoury Crate SE decides what Windows sees from the controller: an XInput controller (gamepad mode) or keyboard and mouse (desktop mode). TyriaPad reads that XInput controller, plus the M1/M2 buttons through the vendor HID collection, and it must be the only thing turning the controller into keys. If Armoury Crate SE (or Steam Input) also converts something, GW2 gets the input twice.

General rule: **in Armoury Crate SE, leave the controller as it comes from the factory, in gamepad mode**. All customization is done in the TyriaPad profile.

The `[ ]` checkboxes are things still to be tested on the Ally X; once tested, the result is noted here.

## Recommended setup

### Control mode: gamepad
- Command Center → **Control mode** → **Gamepad**. In desktop mode the sticks move the mouse and the buttons send keys: that mixes with what TyriaPad sends, and XInput no longer sees the controller.
- **Gamepad** has to be set by hand, not **auto** mode. Auto switches to desktop when it thinks no game is running, and it depends on Armoury Crate SE recognizing GW2.

### Per-game profile: not used
- [x] Tested on the Ally X: Armoury Crate SE never loads the GW2 per-game profile. So there's no conflict with TyriaPad, but it also can't be used to switch to gamepad mode when the game opens: the mode is set by hand (see above).
- If you ever change the resolution or the Windows scaling, you have to calibrate the overlay again (see [PLAN.md](../PLAN.md), phase 3).

### Button mapping: unchanged
- Controller customization → **default mapping** in gamepad mode: no swapped buttons, no turbo and no macros.
- TyriaPad draws the glyphs based on the XInput button it receives. If Armoury Crate SE swaps, say, A and B, the overlay would show the wrong button.
- Armoury Crate SE's macros and turbo send several presses for one. That goes against ArenaNet's policy (1 press = 1 action), even if TyriaPad isn't the one doing it.

### M1/M2: enabled and unassigned
- M1 and M2 **not disabled and unassigned**, as they come from the factory: TyriaPad reads them through the vendor HID collection (VID `0B05`, PID `1B4C`, page `FF31`, usage `76`), and they only send their code there while they have no assignment.
- With the factory assignment, both paddles send the same code (`5A A5`, then `5A 00`), so TyriaPad can't tell them apart and both do what M1 does.
- If you assign something to either of them (a controller button or a key), **both stop sending their code** to TyriaPad: tested on the Ally X by assigning F12 to M2, and M1 stopped responding. On top of that, the assignment goes straight to the system, without passing through TyriaPad.
- [x] Tested: there's no way to give M2 its own code without assigning it a key in Armoury Crate SE. Reading M1/M2 as keys was ruled out to avoid adding more setup: both paddles do the same thing.

### Sticks and triggers
- Stick deadzones and curves: factory defaults. TyriaPad applies its own (`input.leftStickDeadzone`, `input.rightStickDeadzone` in `config.json`), and if both add up the stick feels dead at the start of its travel.
- Trigger travel: factory default. TyriaPad's thresholds (`triggerPressThreshold`, `triggerReleaseThreshold`) assume the full travel.
- If you recalibrate the sticks in Armoury Crate SE, nothing needs to change in TyriaPad.

### Gyroscope: off
- In Armoury Crate SE, leave the gyroscope **unassigned** (neither to a stick nor to the mouse): otherwise it would move the camera or the cursor on its own, on top of what TyriaPad sends. TyriaPad doesn't use the gyroscope.

### Vibration
- Doesn't matter: TyriaPad doesn't use vibration.

## Command Center and Armoury Crate buttons
- They still open Command Center and Armoury Crate SE. TyriaPad ignores their codes (it only reads M1/M2).
- [x] Tested on the Ally X (desktop mode, opening and closing right away): the button sends `A6` through the vendor collection and TyriaPad ignores it. Windows brings Command Center to the foreground and TyriaPad pauses, but about 130 ms later GW2 is in the foreground again. If you leave the panel open and navigate it with the controller, GW2 may also get the input (not tested). If that happens, TyriaPad could pause when it receives `A6`.

## Other things that need to be set this way
- **Steam Input off** for GW2 (Steam → GW2 → Properties → Controller). Otherwise Steam also turns the controller into keys.
- **Windows desktop or Xbox full screen experience**: both work. In the Xbox full screen experience, Windows doesn't give XInput to background apps and TyriaPad reads the controller through Raw Input (tested on the Ally X: controller, keys to GW2 and overlay). There LT and RT share an axis, so the LT+RT layer doesn't activate; F5–F8 are still on LT + D-pad.
- **Windowed Fullscreen** in GW2, so the overlay can be seen.
