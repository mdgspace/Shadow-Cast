# Quest Link validation

Use Unity **6000.6.6f1**, as recorded in `ProjectSettings/ProjectVersion.txt`. Copy or clone the project with `Assets`, `Packages`, and `ProjectSettings`; let Unity regenerate `Library` on the other laptop.

## On the supported laptop

1. Configure Meta Quest Link. In its General settings, set Meta as the active OpenXR runtime, connect the Quest, enter Link inside the headset, and keep it awake before pressing Play. See Meta's [Use Link for App Development](https://developers.meta.com/vr/documentation/unity/unity-link/).
2. For room video, enable **Developer Runtime Features** and **Passthrough over Meta Quest Link** in the Link PC app under **Settings > Beta**, then restart Unity. See Meta's [Passthrough over Link prerequisites](https://developers.meta.com/vr/documentation/unity/unity-passthrough-use-over-link/). Normal Link alone does not enable this developer feature.
3. Open `Assets/Scenes/SampleScene.unity`, the enabled build startup scene. For a PC executable, select a Windows build profile; Android builds are standalone Quest applications.
4. In XR Plug-in Management, verify OpenXR and initialization on startup for the PC target. Keep Oculus Touch Controller Profile enabled. The project includes Hand Tracking Subsystem and Meta Hand Tracking Aim, which supply hands in scenes without Interaction SDK `Hand` components.
5. Keep Mock Runtime and custom API debug layers disabled for the real headset. The project settings disable the Android mock runtime and the desktop operator debug layer.
6. Enter Play Mode. Turn and move your head: the view must track once, without snapping between two poses. Startup supplies a driver for a bare camera and disables the legacy driver when an Input System driver is already present.
7. Navigate Story → chapter → level → wall placement. Choose a wall facing a different direction from world Z. Verify the wall and its objects face you, and that text reads normally.
8. Close the object picker, grab and move an object, release it, undo, redo, and bake. The character must appear on the wall, stay on the starting platform, move, and jump. Reopen the build menu to bake after editing.
9. Test pause/resume, Return to Build, retry after a fall, and returning to level selection during a bake. Check for leftover objects, duplicate characters, and delayed transitions into Play.
10. Lose and restore controller tracking while holding an object; it should release. Test hands separately if enabled for the Link development session. Meta documents Windows hand tracking through Link as an [Editor development feature](https://developers.meta.com/vr/documentation/unity/unity-handtracking-overview/).

## Controller controls

| Action | Control |
| --- | --- |
| Select a menu item | Point the right controller and press its trigger |
| Grab a build object | Right grip near the object, or point and grip |
| Push/pull while holding an object | Hold the left trigger and point forward/back |
| Build menu / pause during play | Left menu button |
| Open object picker | X |
| Undo / redo | Y / left thumbstick click |
| Move character | Left thumbstick |
| Jump / dash | A / B; dash requires a collected charge |

Hand gestures remain available: right index selects/grabs; right pinky opens the menu; right ring opens the picker in Build and dashes in Play; right middle jumps; left middle/ring undo/redo. Hold a left index pinch and move that hand horizontally for movement in Play.

## Desktop and automated checks

Mouse selects menus and drags build objects. Tab toggles the overlay; P opens the picker; B bakes while editing; A/D or arrows move; Space/W/Up jumps; Shift/E dashes; Escape pauses.

- In Play Mode, choose **Tools > Shadow Cast > Run Startup Visibility Check (30 seconds)**. This verifies that the XR origin does not fall without a world floor and that the menu stays in view after a delayed tracking-origin change. Results go to `Logs/VR-startup-visibility.json`.
- Run `ShadowCastRegressionTests` from Unity's Edit Mode Test Runner. These cover rotated build bounds, high-resolution edge-touching shadow contours, scenery rejection, destroyed undo targets, and Oculus Touch input bindings.
- In Play Mode, choose **Tools → Shadow Cast → Run Play Mode Smoke Checks**. This uses the real generated sample campaign and writes `Logs/VR-smoke-checks.json`. It checks navigation, goal creation, authoring units, baking, rotated-wall physics, idle stability, full-body jump/movement, pause, return to Build, retry cleanup, and bake cancellation. It returns to the main menu when finished.

## Startup failures and remaining hardware checks

`XR_ERROR_FORM_FACTOR_UNAVAILABLE` / `ErrorFormFactorUnavailable xrGetSystem` means the selected runtime did not provide an available headset. Recheck Link, the active OpenXR runtime, headset wake state, and the laptop's supported GPU before entering Play again. Game code cannot make an unsupported GPU compatible with Link.

The game keeps an opaque desktop background until XR and the passthrough compositor are ready. Unavailable passthrough should not prevent menus or gameplay; passthrough availability still depends on the runtime configuration.

Unity runtime and Editor compilation and desktop smoke checks passed in this workspace in both the testing scene and `SampleScene`, including an obliquely placed wall. The current laptop cannot provide the intended supported-GPU Quest Link session. Physical-headset rendering, hand tracking, passthrough, and sustained VR frame rate must be verified on the other laptop. No Windows player build or physical-headset performance result is claimed.

The campaign disables the Meta interaction prefab's first-person locomotor and its character controller at runtime. This is a stationary mixed-reality wall game; the 2D wall character owns movement input. Without this restriction, the sample rig applies gravity with no world floor, dropping the camera away from the UI. Physical headset tracking remains enabled.
