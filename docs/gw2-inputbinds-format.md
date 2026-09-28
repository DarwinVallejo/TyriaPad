# GW2 InputBinds XML format

What can be worked out from [samples/InputBinds-blaggletoad.xml](../samples/InputBinds-blaggletoad.xml), the keybinds file for the reference layout. It's the basis of the keybinds reader ([src/TyriaPad.Core/Keybinds/](../src/TyriaPad.Core/Keybinds/)).

## Structure

```xml
<InputBindings>
  <action name="..." id="N" device="Keyboard" button="K" mod="M"
          device2="Keyboard" button2="K2" mod2="M2"/>
</InputBindings>
```

- `id`: stable identifier of the action. **This is what's used, not `name`**, because `name` can change with the client's language.
- `button` / `mod`: primary bind. `button2` / `mod2`: secondary bind (optional).
- The file **only contains the actions that were changed**. The ones that don't appear (skills 1–0, F1–F5, dodge, etc.) use GW2's default key, so TyriaPad needs a **table of default binds**.

## Modifiers (`mod`) — bit mask

| Bit | Key | Evidence |
|---|---|---|
| 1 | Shift | Mounts `Shift+R/G/C…`, Skiff `Shift+F` |
| 2 | Ctrl | About Face `Ctrl+L` |
| 4 | Alt | Inferred: PvP Panel `mod="6"` = Ctrl+Alt+F |

## Key codes (`button`)

**Letters and digits = their ASCII code** (same as the Windows virtual-key):

| Code | Key | Action |
|---|---|---|
| 48, 55–57 | `0`, `7`–`9` | Conjured Doorway, Jade Bot, Scan Rift, Skyscale Leap |
| 66–90 | `B`…`Z` | Mounts, Mail (`Z`=90), secondary Fishing (`E`=69) |

**Special keys = GW2's own table** (doesn't match VK). They follow a nearly alphabetical order of the English names. Codes marked ✔ are confirmed by the reference XML; the rest follow from that same order and are in [Gw2KeyCodes.cs](../src/TyriaPad.Core/Keybinds/Gw2KeyCodes.cs):

| Code | Key | | Code | Key |
|---|---|---|---|---|
| 0 | Alt | | 17 | `` ` `` (left of 1) |
| 1 | Ctrl | | 18 | Backspace |
| 2 | Shift | | 19 ✔ | Delete (Build Template 2) |
| 3 | `'` | | 20 | Enter |
| 4 | `\` | | 21 | Space |
| 5 ✔ | Caps Lock (Start Fishing) | | 22 | Tab |
| 6 ✔ | `,` (Toggle Action Camera) | | 23 ✔ | End (Equipment Template 2) |
| 7 | `-` | | 24 ✔ | Home (Equipment Template 1) |
| 8 | `=` | | 25 ✔ | Insert (Build Template 1) |
| 9 | Esc | | 26 / 27 | Page Down / Page Up |
| 10 | `[` | | 28–31 | Arrows ↓ ← → ↑ |
| 11 | Num Lock | | 32–43 | F1–F12 |
| 12 ✔ | `.` (Walk) | | | |
| 13 | `]` | | | |
| 14 ✔ | `;` (Nearest Ally) | | | |
| 15 | `/` | | | |
| 16 | Print Screen (TyriaPad doesn't send it) | | | |

> To do: the numeric keypad and F13+ (the codes above 90). If an unknown code shows up, the bind is ignored and a warning goes to the log. To decode it, bind that key in GW2, save the binds and compare the XML.

## Actions (`id`)

The id table TyriaPad uses is in [Gw2Actions.cs](../src/TyriaPad.Core/Keybinds/Gw2Actions.cs). The ones that matter for the glyphs are 18–22 (weapons 1–5), 23 (healing), 24–26 (utility), 27 (elite), 28–34 (profession F1–F7) and 78 (Action Camera). An id that isn't in the table is still read and is named by its `name`.

## Default values

The XML only has what was changed, so TyriaPad starts from the client's default keys (US keyboard) and applies the XML on top. When you bind a key, GW2 removes it from the action that had it. That's why a default that matches a key in the XML is discarded: with Mail on `Z`, "Stow/Draw Weapons" is left without a key. Actions whose default isn't clear are left without a key so as not to invent conflicts.

GW2 **doesn't write this XML on its own**: it applies changes immediately but saves them in `Local.dat`. The XML is created when you **export** the keybinds, and TyriaPad reads the most recently exported one in the folder (or the one set by `inputBinds` in `config.json`).

## Match with the reference layout

Everything in [reference-layout.md](reference-layout.md) §1 appears in the XML, with two differences:
- **Start Fishing** has two binds: Caps Lock (primary) and `Shift+E` (secondary). This covers both versions of the post.
- **Extras the post doesn't mention**: PvP Panel on `Ctrl+Alt+F` (probably to free up its default key) and Build/Equipment Templates on Insert/Delete and Home/End (codes 25/19 and 24/23). TyriaPad **doesn't assign them a button**; the reader accepts them without warnings.
