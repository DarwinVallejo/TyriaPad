# TyriaPad — Project plan

External program for playing Guild Wars 2 with a controller on the ROG Ally X. It shows a ConsolePort-style overlay with the controller glyphs on top of the skill bar.

> Name: **TyriaPad**. "GW2" / "Guild Wars" are kept out of the name because they are ArenaNet trademarks. The repository will be public on GitHub, under the MIT license and with the "not affiliated with ArenaNet/NCSOFT" notice.

---

## 1. Decisions already made

| Topic | Decision | Reason |
|---|---|---|
| Approach | Standalone external app, **not** a Blish HUD module or a DX11 addon | GW2 has no addon API. A separate app can control the controller, keys and overlay together |
| Language / UI | **C# + .NET 10 (LTS) + WPF** | Mature UI for a transparent overlay and a settings window. Performance is more than enough |
| Controller input | **XInput via P/Invoke** (later HID for M1/M2) | The Ally X shows up as an Xbox controller; no dependencies |
| Output | **SendInput with scancodes** | GW2 responds better to scancodes than to virtual keys |
| Steam Input | **Disabled for GW2**; the app does the remapping | Avoids the problem of not being able to detect Steam's active layer |
| Philosophy | **Non-invasive**: no drivers, no virtual controller (ViGEm), no hiding the real one (HidHide), no services, no registry | What went wrong with Handheld Companion, which is **not used** alongside TyriaPad |
| Distribution | Single portable executable (`PublishSingleFile`, self-contained) + `config.json` next to it | Deleting the folder = uninstalling |

### Design rules (non-negotiable)
1. **Only reads** the controller; never hides or virtualizes it.
2. **Only sends input when GW2 has focus** and text chat is **not** active (MumbleLink `uiState`).
3. **1 press = 1 action in the game**. No macros or timed sequences: ArenaNet's policy forbids them. Modifiers (Shift+1) are allowed.
4. Does not read the game's memory or inject anything into it. It only uses MumbleLink, the keybinds XML and external drawing.

---

## 2. Architecture

```
XInput/HID ──► Input ──► Mapping (layers + profile) ──► Output (SendInput)
                              │                            │
                              ▼                            ▼
                         Layer state ─────────────► Overlay (glyphs)
                              ▲                            ▲
Game: GW2 focus + MumbleLink + keybinds XML ───────────────┘
```

```
TyriaPad/
├── src/
│   ├── TyriaPad.App/          WPF: startup, tray icon, settings window, overlay
│   │   ├── Overlay/         transparent click-through window + glyph rendering
│   │   └── Settings/        configuration and calibration UI
│   └── TyriaPad.Core/         UI-free logic (testable)
│       ├── Input/           XInput poller, (phase 5) ASUS HID
│       ├── Output/          SendInput, scancode table
│       ├── Mapping/         layers, profiles, JSON config
│       ├── Game/            focus detection, MumbleLink, GW2 window
│       ├── Keybinds/        GW2 InputBinds XML, default binds, warnings
│       ├── Overlay/         slots, calibration, per-slot glyphs
│       └── Native/          P/Invoke (user32, xinput1_4)
├── tests/TyriaPad.Core.Tests/ xUnit
└── (the Xbox glyphs are drawn as vectors in the app; there is no assets folder)
```

Splitting `Core` from `App` makes it possible to test mapping, layers and parsers without opening the game.

### Key components
- **GamepadPoller**: dedicated thread that reads `XInputGetState` at ~250–500 Hz with GW2 focused and at ~10 Hz without focus (saves battery). It only raises events when `dwPacketNumber` changes.
- **FocusWatcher**: `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` plus a check that the process is `Gw2-64.exe`. No polling.
- **MumbleLinkReader**: `MemoryMappedFile` "MumbleLink". Reads `uiState` (game focus, chat active, combat, map open), the `identity` JSON (`uisize`, profession, mount) and the window size.
- **GestureDetector**: turns press/release into `tap`, `hold` and `double tap` with configurable thresholds. The tap is sent on release if it did not reach hold.
- **ContextEngine**: picks the active context (combat, cursor or mount) based on the toggle button and MumbleLink (`mountIndex`, map open).
- **LayerEngine**: within each context, handles the base layer and the modifier layers (LB, LT, LT+RT). A button can be an action in one layer and a modifier in another (RT). When the layer changes, it notifies the overlay.
- **InputEmitter**: `SendInput` with `KEYEVENTF_SCANCODE`, extended key handling and tracking of pressed keys. On focus loss or layer change it **releases everything** to avoid "stuck" keys.
- **Overlay**: WPF window with `AllowsTransparency`, `Topmost`, and `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`. It hides when GW2 is not focused.

---

## 3. Phases

Each phase ends in something that can be tested on the Ally X.

### Phase 0 — Setup
- [x] Install the **.NET 10 SDK** (10.0.401).
- [x] `git init`, `.gitignore`, `.editorconfig`, `Directory.Build.props`, README, LICENSE and the `TyriaPad.slnx` solution with the App, Core and Tests projects. `dotnet build` passes with no warnings and `dotnet test` passes.
- [ ] Set GW2 to **Windowed Fullscreen** (required for the overlay).
- [ ] Disable Steam Input for GW2 and leave Armoury Crate SE in gamepad mode.
- [ ] Set the reference layout keybinds in GW2 ([docs/reference-layout.md](docs/reference-layout.md) §1). At minimum, Action Camera on `,`.
- [x] Reference layout keybinds saved in [samples/InputBinds-blaggletoad.xml](samples/InputBinds-blaggletoad.xml).
- [ ] Also export **your** current keybinds from GW2 (exporting them from Options → Control Options) to `samples/`, to test with a real case besides the reference one.
- [x] Apply the Steam config `steam://controllerconfig/1284210/3595428420` once, only to **get its `.vdf`**, copy it to [samples/steam-blaggletoad.vdf](samples/steam-blaggletoad.vdf) and disable Steam Input again. It is used to verify the reference layout.

**Done when:** the empty solution builds. ✅ (the manual steps in GW2, Steam and Armoury Crate SE are still pending)

### Phase 1 — Play with a fixed mapping
- [x] XInput P/Invoke and `GamepadPoller` with edge detection (press/release) and stick/trigger deadzone.
- [x] `FocusWatcher` for `Gw2-64.exe`.
- [x] `InputEmitter` with scancodes and key release on focus loss.
- [x] Fixed mapping in code with the reference layout's **base layer** (taps only): left stick → WASD, RB/RT → 1/2, L3 → 6, X → Special Action Key, Y → Interact, A → Jump, B → Dodge, R3 → Nearest Enemy, D-pad → map/inventory/hero/social.
- [x] **Two camera modes; holding Menu toggles between them** (same as in the reference layout; the GW2 key is `,`):
  - **Action Camera (default mode in combat):** right stick → relative mouse movement, which turns the camera. The target is picked by aiming with the reticle (no tab-target).
  - **Cursor mode:** the app sends GW2's Action Camera key to leave it. The right stick moves the cursor and A/B do left/right click, for menus, vendors and the inventory.
  - The app keeps track of the current mode because MumbleLink does not expose whether Action Camera is active. If the state gets out of sync, it can be resynced by holding the toggle button.
- [x] Tray icon with "Pause" and "Exit".
- [x] Input blocked when MumbleLink reports chat active.

**Done when:** you can move, turn the camera, switch to the cursor and use skills 1, 2 and 6. Outside GW2 the controller behaves normally. ✅ (tested on the Ally X in desktop mode)

Implementation notes:
- Menu: tap (<250 ms) = Esc. Holding ≥250 ms sends `,` and toggles the mode. Holding on to 1.5 s resyncs: it only flips the internal mode, without sending a key.
- **The mode follows the Windows cursor** (`GetCursorInfo`): visible = cursor, hidden = camera, with an 80 ms debounce. This covers the menus and dialogs GW2 opens with Action Camera active. After holding Menu the cursor is ignored for 600 ms while the game reacts. It starts in cursor mode, like GW2.
- In cursor mode: A = left click, B = Esc, X = right click, and the right stick is slower. The other buttons stay the same.
- If GW2 runs as administrator, TyriaPad must too (Windows blocks SendInput to processes with higher privileges).
- **Xbox full screen experience (Xbox mode).** With a game in the foreground, Windows delivers the controller over XInput only to that app; TyriaPad gets it idle. Reading the HID collection with `ReadFile` doesn't get it either, nor does GameInput (phase 5). **It does arrive over Raw Input with `RIDEV_INPUTSINK`**, and since phase 6 TyriaPad reads the controller that way when XInput is idle: verified on the Ally X (controller, keys to GW2 and overlay). Only loss: the LT+RT layer.
- Diagnostics: `tyriapad.log`, next to the executable (tray → "Open log"), records focus (with a safety net every 500 ms, because the hook sometimes doesn't fire), the cursor, what comes from the controller, every key sent and a heartbeat every 10 s. `TyriaPad.exe --hid-probe` also logs the Ally X HID reports, and `--gameinput-probe` also reads the controller with GameInput and compares it with XInput.

### Phase 2 — Full layout and configuration
- [x] `Profile → Contexts → Layers → Bindings` model in JSON (System.Text.Json): `config.json` (settings) + `profiles/<name>.json` (buttons).
- [x] `GestureDetector`: tap / hold / double tap.
- [x] LB, LT and LT+RT layers, with RT as a modifier under LT (`LayerEngine`).
- [x] Combat / cursor / mount contexts, switched automatically by MumbleLink (`mountIndex`, map open) and by the cursor.
- [x] Binding types: key, key+modifier, click (held = drag), wheel with repeat, toggle Action Camera, change mode, open radial.
- [x] **Default profile = full reference layout** ([profiles/blaggletoad.json](profiles/blaggletoad.json)), built into the executable and copied next to it if missing.
- [x] Hot reload of `config.json` and `profiles/*.json` with `FileSystemWatcher`; errors are logged (JSON path + reason) and the previous configuration is kept.
- [x] Separate stick sensitivity and curve for each mode (`camera` and `pointer` in `config.json`).
- [x] Each context has its own set of layers; a context can inherit from another (`"inherits"`), like mount from combat.
- [x] Optional auto-switch to cursor with the map open (`pointerWhenMapOpen`). With chat active it isn't needed: input is already blocked entirely.
- [x] Unit tests: LayerEngine, GestureDetector, parser, configuration and the mapper with the default profile (106 in total).

**Done when:** the whole reference layout works and can be edited without recompiling. ✅ (tests and smoke test on the desktop; still to be tested on the Ally X with GW2)

Implementation notes:
- **Profile syntax.** Layers: `"base"` or the modifiers joined with `+` (`"LB"`, `"LT+RT"`). A binding is a string (held while the button is down, no delay) or an object with `tap` / `hold` / `double`. Actions: key (`"1"`, `"Shift+R"`), `click:left|right|middle` (accepts modifiers: `"Alt+click:left"`), `wheel:up|down`, `actionCamera`, `mode:pointer|camera|toggle`, `radial:<name>`. Sticks are assigned per context or per layer (`leftStick`/`rightStick`: `move`, `camera`, `pointer`, `none`).
- **Active layer.** The layer with the most modifiers held wins; on a tie the current one is kept, so LB→LT and LT→LB both give skill 5. A button whose press changes the layer is a modifier and sends nothing; if it is released right away without the layer being used, it does its base-layer tap (LB in cursor mode = right click).
- **Gestures.** The tap is only delayed if the button has a double tap (R3); otherwise it fires on release. Whatever was pressed gets released even if the layer or context changes in the meantime.
- **Radials.** They already work "blind": hold opens, the stick picks (option 0 at the top, clockwise) and a single key is sent on release. Phase 3 only draws them (`ProfileMapper.Radial` exposes the menu and the option).
- **Context.** Cursor if the mode is cursor (cursor visible or toggled with Menu) or the map is open; mount if MumbleLink gives `mountIndex ≠ 0`; combat otherwise.
- **Tray.** "Open configuration folder" and "Reload configuration"; errors are shown in a tray notification and in `tyriapad.log`. The tooltip shows the profile and context.
- Two VDF bindings that sent two keys per press (build and equipment templates on LT+Menu/View) are left out: they break rule 3. They can be added by hand with a single key.

### Phase 3 — Overlay
- [x] Transparent click-through window aligned to the GW2 client area (follows its position and size).
- [x] Slot calculation from the resolution and MumbleLink's `uisize`.
- [x] **Calibration mode**: drag markers onto slots 1, 5 and 10 (the health orb separates 5 from 6, so 1 and 10 are not enough) and onto F1 and F2, and interpolate the rest. Saved per resolution and UI size in `calibration.json`.
- [x] Xbox glyphs with the combo (e.g. `LB`+`Y`) on skills 1–0 and on the F1–F5 profession bar (configurable up to F8). Dim the inactive layer and highlight the active one.
- [x] **Draw the radial menus** (windows, masteries, mounts). The logic (open with hold, pick with the stick, a single key on release) is already in phase 2; here they only need to be painted. They replace GW2Radial.
- [x] Small indicator of the current mode (camera / cursor / mount).
- [x] Hide the overlay without focus, with chat active, with the map open (configurable) and when MumbleLink stops updating (loading screens, character select). Cutscenes are not detected: MumbleLink does not signal them.

**Done when:** the glyphs line up well on the 7" screen, change instantly when holding LB/LT and the three radials work. ✅ on the desktop (captures with `--overlay-snapshot` and preview); **still to be tested on the Ally X with GW2 and to calibrate the bar**.

Implementation notes:
- **Window.** WPF with `AllowsTransparency`, `Topmost` and the `WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` styles; it is placed with `SetWindowPos` in physical pixels over the GW2 client area (`GetClientRect` + `ClientToScreen` of the last game window that was in the foreground), checked every 200 ms. The process is per-monitor DPI aware (`app.manifest`, PerMonitorV2) so the pixels match the Ally's Windows scaling. GW2 has to be in **Windowed** or **Windowed Fullscreen**; nothing is drawn over exclusive fullscreen.
- **Positions.** The defaults (52.8 px step, 5→6 130 px, F1 75 px higher, at 1080p with Normal interface size and Windows at 100%) are multiplied by the `uisize` (Blish HUD's factors) **and by the Windows DPI scaling** of GW2's monitor (`GetDpiForWindow`): GW2 scales its interface with it (the "DPI Scaling" option, on by default). They were tuned with the real Ally X calibration (1920x1080, Small interface size, 150%: 71.5 px step), which now matches the defaults to within 6 px. `calibration.json` stores one entry per key `"1920x1080@small"` and is in `.gitignore`. During calibration the overlay receives clicks (and screen touches) without stealing focus from GW2, so the controller keeps working.
- **Glyphs.** Vector-based (no images): dark disc with a colored letter for A/B/X/Y, LB/RB/LT/RT pills, L3/R3, D-pad with the direction, Menu and View. The combo is stacked (modifier above the button) so it stays inside the slot. With the layer active, **only the button** is drawn highlighted (the modifier is already held and shows in the indicator); the rest is dimmed. Hold = dashed gold ring; double tap = "×2".
- **Radial.** Ring in the center with the pointed option in gold, labels in pills outside the ring and a hub with the option and its key ("Shift+8"). The center stays clear for the reticle.
- **Engine data.** `TyriaPadEngine.OverlayChanged` publishes an `OverlayState` (state, mode, context, layers, active layer, radial) only when it changes; the overlay paints it on the UI thread. `SlotHints` resolves which combo triggers each key 1–0 / F1–F8 in each layer (LB→LT and LT→LB are the same combo for 5; for F5–F8 the shortest combo wins, LT+D-pad, unless LT+RT is active).
- **Tray.** "Show overlay", "Calibrate overlay…" (requires GW2 to have been in the foreground once) and "Reset calibration". The `overlay` section of `config.json`: `enabled`, `scale`, `opacity`, `professionSlots`, `hideWhenMapOpen`, `indicator` (corner or `none`) and `radialRadius`.
- **Diagnostics without GW2.** `TyriaPad.exe --overlay-preview` shows the overlay full screen cycling through sample states; `TyriaPad.exe --overlay-snapshot <folder>` saves those states as 1080p PNGs and exits.

### Phase 4 — Auto-mapping from GW2 keybinds
- [x] Parser for the XML in `Documents\Guild Wars 2\InputBinds\`, following [docs/gw2-inputbinds-format.md](docs/gw2-inputbinds-format.md). Uses `id` (not `name`), primary and secondary bind, and `mod` as a Shift=1/Ctrl=2/Alt=4 mask.
- [x] Table of GW2 **default binds** (the XML only holds the changed ones).
- [x] Translation table from GW2 key codes → scancode (ASCII for letters and digits; own table for the special keys). The numeric keypad and F13+ are missing.
- [x] Tests with [samples/InputBinds-blaggletoad.xml](samples/InputBinds-blaggletoad.xml).
- [x] Resolve the chain Button → Key → GW2 action → Slot → Glyph.
- [x] Warn about conflicts (key without action, action without key).
- [x] Watch the folder to reload when the binds are exported again.

**Done when:** after changing a bind in GW2 and exporting it, the glyph moves to the right slot on its own. ✅ in the tests and with GW2 on the Ally X: skill 7 on key 8 and export → LB+Y moves to slot 7, slot 8 is left empty and the warnings "7 has no action" and "Utility Skill 2 has no key" show up.

Implementation notes:
- **Code.** `TyriaPad.Core/Keybinds/`: `Gw2KeyCodes` (GW2 code ↔ scancode), `Gw2Actions` (ids, names and default keys), `InputBindsParser`, `Gw2Keybinds` (defaults + XML, resolves action → keys and key → actions), `KeybindCheck` (warnings) and `InputBindsStore` (picks the file, watches it and reloads).
- **File.** GW2 applies key changes immediately but saves them in `Local.dat` (binary, undocumented, not read); the XML is only written when the keybinds are **exported**. That's why the most recent export in the folder is read; `inputBinds` in `config.json` accepts a name, a path or `"none"`. Without a file the default keys are assumed, and the glyphs stay as in phase 3. The whole `Documents\Guild Wars 2` folder is watched because `InputBinds` does not exist until the first export. "Check GW2 keybinds" reads the file again in case the folder notification doesn't arrive.
- **Defaults.** US keyboard. A default that matches a key in the XML is dropped, because GW2 takes it away from the previous action when reassigning it. Actions with a doubtful default are left without a key. Profession F6/F7 are assumed on F6/F7; GW2 has no F8, so that slot is still the literal F8 key.
- **Glyphs.** `SlotKeys` gives, per slot, the chords that trigger it in GW2 (primary and secondary, with modifiers). `SlotHints` looks in the profile for the buttons that send exactly that chord: if the elite moves to `Shift+0` in GW2, the profile's "0" no longer counts and the slot is left without a glyph.
- **Warnings.** Profile keys with no action in GW2 (except Esc and Enter), the left stick on W/A/S/D without moving forward or strafing, an Action Camera key different from `actionCameraKey`, skills with no key in GW2 and skills with no button in the combat context. Also the XML binds that can't be read (mouse, unknown codes). They go to the log at startup and on every reload; when the binds are exported again a tray notification shows the count, and tray → "Check GW2 keybinds" shows them in a list.
- **With the reference XML** three warnings remain, all expected: A/D turn instead of strafing (the post recommends it, but its XML doesn't include it) and F8 has no action.

### Phase 5 — Ally X extras
- [x] Read M1/M2 over HID, using the Handheld Companion and HHD code only as a protocol reference (neither is installed). Default: M1 = mount / mounts radial (what the Guide button does on Xbox in the reference layout). ✅ Tested on the Ally X with GW2: tap mounts/dismounts and hold opens the mounts radial. M2 sends the same code as M1, so it does the same.
  - Confirmed with `--hid-probe`: the vendor buttons arrive on the HID collection VID `0B05` PID `1B4C`, page `FF31` usage `76` (6 B reports, e.g. `5AEC…`, `5A93…`), also with GW2 in the foreground and in Xbox mode.
- [x] ~~Try GameInput (`GameInput.dll` ships with Windows) as an alternative to XInput to read the controller in the background in Xbox mode.~~ Dropped: it does not deliver the controller to background apps.
  - Diagnostic `TyriaPad.exe --gameinput-probe`: loads `GameInput.dll` (API v0; Windows 11 ships 0.2309) and every 10 ms reads the gamepad reading and the motion reading, without changing the focus policy. It logs button changes and, every 10 s, how many new readings GameInput gave versus XInput's packets. The first reading logs the device type and VID/PID: if the type lacks the gamepad bit, the method table is not the expected one. Tested on the dev PC (it gets created; with no controller there are no readings). **Result on the Ally X:** the first reading is correct (type `0x01040007`, VID `0B05` PID `1B4C`), but after that GameInput gives no new readings while TyriaPad is in the background, neither in desktop mode nor in Xbox mode, even though XInput counts hundreds of packets. There are no motion readings either: the Ally X gyroscope doesn't come through GameInput. In Xbox mode, XInput keeps counting packets but all of them idle (buttons and sticks at 0), so Windows delivers a neutral controller to background apps. Xbox mode is left unsupported. **Revisited in phase 6:** that probe never requested background input (`SetFocusPolicy`), and the HID probe used `ReadFile`, not Raw Input. Both were added (`--gameinput-probe` now switches to `GameInputEnableBackgroundInput` = 0x40 after 20 s and logs the GameInput service version, which can be newer than the DLL: 3.5 on the dev PC; `--rawinput-probe` registers gamepad, joystick, multi-axis and, as a control, the ASUS collection with `RIDEV_INPUTSINK`). `--xbox-probe` launches both plus the sample overlay, to also see whether the overlay is drawn in Xbox mode. **Result on the Ally (Xbox mode, GW2 in front):** GameInput still gives nothing even when background input is requested (service 3.5 installed), but **Raw Input does**: up to ~280 reports/s from the gamepad collection (VID 0B05 PID 1B4C, `MI_05&IG_00`) while XInput gives all zeros. 16 B reports: id 0, five 16-bit axes centered at 0x8000 (X, Y, Rx, Ry and Z with both triggers) and buttons after them. With that, `RawInputGamepad` was added as a fallback source (see phase 6).
- [x] ~~Gyroscope → fine mouse (aiming) with button activation.~~ Dropped: it adds nothing in GW2.
- [x] Coordination with Armoury Crate SE: recommended configuration in [docs/armoury-crate-se.md](docs/armoury-crate-se.md), verified on the Ally X (M1 unassigned, per-game profile, Command Center).
- [ ] (Optional) Import a Steam Input `.vdf` to migrate an existing layout (Gameloop.Vdf).
- [ ] (Optional) Screen touches on the overlay.

Implementation notes:
- **M1/M2 codes.** The references disagree: HHD uses `A6`/`38`/`93`/`A7`/`A8` for the Armoury Crate, Command Center and keyboard buttons; Handheld Companion puts M1/M2 on `A5`/`A8` and also gives `A7`/`A8` as hold and release of the Armoury Crate button. On top of that, HC sends a feature report (`5A D1 02 08 2C …`) to reprogram M1/M2, which breaks rule 1. So no codes are hard-coded: tray → **"Learn Ally M1/M2…"** asks you to hold each button and release it, and records the first code as the press code and the next different one (if any) as the release code. They are saved in `allybuttons.json` (`{"M1": "A5/00", "M2": "A8"}`), which is in `.gitignore`. Unassigned codes are logged once. **First test on the Ally X:** M1 = `A5`, with `00` on release. The Ally X sends `EC` by itself every ~4 s (a heartbeat), and the first version of the wizard learned it as M2. Now `EC` is always discarded, and so is any other code that arrives at a fixed cadence (`PeriodicCodeDetector`). While learning, every code received goes to the log. A button pressed with no action in the current layer is logged ("M1 has no action [Combat/base]"). That way an old profile next to the executable gets noticed. **Second test:** M2 sends the same as M1 (`5A A5`, then `5A 00`), so the two paddles can't be told apart on this collection. The wizard now detects it (`SameAsPrevious`), saves M1 and warns; both paddles do what M1 does. **Third test:** after assigning F12 to M2 in Armoury Crate SE, M1 stopped sending a code: the firmware only reports unassigned paddles on this collection. M1/M2 stay unassigned and with the same action (reading them as keys over Raw Input was dropped to avoid asking for more setup). Without `allybuttons.json`, `M1 = A5/00` is used, the code verified on the Ally X, so M1 works without learning it (in the first test in Xbox mode it didn't respond because the program was in another folder without that file). Learning the buttons replaces it. Also, a `profiles/blaggletoad.json` that is an unedited default profile from an earlier version (SHA-256 fingerprint in `Defaults`) is replaced automatically by the current one at startup. The fingerprint of the replaced version has to be added every time the default profile changes.
- **Reading.** `AllyVendorButtons` opens the collection read-only and shared on its own thread, and retries every 5 s if it isn't there (outside the Ally it only logs this once). `AllyButtonDecoder` delivers M1/M2 as `GamepadButtons` bits through `RawGamepad.Extra`, so the profile, layers, gestures and glyphs treat them like any other button. If a button sends no code on release, it is considered released after `allyButtons.releaseAfterMs` (150 ms) without reports; in that case only tap works, unless the firmware repeats the code while it is held. A tap that starts and ends between two poller reads is still delivered once.
- **Profile.** `"M1": { "tap": "X", "hold": "radial:mounts" }` in combat (and in mount, which inherits it: the tap dismounts). M2 is left free. A hand-edited `profiles/blaggletoad.json` is not touched: the M1 line has to be added there.

### Phase 6 — Polish and distribution
- [x] Full settings UI (visual layer editor).
- [x] Optional autostart when `Gw2-64.exe` is detected and automatic exit when the game closes. No services: "Start with Windows" leaves TyriaPad idle until GW2 starts.
- [x] Self-contained `win-x64` publish (`scripts/publish.ps1`) and GitHub release prepared (Actions workflow and notes). **The repository still has to be created and the `v0.1.0` tag pushed.**
- [x] Custom icon (dragon with a controller) in the `.exe`, the tray and the settings window. `src/TyriaPad.App/Assets/TyriaPad.png` is the original image; `scripts/make-icon.ps1` generates `TyriaPad.ico` (crops the leftover black background, 16–64 px as bitmaps and 256 px as PNG) and only needs to be rerun if the image changes.
- [x] Measure CPU and RAM usage on the Ally X (target: <1% CPU, <80 MB). On the Ally, playing in Xbox mode: CPU 0.1–0.7%, but private memory rose from 120 to 166 MB in 4 min. It was a leak in WPF's GPU rendering (Direct3D), reproduced on the dev PC with `--overlay-preview` (~15 MB/min, also with an opaque window). With software rendering (`RenderMode.SoftwareOnly`) it stabilizes at ~45 MB private and ~72 MB working set, with ~0.2% CPU. **Confirmed on the Ally X** (6 min in Xbox mode, 2026-09-27): 37–50 MB private and stable, CPU 0.1–0.6% with peaks of ~2% for a few seconds with the radial open. The working set (85–100 MB while playing) includes the shared Windows and .NET DLLs.

**Done when:** everything is configured from the settings window, TyriaPad starts and sleeps on its own, and there is a .zip ready to publish. ✅ Phase closed on 2026-09-27: tested on the dev PC (tests, captures with `--settings-snapshot`, smoke test) and on the Ally X, also in the Xbox full screen experience (controller over Raw Input, overlay visible, usage within target). The settings window by touch and "Start with Windows" were not tested on the Ally.

Implementation notes:
- **Settings window** (`App/Settings/`, built in code like the overlay, with WPF's Fluent theme in light or dark following Windows). Tray → "Settings…" or double-click the icon. Tabs General, Controller, Overlay, Buttons and Radials. Nothing is applied until "Save", which validates with the same parser as the program (the first error shows at the bottom and disables the button) and lets hot reload do the rest. On the Ally's screen (1280x720 at 150%) it opens maximized.
- **Layer editor.** `ControllerMap` lays out the 18 buttons as on a controller (with M1/M2), each with its glyph, the main action and below it hold/double tap, "opens layer X" or what the key does in GW2 (`Gw2Keybinds`). Whatever is inherited from another context is in italics ("from Combat") and editing it creates its own version; "Revert to inherited" removes it. The layer's modifiers show as "(held)". `BindingEditor` chooses between single press and gestures; `ActionPicker` chooses the type (key, click, wheel, Action Camera, mode, radial) and the value from lists (there usually isn't a keyboard on the Ally) or by capturing the key by scancode. Tapping L3/R3 also edits the stick's role. Layers and contexts can be created and removed (mount inherits from combat).
- **Model.** `ProfileDraft` (Core) is the profile as written: it keeps `inherits` and the actions as text, and `ToJson` rewrites it with one button per line. `ActionNames.Format` is the inverse of `TryParse`. The default profile is never overwritten: saving it asks for a name and `config.json` switches to the copy. Comments in a profile saved from the window are lost.
- **config.json with comments.** `JsonTextEditor` changes individual values without rewriting the file (it adds missing properties at the end of their object) and `SettingsWriter` compares the settings by reflection to write only what changed. It writes to a `.tmp` and replaces the file, so the watcher never reads a half-written file.
- **Idle and autostart.** `GameProcessMonitor` attaches to the GW2 process when its window comes to the foreground (and once at startup) and waits for it to exit, without polling processes; when it exits it checks again after 10 s in case the launcher started another process. With GW2 closed the controller is read at 1 Hz (`PollRate.Dormant`); with GW2 open but unfocused, at 10 Hz; focused, at 250 Hz. "Start with Windows" is a shortcut in `shell:startup` with `--autostart` (no registry). `exitWithGame` closes TyriaPad when GW2 closes, unless it was started by Windows startup.
- **Memory.** **Software rendering** for the whole process: with the GPU, WPF loaded Direct3D (~90 MB more) and leaked native memory on every overlay repaint; it was isolated by turning off parts of the rendering (drawing nothing, it didn't grow; without text or without opacity, somewhat less) and software mode fixed it. Also, the heartbeat collects if more than 12 MB of garbage piles up (the GC barely ran on its own) and logs the GC committed memory, the collections and the handles. The GC is compacted and the working set trimmed once startup finishes, when the settings window closes (WPF and the theme stay loaded: ~190 MB with it open) and when GW2 closes. No concurrent GC, and only the `es` and `en` language resources. The log rolls over past 8 MB (`tyriapad.old.log`), because with autostart TyriaPad can stay open for days. The heartbeat logs CPU (including the max), memory and threads.
- **Xbox mode over Raw Input.** `RawInputGamepad` (Core/Input) registers the gamepad and joystick HID collections with `RIDEV_INPUTSINK` on a message-only window and decodes each report with the device descriptor (`HidP_GetUsages` / `HidP_GetUsageValue`, `HidGamepadDecoder`): buttons 1–10 = A, B, X, Y, LB, RB, View, Menu, L3, R3, hat = D-pad, X/Y and Rx/Ry = sticks (Y inverted), triggers on Z (combined: LT above center, RT below) or on Z and Rz. `FallbackGamepadSource` uses XInput while it gives data and Raw Input when XInput is idle, read by read: on the desktop XInput wins (separate triggers) and in Xbox mode, Raw Input. With the triggers on a single axis, pressing both at once cancels them out and the LT+RT layer doesn't activate (F5–F8 stay on LT + D-pad). `input.rawInputFallback` turns it off. Verified on the Ally X in Xbox mode: buttons, sticks and triggers (LT above the Z center) read correctly, GW2 accepts the keys and the overlay shows on top of the game.
- **Publishing.** Self-contained `TyriaPad.exe` of ~140 MB (uncompressed: it is mapped from disk instead of being decompressed into memory) plus the 5 native WPF DLLs next to it. If they were inside the .exe, .NET would extract them to `%TEMP%\.net` and deleting the folder would no longer fully uninstall. The .zip is ~61 MB. `.github/workflows/release.yml` publishes when a `vX.Y.Z` tag is pushed, with the notes from `docs/releases/`; `ci.yml` builds and runs the tests on every push.

---

## 4. Accepted risks and limitations

| Risk | Impact | Mitigation |
|---|---|---|
| Contextual bars (mounts, kits, transforms) | Glyphs may not match | Approximate with MumbleLink's mount state; per-context profiles |
| No access to cooldowns or equipped skills | The overlay is informational only | Accepted: reading them would require reading memory (violates the ToS) |
| Stuck keys on alt-tab | Character walks on its own | Release everything on focus loss |
| Conflict with Armoury Crate SE or Steam | Duplicate input | Setup checklist and a warning if Steam Input is detected as active |
| Bar position with scaled UI or ultrawide | Misaligned glyphs | Calibration mode |
| Undocumented M1/M2 over HID | Delay in phase 5 | Doesn't block phases 1–4 |
| Guide button not exposed in public XInput | Button unavailable | Hidden ordinal `XInputGetStateEx` (#100) if needed |
| Xbox mode (full screen) doesn't deliver XInput to background apps | No controller in Xbox mode | Raw Input with INPUTSINK does arrive: it is read that way when XInput is idle. LT and RT share an axis (no LT+RT layer) |

---

## 5. Open questions

Resolved:
- ~~Camera~~ → both, with a button to toggle between Action Camera and cursor.
- ~~Targeting~~ → aiming with Action Camera.
- ~~Name~~ → TyriaPad, public repository and MIT license.

- ~~Reference layout~~ → Blaggletoad v5.0, transcribed in [docs/reference-layout.md](docs/reference-layout.md).

Resolved with the VDF:
- ~~LT + RT + X/Y/B/A~~ → face buttons (chord with RT). The VDF adds F5–F7 on the D-pad under LT. See [docs/reference-layout.md](docs/reference-layout.md) §7.
- ~~Mount skills~~ → the VDF has no mount layer: it uses the same keys (Space, V, click) and X sends `C`+`N` at once. TyriaPad will send `C` alone in the mount context.

---

## 6. Immediate next step

Phase 6 is closed. What's left is creating the repository on GitHub and pushing the `v0.1.0` tag so the Actions workflow publishes the release, and checking, whenever they get used, the settings window by touch and "Start with Windows" on the Ally. On the profile side: in the cursor context M1 has no action (mounting with the cursor visible does nothing). The warnings about A/D turning instead of strafing are expected with the user's keybinds: they use them to turn on purpose.

Pending from earlier phases: test phase 3 on the Ally X: that the overlay follows the window with the Ally's Windows scaling, calibrate the bar (tray → "Calibrate overlay…") and check that the glyphs change when holding LB/LT and that the three radials show up. Phase 4 was already tested with GW2 (moving skill 7 to key 8 and exporting moved the LB+Y glyph to slot 7 and gave the expected warnings). If the log warns about unknown key codes, complete `Gw2KeyCodes`. Phase 5: M1 already works on the Ally. M2 does the same as M1 (both have to stay unassigned in Armoury Crate SE). GameInput and the gyroscope were dropped.
