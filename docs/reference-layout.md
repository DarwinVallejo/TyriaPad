# Reference layout (Blaggletoad v5.0)

Source: r/Guildwars2 post `w4zlhs`, "Full controller support for Guild Wars 2" (author: Blaggletoad).
Steam Input config: `steam://controllerconfig/1284210/3595428420`. There's a newer version of the post at `1oipjgz`.

This is TyriaPad's **default profile**. Buttons use Xbox notation (Ally X), converted from PlayStation like this:
Square → **X**, Triangle → **Y**, Circle → **B**, Cross → **A**, Start → **Menu**, Select → **View**.

Legend: `tap` = short press, `hold` = hold (threshold ~250 ms), `2×` = double tap.

---

## 1. Keybinds to set in GW2

| GW2 action | Key | Required? |
|---|---|---|
| Toggle Action Camera | `,` | **Yes** |
| Strafe Left / Strafe Right | `A` / `D` | Recommended: by default `A`/`D` turn and strafing is on `Q`/`E` |
| Walk | `.` | |
| About Face | `Ctrl+L` | |
| Nearest Ally | `;` | |
| Start Fishing | `Shift+E` | |
| Summon Skiff | `Shift+F` | |
| Set Jade Bot Waypoint | `Shift+7` | |
| Scan for Rift | `Shift+8` | |
| Skyscale Leap | `Shift+9` | |
| Conjured Doorway | `Shift+0` | |
| Mail | `Z` | |
| Raptor / Springer / Skimmer / Jackal / Griffon | `Shift+R / G / C / V / N` | |
| Roller Beetle / Warclaw / Skyscale / Siege Turtle | `Shift+B / L / O / T` | |

> The post has two lists of optional keys. This uses the **newer** one (Shift+E, Shift+F, Shift+7…). The old one used Caps Lock, `[` and `]`.
> TyriaPad checks these keys by reading the InputBinds XML and warns about any that are missing (tray → "Check GW2 keybinds").

---

## 2. Combat context (Action Camera)

### Base layer
| Button | tap | hold | 2× |
|---|---|---|---|
| Left stick | Movement (WASD) | | |
| Right stick | Camera (relative mouse) | | |
| RB | Weapon Skill 1 | | |
| RT | Weapon Skill 2 | | |
| L3 | Skill 6 (healing) | Mount radial (pick with right stick) | |
| X | Special Action Key | | |
| Y | Interact | Mastery radial (pick with left stick) | |
| A | Jump | | |
| B | Dodge | | |
| R3 | Next enemy (`Tab`) | Call target (`Ctrl+T`) | Take target (`T`) |
| D-pad ↑ | Map | Take out fishing rod | |
| D-pad → | Inventory | Summon Skiff | |
| D-pad ↓ | Hero Panel | Jade Bot Waypoint | |
| D-pad ← | Social menu | Windows radial (pick with right stick) | |
| Menu | Main menu / close (Esc) | **Toggle Action Camera ↔ cursor** | |
| View | Map | Toggle walk/run | |

### Hold LB
| Button | Action |
|---|---|
| RB | Weapon Skill 3 |
| RT | Weapon Skill 4 |
| LT | Weapon Skill 5 |
| X / Y / B / A | Skill 7 / 8 / 9 / 0 (elite) |
| L3 | Swap weapons |
| R3 | About Face |
| Menu / View | Zoom in / out |

### Hold LT
| Button | Action |
|---|---|
| X / Y / B / A | Profession F1 / F2 / F3 / F4 |

### Hold LT + RT
| Button | Action |
|---|---|
| X / Y / B / A | Profession F5 / F6 / F7 / F8 |

> **Confirmed with the VDF:** these are the face buttons. In the LT layer, X/Y/B/A have a *chord* with RT that sends F5–F8.
> The VDF also adds the D-pad inside the LT layer: ↑ F5, → F6, ↓ F7, ← F5 (the last one looks like the author's mistake; it should be F8).

---

## 3. Cursor / menus context (map, inventory, etc.)

| Button | Action |
|---|---|
| Left and right stick | Move the cursor |
| RB, A | Left click |
| LB | Right click |
| LT / RT | Wheel up/down (scroll; zoom on the map) |
| B | Close menu (Esc) |
| Y | Place a waypoint on the map |
| hold RB + right stick | Drag the map (left click held) |
| hold LB + RB | Ping on the map |
| hold LB + R3 | Draw on the map |
| hold D-pad ← | Windows radial |

---

## 4. Mount context

| Button | Action |
|---|---|
| RB | Mount attack / dismount |
| X | Descend / dive / drift |
| A | Jump / blink / dash |
| L3 | Bond of Life |
| LB + X | Bond of Vigor |
| LB + Y | Bond of Faith |
| hold L3 + right stick | Mount radial |
| hold L3 + RB/LB | Default mount |
| Xbox button (tap / hold) | Mount / mount radial. The Ally X has no standard Guide button: **it's remapped to M1/M2 in phase 5** |

---

## 5. Fishing and Skiff

The fishing and Skiff skills **take weapon slots 1–5**, so the combat layer already covers them (RB casts, RT = 2, LB+RB = 3, LB+RT = 4…). No separate context is needed. There's just one detail: "left stick left/right to catch the fish" already works because the left stick sends A/D.

---

## 6. What this layout requires from TyriaPad's engine

1. **tap / hold / double tap** on the same button, with configurable thresholds. The tap fires on release if it was shorter than the hold threshold.
2. **Buttons that are a modifier in one layer and an action in another**: RT is skill 2 in the base layer and a modifier under LT (LT+RT). LB and LT on their own are pure modifiers.
3. **Contexts that switch on their own**:
   - mount → MumbleLink `mountIndex` ≠ 0
   - cursor → when the user toggles it, or automatically if MumbleLink says the map is open
   - combat → default
4. **Built-in radial menus** drawn in the overlay (windows, masteries, mounts). Picking an option sends **a single key**, so there's no need for GW2Radial or Blish HUD and 1 press = 1 action is respected.
5. **Mouse output**: relative movement (camera), absolute or accelerated (cursor), clicks, held click (drag) and wheel.
6. **Overlay**: glyphs on skills 1–0 and on the profession bar F1–F5 (above the main bar) with the exact combo (e.g. `LB`+`Y`).

---

## 7. What the actual VDF says

File: [samples/steam-blaggletoad.vdf](../samples/steam-blaggletoad.vdf). It's the Steam config as applied and exported; the SteamID and the local path were removed.
Steam names: `button_escape` = Menu, `button_menu` = View.

### Steam layers and their equivalent
| # | Steam layer | Activated by | In TyriaPad |
|---|---|---|---|
| 1 | Default | — | Combat context, base layer |
| 2 | Cursor Mode (action set) | — | Cursor context |
| 3 | Spell Slot Modifier | hold LB | LB layer |
| 4 | Spell Slot Modifier 2 | hold LT | LT layer (+ RT as a *chord*) |
| 5 | Radial | hold D-pad ← | Windows radial |
| 6 | Cursor Radial | hold D-pad ← in cursor mode | Windows radial |
| 7 | Mount Radial | hold L3 | Mount radial |
| 8 | Cursor Mode Movement | hold LB in cursor mode | Move with WASD in cursor mode |
| 9 | Mastery Skills Radial | hold Y | Mastery radial |

### Differences from the post's transcription
| Button | Post (sections 2–3) | VDF | Decision |
|---|---|---|---|
| R3 tap | Nearest Enemy | `Tab` (next enemy) | `Tab` is used (fixed in phase 1) |
| RB tap / hold | Weapon Skill 1 | tap = left click, hold = `1` | Kept as `1`: the click aims with the reticle and does the same thing |
| X | Special Action Key | `C` + `N` at the same time | Only `N`. `C` (mount skill 2) goes to the mount context (phase 2) to respect 1 press = 1 action |
| Y | Interact | `F` on press and **right click on release** | Only `F`; it's unclear what the click is for |
| D-pad ↑ | Map | `N` (labeled "Map") | `M` is used, which is the default map key and the one the VDF itself uses in cursor mode |
| D-pad ← tap | Social menu | `K` | Kept as `Y` (Contacts) until we know what `K` is in their keybinds |
| D-pad ↑ / → / ↓ hold | Fishing rod / Skiff / Jade Bot | `Caps Lock` (old key) / nothing / nothing | Removed from the D-pad: all three are in the mastery radial. The D-pad responds instantly |
| View hold | Toggle walk | `.` (labeled "Slow Skiff") | Same: `.` = Walk |
| Menu / View in LT layer | — | Build/Equipment Template 2 (`Delete`+`End`) and 1 (`Insert`+`Home`) | **Left out**: they send two keys per press (rule 3). They can be added by hand with a single key |
| R3 in LT layer | — | `;` Nearest Ally | Added |
| LB in LT layer | — | Weapon Skill 5 | Added (LB→LT and LT→LB both give 5) |
| Menu / View in LB layer | Zoom in / out | — | `wheel:up` / `wheel:down` with repeat; doesn't depend on keybinds |

### Cursor mode in the VDF
| Button | Action |
|---|---|
| Left / right stick | Mouse (stick click = left / right click) |
| RB, A | Left click |
| LB | tap: right click; hold: movement layer with WASD (+ `Shift`) |
| X | tap: right click; hold: `Enter` |
| B | Esc |
| Y | `Alt` held (markers/pings on the map) and left click on release |
| LT / RT | Wheel down / up, repeating every 200 ms |
| Menu hold | `,` and back to the combat context |

> The VDF has no binding that enters Cursor Mode from Default (it only exits). TyriaPad solves this with hold Menu in both directions, and it also follows the Windows cursor.

How it ends up in [profiles/blaggletoad.json](../profiles/blaggletoad.json) (`pointer` context):
- Both sticks move the cursor; L3 / R3 = left / right click.
- `LB` tap = right click; holding LB opens the `LB` layer: the left stick moves the character (WASD), RB = ping (`Shift`+click) and R3 = draw on the map (`Shift`+click held while the right stick moves).
- `Y` = personal marker on the map (`Alt`+click). `X` tap = right click, hold = `Enter`. `LT` / `RT` = wheel down / up with repeat.
- It's unknown what the `J` in the VDF's movement layer is in the author's keybinds, so it isn't copied.

### Mount context in the profile
Inherits from combat and only changes `X` → `C` (mount skill 2: descend, dive, drift). The rest (RB = 1 = attack, A = Space = jump, L3 = 6, LB+X / LB+Y = 7 / 8) already matches the combat keys.

### Radials
- **Windows:** Hero `H`, Mail `Z`, Guild `G`, Social `Y`, Trading Post `O`, `K` ("Pets").
- **Mounts:** `Shift` + `R` raptor, `O` skyscale, `G` springer, `C` skimmer, `V` jackal, `B` roller beetle, `T` siege turtle, `N` griffon, `L` warclaw. Matches section 1.
- **Masteries:** `Shift` + `9` leap, `0` doorway, `8` rift scan, `7` Jade Bot, `E` fishing, `F` skiff. Matches section 1.
- **Mount:** `X` (hold: `Shift+X`); `'` opens GW2Radial, which TyriaPad doesn't need.
