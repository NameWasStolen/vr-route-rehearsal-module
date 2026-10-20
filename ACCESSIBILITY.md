# Accessibility: audio buses + font scaling

Built against `dev` at `a0d33fc` (all three PRs merged). Compile-checked — `exit=0`.

## Files

| File | Goes in | Status |
|---|---|---|
| `Settings/AccessibilitySettings.cs` | `Assets/Scripts/Settings/` | New — the service |
| `Settings/ScalableText.cs` | `Assets/Scripts/Settings/` | New |
| `Settings/ScalableRect.cs` | `Assets/Scripts/Settings/` | New |
| `Settings/SettingsController.cs` | `Assets/Scripts/Settings/` | **Replaces** the current file |
| `Audio/SurfaceMarker.cs` | `Assets/Scripts/Audio/` | New |
| `Audio/FootstepAudio.cs` | `Assets/Scripts/Audio/` | New |
| `Audio/UiCuePlayer.cs` | `Assets/Scripts/Audio/` | New |
| `Audio/CueRelay.cs` | `Assets/Scripts/Audio/` | New — one per content scene |
| `Patches/TutorialFlow.patch.md` | — | 4 small edits to your existing file |

`SettingsController`'s serialized fields are all preserved by name except `audioMixer` and
`textElements`, which move to the service and disappear respectively. Everything you wired in
the Inspector survives the swap; those two will show as removed.

---

## 1. Audio mixer — do this first

The mixer currently has one group (`Master`) and one exposed parameter. Everything else depends
on it being split, and splitting it later means re-routing every AudioSource, so it's the first
job.

In **Window → Audio → Audio Mixer**, with `MainAudioMixer` selected:

1. Add three child groups under `Master`: **Ambience**, **UI**, **Movement**.
2. For each of the four groups, right-click its **Volume** in the Inspector → *Expose … to script*.
3. In the **Exposed Parameters** dropdown (top right), rename them to exactly:
   `MasterVolume`, `AmbienceVolume`, `UIVolume`, `MovementVolume`.

The names must match exactly or that bus's slider silently does nothing — `AccessibilitySettings`
logs a warning naming the missing parameter rather than failing quietly.

Then re-route `CityAudio` in Bootstrap from `Master` to **Ambience**. That's the point of the
split: street ambience is the one bus that is pure atmosphere and also the one most likely to
mask an instruction, so a participant needs to be able to pull it down without losing the cues.

---

## 2. Bootstrap wiring

**On a new `AccessibilitySettings` GameObject** (alongside `ControllerHandednessManager`):

- `mixer` → `MainAudioMixer`
- leave the four parameter-name fields at their defaults
- defaults are authored on the component — ambience starts lower than the rest, deliberately

**On a new `UiCuePlayer` GameObject** (needs an `AudioSource`):

- `output` → the **UI** mixer group
- assign `stepComplete`, `stepAdvance`, and optionally `gentleRetry` / `flowComplete`

**On the `XRPlayerRig` root** (needs a second `AudioSource`):

- add `FootstepAudio`, `output` → the **Movement** group
- add surface entries for `Grass` and `Paving`, two or more clips each
- material keywords: `grass`, `lawn` for grass; `stone`, `concrete`, `paving` for paving

Footsteps resolve the surface by raycasting down and reading a `SurfaceMarker` if there is one,
falling back to matching the material name. The generated environment has no markers yet, so
the fallback is what will actually run — which means **you get working footsteps without
rebuilding the scene and re-baking the lighting**. If you do rebuild later, one
`SurfaceMarker` line in `CreateSlab` makes it exact.

---

## 3. Font scaling

Add **`ScalableText`** to every TMP text you want to respond — the 6 in `Tutorial.unity` and the
12 in `MainMenuScreen.prefab`. No list to maintain: each text finds the setting itself, which is
the only arrangement that works across additive scene loads.

Add **`ScalableRect`** to the **MainMenuScreen** panel only.

**Do not** add `ScalableRect` to the tutorial panel. `TutorialFlow` owns that RectTransform's
`sizeDelta` and rewrites it on every step change, so the two would fight. Apply
`Patches/TutorialFlow.patch.md` instead — it folds the scale into `PlacementFor`, so it composes
with your per-step `panelSize` overrides and animates through the existing lerp.

One thing to check while you're in there: the six tutorial texts need to be **anchor-stretched**
to the panel. If any has a fixed width, the frame will grow around it and the text will keep
wrapping at its old width.

---

## 4. Cues

Add a **`CueRelay`** to `Tutorial.unity` (empty GameObject, name it `CueRelay`), then wire the
scene's UnityEvents to **that**, not to `UiCuePlayer`:

| Event | Target |
|---|---|
| `TutorialFlow.onStepChanged` | `CueRelay.PlayStepAdvance(int)` |
| `SnapTurnTask.onCompleted` | `CueRelay.PlayStepComplete()` |
| `TutorialFlow.onFlowCompleted` | `CueRelay.PlayFlowComplete()` |

**Why the relay and not `UiCuePlayer` directly.** `UiCuePlayer` lives in Bootstrap;
`TutorialFlow` lives in `Tutorial.unity`. A UnityEvent cannot hold a reference across scenes —
the exact same limitation that made the old `textElements` list unable to reach the tutorial.
Dragging the Bootstrap object into a tutorial-scene event either refuses or silently resolves
to `None` at runtime. `CueRelay` is local to the scene, so it serialises, and it forwards to
the singleton through a static lookup, which is the only cross-scene reference that works.

On clip choice: the completion cue should be short, warm and low. A bright ping reads as an
alert, and an alert is the wrong message for "you did that right". The advance cue should be
quieter than the completion one — it's asking for attention rather than rewarding anything.

---

## 5. Operator reset

`AccessibilitySettings.ResetToDefaults()` restores everything and clears the saved values.
Wire it to `SettingsController.resetToDefaultsButton`, and put that button somewhere a
participant won't reach by accident — it's for handing the headset to the next person.

Settings otherwise persist via `PlayerPrefs`, including the controller hand, which did not
persist before.

---

## Two fixes folded in

**The brightness listener leak.** The previous `SettingsController` registered `SetBrightness`
from *inside* the brightness slider's own value-changed callback, so every movement of the
slider added another permanent listener — one drag registered dozens, none were ever removed,
and it mutated the event's invocation list during invocation. Listeners are now bound once in
`Start` and removed in `OnDestroy`.

**Volume was mapped linearly to decibels.** `Lerp(-20f, 0f, v)` spends most of the slider's
travel on changes nobody can hear and never reaches silence — at zero it's still only -20 dB.
Volume now maps logarithmically, so halfway sounds halfway and zero is actually off. That
matters more than it sounds for a cohort who may need ambience genuinely muted.

I also narrowed brightness from ±2 EV to ±1.2. At ±2 the baked lighting either washes out or
crushes to the point where kerbs and path edges stop being readable, which is a safety-relevant
detail in a route-rehearsal module.

---

## Test list

- Change font size in the menu, then enter the tutorial → text and panel are already scaled on
  the first frame, not scaled a moment later. This is the case the old `textElements` list
  could not do at all.
- Change font size **while** a tutorial step is showing → the frame eases to its new size.
- Slide font size to max and back to min repeatedly → sizes return exactly to authored values,
  no drift. (Every component multiplies the captured authored value, never the live one.)
- Walk from the path onto the grass → the footstep sound changes.
- Stand still and look around → no footsteps.
- Set master volume to zero → actual silence.
- Set a setting, quit, relaunch → it is still set. Press operator reset → back to defaults.
- Open `Tutorial.unity` standalone with no Bootstrap → no null refs; everything falls back to
  its default scale.

---

## Still open

`SettingsController`'s rotation toggles remain `// TODO`, and the comment now records why that
matters: `SnapTurnTask` detects a single-frame yaw jump, which by design never fires under
continuous rotation. The moment that setting works, a participant who chooses continuous
reaches the camera step and cannot complete it. Worth settling before the setting ships.

Seated mode is also still `// TODO`. When it lands it belongs in `AccessibilitySettings` so it
persists with everything else, rather than in the menu.
