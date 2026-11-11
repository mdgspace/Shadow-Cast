# Campaign authoring and quick start

See [Quest Link validation and controller controls](VR_VALIDATION.md) for setup on the supported laptop and repeatable checks.

## Run the headset sample

The sample experience starts automatically when a scene loads. It accepts OpenXR controllers and tracked hands, with or without an authored Meta rig. Aim with the right hand and pinch the thumb and index finger to select a menu item. The menu is placed two metres in front of the headset and stays in place until another page opens. The mock wall is placed about 2.8 m ahead.

In build mode, pinch the right thumb and index finger near an object to grab it. While holding it, pinch the left thumb and index finger and point to adjust depth. Pinch the right thumb and pinky to show or hide the build menu; pinch the right thumb and ring finger to open the object picker. Left thumb-middle and thumb-ring pinches undo and redo. During play, hold a left index pinch and move that hand left or right to move, use a right middle pinch to jump, right ring pinch to dash, and right pinky pinch to pause. Keyboard and mouse remain available for desktop testing.

The menu images in `Assets/Resources/FigmaScreens` are PNG exports from [Syntax AR Dev in Figma](https://www.figma.com/design/n6UWgCq3r0xe5knCBHcUnJ/Syntax-AR-Dev). The runtime displays those original screens in world space and binds transparent hit areas to their visible cards, hands, and controls. It creates no replacement screen artwork. `CampaignUIController` remains available if the project later switches to imported screen prefabs.

## Swap the sample primitives for models

Add `ShadowCastExperienceBootstrap` to an active object in the startup scene and assign any of its optional prefab fields. Assign unit-sized prefabs for **Block**, **Ramp**, and **Orb**; assign a light prefab with a `Light` component for **Spotlight**; optionally assign a character prefab with a `Rigidbody2D` and collider, and a **Goal** prefab. Unassigned fields keep their generated primitive defaults. The model prefabs are instantiated by the sample level and the build palette. Keep pivots centered and use meters so the included sample placements remain useful. Level object layers are assigned to `ShadowCaster` at runtime.

The sample geometry and all sizes/positions are intentionally plain so those placements can be adjusted or replaced when authored models and final level layouts are ready.

## Add chapters and levels

1. In the Project window, create a folder for the level, then choose **Assets → Create → Shadow Cast → Campaign → Level**. Give the asset a unique, permanent **ID** (for example `chapter-1-level-2`) and a player-facing **Display Name**. Never reuse or later change an ID after shipping because saves and scores use it.
2. Set **Wall Width/Height**, **Character Start**, **Goal Position/Size**, and optional **Goal Condition Prefab**. Position offsets are in metres relative to the wall: X is left/right, Y is up/down, and negative Z points toward the player. Wall dimensions do not multiply these offsets.
3. Add prefab entries under **Objects** and **Lights**. For each object, set a unique placement ID, prefab, wall-local transform, scale, and whether the player may move it. Use **GameObject → Shadow Cast** to create keys, power-ups, traps, normal objects, and moving-shadow prefabs, then drag those prefabs into this list.
4. Under **Score & Hints**, choose the level's **Base Score**, **Minimum Score**, **Hint Penalty**, and ordered **Hints**. Each press reveals the next hint and subtracts the penalty, but never drops below the minimum.
5. Create or select a chapter with **Assets → Create → Shadow Cast → Campaign → Chapter**, then add the new level to its **Levels** list in play order.
6. Add `ShadowCastExperienceBootstrap` to an object in the startup scene and add the chapter under **Campaign Chapters**. The bootstrap passes those chapters to the runtime flow; leave the list empty only when you want the generated sample. In a fully manual scene setup, add the chapter directly to `CampaignFlowController` instead. The first campaign level is unlocked; completing it unlocks the next level in list order.
7. Enter Play Mode and test wall placement, editing, baking, every hint, the goal condition, retry, and completion. If the level uses a key, ensure the exit stays locked until every `LevelKeyPickup` has been collected.

For a fully manual scene-driven setup, put `CampaignFlowController`, `CampaignLevelRuntime`, `BuildModeManager`, `CampaignUIController`, and `WallPlacementController` on the gameplay root (or assign explicit references). Set `roomWallMask` to the layer used by the room's wall mesh/colliders. Using the bootstrap's **Campaign Chapters** list is the simpler route for most levels.

## Author keys, traps, gravity, and moving shadows

Use **GameObject → Shadow Cast** to create correctly configured starting objects:

- **Normal Shadow Object** uses `BuildableObject`. Its Inspector exposes **Is Moveable**, **Gravity Enabled**, and **Is Trap**. A non-moveable object can still be selected but cannot be translated or rotated. **Gravity Enabled** means the object is placement-grounded: it rests on a floor, furniture, another object, or the bottom of the build area and cannot be left floating. A trap creates a lethal play-space trigger when shadows are baked.
- **Moving Shadow → Type A** creates a blue procedural cube with a triangular arrow cutout. Choose Horizontal or Vertical; the shadow always travels on that fixed wall axis.
- **Moving Shadow → Type B** creates an orange procedural cube with the same real cutout. Rotate the source before baking to rotate both its arrow and its travel direction.
- **Key Collectible** creates an object with `LevelKeyPickup`. Make it a prefab and add it to a level's Objects list. When a level contains one or more keys, its exit turns red and automatically requires every key before completion; it turns green after the last key is collected.
- **Power-up → Double Jump** and **Power-up → Dash** create dedicated `CharacterAbilityPowerup` objects. Set **Uses Granted** in the Inspector, make the object a prefab, and add it to the level's Objects list. Every airborne second jump or dash consumes one stored use. Desktop controls are **Space/W/Up** for jump and **Shift/E** for dash; the hand controls use right middle-finger and ring-finger pinches.
- **Lamp → Point Light** creates a warm omnidirectional lamp, while **Lamp → Directional Light** creates a cool sun-style lamp whose rotation controls its direction. Make either object a prefab and add it under the level's **Lights** list. Each light placement has its own **Light Type** and **Movable** checkbox; the level applies that type at runtime even if the prefab was authored differently.

Attempting to grab a normal object with **Is Moveable** disabled, or a light placement with **Movable** disabled, leaves it in place and shows a clear warning for 2.5 seconds. This applies to both direct XR pinch grabs and pointer-driven grabs because the check lives in the shared object manipulator.

Moving-shadow objects use `MovingShadowPlatform`, not `BuildableObject`. Their playable shadow and matching arrow-shaped collision hole are generated after the normal bake, move automatically between the wall edges, and are removed when returning to Build Mode. The cube-minus-triangle mesh is generated by `ArrowCutoutCube`, so there is no modeled mesh asset to maintain.

The campaign fallback character is the procedural soft Blob from `Assets/Blob`. `WallPlatformerController2D` is the single movement authority for both generated and prefab characters, with acceleration/deceleration, air control, coyote time, jump buffering, variable jump height, faster falling, terminal velocity, and a short gravity-free dash. Existing Blob prefabs keep working through the lightweight `BlobController` adapter.

## HUD, pause, settings, and local saves

During play, the world-space HUD shows the remaining consumable **Double Jump** and **Dash** uses and provides large Jump, Dash, and Pause buttons. Press **Escape** on desktop or the left-controller menu button in XR to pause. The pause menu supports Resume, Save Game, Load Last Save, Settings, and Return to Main Menu. Settings currently include master volume and reduced-motion UI tracking and are persisted immediately.

The HUD also shows the current level score and a **Hint** button. Hints are revealed in the same order as the level asset's **Hints** list. Every newly revealed hint applies the configured penalty exactly once; pressing after the last hint does not cost points. Completion shows the run score, that level's saved best, and the campaign total. Replaying a level can improve its saved best score, while the campaign total is the sum of best scores rather than a farmable sum of every run.

Campaign progress, best scores, the most recently selected level, and player settings are stored in `shadow-cast-campaign.json` under `Application.persistentDataPath`. Writes use a temporary file and retain a `.bak` copy of the previous save. The main menu displays **Continue Local Save** whenever the file exists; loading returns to the saved level's wall-placement step. Level completion still auto-saves, while the pause menu also exposes an explicit save action.

