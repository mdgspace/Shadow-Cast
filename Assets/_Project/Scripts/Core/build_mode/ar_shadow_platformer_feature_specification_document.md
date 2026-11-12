# AR Shadow Platformer: Feature Specification Document

## 1. Executive Summary & Vision
**Project Title:** Shadow Architect (Working Title)  
**Target Device:** Meta Quest 3 (Meta XR Interaction SDK, Unity Engine)  
**Core Gameplay Loop:** Players place physical and virtual 3D objects inside their room in **Build Mode**. Real-time directional/point light sources cast shadows from these objects onto physical walls. In **Play Mode**, these 2D shadow silhouettes are baked into physical platforming surfaces for a 2D/3D character to traverse.

---

## 2. Spatial Architecture & Bounding Constraints

### 2.1 Predefined Bounding Volume (Play Envelope)
To prevent objects from being clipping outside the playable mixed-reality area or disappearing into untracked room space, all virtual objects are strictly constrained within a **Bounded Play Envelope**.

* **Volume Geometry:** A configurable 3D Box/Axis-Aligned Bounding Box (AABB) or Oriented Bounding Box (OBB) anchorable to the room center.
* **Hard Bounds Enforcement:**
  * When an object is moved via hand gestures, its center position $\mathbf{P}_{\text{obj}}$ is clamped to the bounding box extents:
    $$\mathbf{P}_{\text{clamped}} = \begin{bmatrix} \text{clamp}(P_x, X_{\text{min}} + r_x, X_{\text{max}} - r_x) \\ \text{clamp}(P_y, Y_{\text{min}} + r_y, Y_{\text{max}} - r_y) \\ \text{clamp}(P_z, Z_{\text{min}} + r_z, Z_{\text{max}} - r_z) \end{bmatrix}$$
    *(where $r_x, r_y, r_z$ are the extents of the object's oriented bounding box).*
* **Visual Boundary Feedback:**
  * **Holographic Boundary Grid:** When an object approaches within $0.15\,\text{m}$ of the volume edge, a subtle wireframe/grid shader fades in on that face of the bounding box.
  * **Haptics:** Gentle haptic pulse sent to the controller/wrist when hitting the boundary edge.

---

## 3. Surface & Grid Snapping Engine

### 3.1 Spatial Anchors & Surface Detection
Using the Meta Spatial Scene API (Room Setup Mesh), the system classifies surfaces into four categories:
1. **Walls** (Vertical Planes)
2. **Floors** (Horizontal Bottom Planes)
3. **Ceilings** (Horizontal Top Planes)
4. **Furniture Surfaces** (Tables, Couches, Desks)

### 3.2 Dynamic Surface Snapping
* **Raycast Alignment System:**
  * While grabbing an object, a short Raycast ($\text{length} = 0.3\,\text{m}$) is cast along the object’s cardinal axes toward detected room mesh surfaces.
  * **Thresholding:** If the object surface is within $\text{Snap Distance} \le 0.1\,\text{m}$ and the surface normal aligns within $25^\circ$ of a cardinal axis, the object automatically snaps its position and aligns its normal to the target surface.
* **Surface Modes:**
  * **Wall Snap:** Flushes object back-face to the wall surface, preserving lateral ($X, Y$) movement relative to the wall plane.
  * **Floor/Ceiling Snap:** Aligns object base or top to horizontal planes, clamping vertical ($Y$) motion while allowing horizontal planar sliding.
  * **Furniture Surface Snap:** Allows stacking on real-world desk/table meshes.

### 3.3 Optional Incremental Grid Snapping
* **Grid Toggle:** Players can toggle **Grid Snap** on/off via the hotbar or wrist UI.
* **Grid Specifications:**
  * **Translation Snap:** $0.05\,\text{m}$ ($5\,\text{cm}$) or $0.1\,\text{m}$ ($10\,\text{cm}$) increments.
  * **Rotation Snap:** $15^\circ$, $45^\circ$, or $90^\circ$ rotational increments around the local or world axis.
* **Visual Snap Alignment Helper:** Displays ghosted grid lines on snapped surfaces to guide precise alignment.

---

## 4. User Interaction & Control Scheme

### 4.1 UI Layout: Inventory Hotbar
* **Wrist-Anchored Carousel:**
  * Floating curved carousel attached to the non-dominant wrist or locked in room space near the user.
  * Displays thumbnail previews of virtual primitive shapes (cubes, ramps, spheres, archways, customized silhouettes).
* **Focus & Selection:**
  * **Carousel Scroll:** Horizontal swipe gesture over the hotbar to rotate items.
  * **Item Focus:** Active item expands slightly with a soft glow in the center slot.
  * **Pinch-To-Spawn:** Direct pinch or ray-pinch on the focused item spawns a clone into the active build space.

ui is implemented as a different module

### 4.2 Hand-Tracking & Gesture Specification

| Gesture / Input | Target Action | Behavior & Calculation |
| :--- | :--- | :--- |
| **Pinch (Right Hand)** | Direct Grab | Attaches target object to right-hand index/thumb pinch point. |
| **Pinch + Wrist Roll (Right Hand)** | 1-Axis Rotation | Rotates object around its local Y-axis (or camera-aligned Z-axis) proportional to wrist angular delta $\Delta \theta$. |
| **Pinch + Hand Translation (Right Hand)** | Screen-Space $X/Y$ Move | Translates object along the camera’s horizontal and vertical projection plane within volume limits. |
| **Left Hand Point Forward ($\vec{d}_{\text{point}} \cdot \vec{d}_{\text{cam}} > 0.8$)** | Push Object Away ($+Z$) | Moves selected object along the camera forward vector at constant velocity ($v = 0.75\,\text{m/s}$). Stops instantly when pointing stops. |
| **Left Hand Point Backward ($\vec{d}_{\text{point}} \cdot \vec{d}_{\text{cam}} < -0.8$)** | Pull Object Closer ($-Z$) | Pulls selected object toward the user at constant velocity ($v = 0.75\,\text{m/s}$). Stops instantly when pointing stops. |

| **Pinch + Shake Hand Rapidly** | Delete Object | Quick back-and-forth shake while pinching deletes the object and returns it to inventory (with undo pop-up). |

---

## 5. Shadow Baking Pipeline & Play Mode Transition

### 5.1 Virtual "Bake" Button
* **World-Space UI Element:** A prominent 3D virtual button fixed to the build interface or left wrist.
* **Visual States:**
  * `Idle / Unbaked`: Pulsing blue aura.
  * `Baking Process`: Progress fill with light sweep animation.
  * `Baked / Play Active`: Green active indicator.

### 5.2 The Shadow Baking Execution Flow
When the user presses the **Bake** button:

```
[ Build Mode Active ]
        │
        ▼
[ Trigger Bake Button ] ──► Freeze All Object Transforms & Disable Grabbables
        │
        ▼
[ Assign Layers ] ───────► Move Objects to Layer "ShadowCasters"
        │
        ▼
[ Shadow Projection ] ──► Project 3D Meshes along Light Source Rays onto Wall Plane Mesh
        │
        ▼
[ Collider Generation ] ─► Generate 2D PolygonColliders / Composite MeshColliders along Shadow Outline
        │
        ▼
[ Play Mode Active ] ────► Spawn 2D Shadow Character on Wall Surface; Enable Gravity & Controls
```

1. **Transform Lock:** Disable all `Grabbable` and hand interaction components on build objects.
2. **Layer Masking:** Reassign all active build objects to layer `ShadowCasters`.
3. **Shadow Mesh Extraction:**
   * Compute directional/point light occlusion relative to the target physical wall mesh.
   * Calculate the 2D silhouette contour of projected shadow geometry.
4. **Collider Generation:**
   * Generate 2D edge colliders (`EdgeCollider2D` or 3D extrusions) coinciding exactly with the cast shadow shapes on the wall plane.
5. **Mode Switch:** Enable 2D platformer character control, spawning the avatar onto the starting shadow platform.

---

## 6. Quality of Life (QoL) & Advanced Features

### 6.1 Shadow Preview & Light Source Manipulation

* **Movable Light Source:**
  * Allow players to grab and place the **Virtual Sun / Lamp Light Source**.
  * Moving the light source dynamically scales, stretches, and skews all active shadows across the room simultaneously, creating dynamic puzzle mechanics.
  
  i should be able to easily enable/ disable moving the light for any particular level (as the dev)

### 6.2 Undo / Redo & Multi-Select Operations
* **Action History Stack:** Full Undo/Redo stack supporting up to 10 spatial actions (Move, Rotate, Spawn, Delete).
* **Wrist Quick Gestures:**
  * Double-tap left wrist for **Undo**.
  * Triple-tap left wrist for **Redo**.
* **Group Selection (Lasso / Volume Select):**
  * Box-select multiple virtual objects to translate, rotate, or delete them as a single group.

### 6.3 Real-World Physical Object Integration
* **Real-World Occlusion & Shadows:**
  * Players can place real physical objects (e.g., books, boxes, hands) into the physical light path.
  * Quest 3 Passthrough depth engine detects real objects and merges their real shadows with virtual shadows into the platforming collision map.

### 6.4 Save / Load Room Layouts
* **Spatial Anchor Persistence:** Save placed object coordinates, scale, and room positions relative to Quest 3 Spatial Anchors.
* **Preset Load:** Load previously saved platforming levels seamlessly back into the same physical room setup.

this should NOT be implemented right now, only in later versions
---

## 7. System Architecture & Class Diagram

```
                       ┌─────────────────────────┐
                       │   BuildModeManager      │
                       └────────────┬────────────┘
                                    │
         ┌──────────────────────────┼──────────────────────────┐
         ▼                          ▼                          ▼
┌──────────────────┐      ┌──────────────────┐      ┌──────────────────┐
│ ObjectManipulator│      │ SurfaceSnapEngine│      │ BoundingVolume   │
├──────────────────┤      ├──────────────────┤      ├──────────────────┤
│ - PinchGrab()    │      │ - SnapToWall()   │      │ - ClampToBox()   │
│ - RotateWrist()  │      │ - SnapToFloor()  │      │ - GridBoundary() │
│ - DepthPoint()   │      │ - GridSnap()     │      │ - TriggerHaptic()│
└──────────────────┘      └──────────────────┘      └──────────────────┘
         │                          │                          │
         └──────────────────────────┼──────────────────────────┘
                                    │
                                    ▼
                       ┌─────────────────────────┐
                       │   ShadowBakePipeline    │
                       ├─────────────────────────┤
                       │ - FreezeTransforms()    │
                       │ - ProjectSilhouettes()  │
                       │ - GenerateColliders()   │
                       └─────────────────────────┘
```

---

## 8. Summary Checklist for Implementation Sprint

- [ ] Set up `BoundingVolumeManager` with clamp logic for $X, Y, Z$ limits.
- [ ] Implement `SurfaceSnapEngine` using raycasts against Spatial Room Mesh (Wall/Floor/Ceiling).
- [ ] Implement dual-hand depth translation (`Left Hand Pointing`).
- [ ] Build wrist inventory carousel UI with focus/select states.
- [ ] Integrate real-time shadow preview projection during translation.
- [ ] Implement `ShadowBakePipeline` for converting 3D shadows to 2D platform colliders.
- [ ] Add QoL Undo/Redo system and dynamic light source grab control.


main thing: this entire system ("build mode" aka adding, manipulating objects) must be gameobjects + scripts, not a separate scene. i should be able to easily attach this to any scene easily