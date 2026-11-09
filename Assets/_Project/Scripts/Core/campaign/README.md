# Campaign authoring and quick start

## Run the headset sample

The sample experience starts automatically when a scene loads. Use the existing XR camera rig in `Assets/Scenes/SampleScene.unity`, or any scene with an active `MainCamera`. In the headset, look at the pictured controls and press the right controller trigger. The mock wall is placed about 2.8 m in front of the headset, so the first loop works without room scanning or Scene API permissions.

Flow: **Story Mode → chapter → level → confirm mock wall → select a primitive → adjust the ground → bake → reach the green gate**. The home screen's **Quick Play** opens the Figma difficulty screen; each choice starts the single sample level. The main menu's Live Worlds card also opens Quick Play, and Account opens the chapter list.

During setup, select one of the pictured objects to add a replaceable primitive to the wall; the overlay hides so the wall and objects can be grabbed and moved. Press **Tab** or the right controller menu button to show the adjustment screen: look at **Explore Next World** and press the right trigger to bake and start. Tap an empty area to hide the screen and return to editing. Press **P** or the right controller B button to reopen the object picker and add more primitives. During play, use the left stick to move and right controller A to jump. The pictured joystick and action buttons also work as gaze targets with the right trigger when the Figma play screen is open. The back control returns to building, and falling restarts the level. Keyboard A/D and Space work in desktop play mode. The play screen starts hidden so it does not cover the mock level; use **Tab** or the right controller menu button to show it.

The menu images in `Assets/Resources/FigmaScreens` are PNG exports from [Syntax AR Dev in Figma](https://www.figma.com/design/n6UWgCq3r0xe5knCBHcUnJ/Syntax-AR-Dev). The runtime displays those original screens in world space and binds transparent hit areas to their visible cards, hands, and controls. It creates no replacement screen artwork. `CampaignUIController` remains available if the project later switches to imported screen prefabs.

## Swap the sample primitives for models

Add `ShadowCastExperienceBootstrap` to an active object in the startup scene and assign any of its optional prefab fields. Assign unit-sized prefabs for **Block**, **Ramp**, and **Orb**; assign a light prefab with a `Light` component for **Spotlight**; optionally assign a character prefab with a `Rigidbody2D` and collider, and a **Goal** prefab. Unassigned fields keep their generated primitive defaults. The model prefabs are instantiated by the sample level and the build palette. Keep pivots centered and use meters so the included sample placements remain useful. Level object layers are assigned to `ShadowCaster` at runtime.

The sample geometry and all sizes/positions are intentionally plain so those placements can be adjusted or replaced when authored models and final level layouts are ready.

## Add chapters and levels

1. Create a `ChapterDefinition` from **Assets → Create → Shadow Cast → Campaign → Chapter**. Give it a stable ID, display name, and chapter artwork, then add its `LevelDefinition` assets in play order.
2. Create each level from **Assets → Create → Shadow Cast → Campaign → Level**. Set a stable ID, wall size, independent object placements, one or more light prefabs with per-light movement settings, character start, and goal position. Object and goal positions are relative to that level's confirmed wall pose.
3. Add the chapter assets to `CampaignFlowController` in campaign order. The first level is unlocked; each completed level unlocks the next one. Completion is stored locally by level ID.
4. Put `CampaignFlowController`, `CampaignLevelRuntime`, `BuildModeManager`, `CampaignUIController`, and `WallPlacementController` on the gameplay root (or assign explicit references). Set `roomWallMask` to the layer used by the room's wall mesh/colliders.

## Author keys, traps, gravity, and moving shadows

Use **GameObject → Shadow Cast** to create correctly configured starting objects:

- **Normal Shadow Object** uses `BuildableObject`. Its Inspector exposes **Is Moveable**, **Gravity Enabled**, and **Is Trap**. A non-moveable object can still be selected but cannot be translated or rotated. **Gravity Enabled** means the object is placement-grounded: it rests on a floor, furniture, another object, or the bottom of the build area and cannot be left floating. A trap creates a lethal play-space trigger when shadows are baked.
- **Moving Shadow → Type A** creates a blue procedural cube with a triangular arrow cutout. Choose Horizontal or Vertical; the shadow always travels on that fixed wall axis.
- **Moving Shadow → Type B** creates an orange procedural cube with the same real cutout. Rotate the source before baking to rotate both its arrow and its travel direction.
- **Key Collectible** creates an object with `LevelKeyPickup`. Make it a prefab and add it to a level's Objects list. When a level contains one or more keys, its exit turns red and automatically requires every key before completion; it turns green after the last key is collected.
- **Power-up → Double Jump** and **Power-up → Dash** create dedicated `CharacterAbilityPowerup` objects. Set **Uses Granted** in the Inspector, make the object a prefab, and add it to the level's Objects list. Every airborne second jump or dash consumes one stored use. Desktop controls are **Space/W/Up** for jump and **Shift/E** for dash; the default XR controls use right-controller A and B.

Moving-shadow objects use `MovingShadowPlatform`, not `BuildableObject`. Their playable shadow and matching arrow-shaped collision hole are generated after the normal bake, move automatically between the wall edges, and are removed when returning to Build Mode. The cube-minus-triangle mesh is generated by `ArrowCutoutCube`, so there is no modeled mesh asset to maintain.

The campaign fallback character is the procedural soft Blob from `Assets/Blob`. `WallPlatformerController2D` is the single movement authority for both generated and prefab characters, with acceleration/deceleration, air control, coyote time, jump buffering, variable jump height, faster falling, terminal velocity, and a short gravity-free dash. Existing Blob prefabs keep working through the lightweight `BlobController` adapter.

## HUD, pause, settings, and local saves

During play, the world-space HUD shows the remaining consumable **Double Jump** and **Dash** uses and provides large Jump, Dash, and Pause buttons. Press **Escape** on desktop or the right-controller menu button in XR to pause. The pause menu supports Resume, Save Game, Load Last Save, Settings, and Return to Main Menu. Settings currently include master volume and reduced-motion UI tracking and are persisted immediately.

Campaign progress, the most recently selected level, and player settings are stored in `shadow-cast-campaign.json` under `Application.persistentDataPath`. Writes use a temporary file and retain a `.bak` copy of the previous save. The main menu displays **Continue Local Save** whenever the file exists; loading returns to the saved level's wall-placement step. Level completion still auto-saves, while the pause menu also exposes an explicit save action.

