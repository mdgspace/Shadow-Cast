using ShadowCast.Core.BuildMode;
using ShadowCast.Core.Campaign;
using UnityEditor;
using UnityEngine;

namespace ShadowCast.EditorTools
{
    /// <summary>One-click primitives for level designers. No external modeled assets are required.</summary>
    public static class ShadowCastLevelAuthoringMenu
    {
        [MenuItem("GameObject/Shadow Cast/Normal Shadow Object", false, 10)]
        private static void CreateNormalObject()
        {
            GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "Shadow Object";
            target.AddComponent<BuildableObject>();
            FinishCreation(target, "Create Normal Shadow Object", "ShadowCaster");
        }

        [MenuItem("GameObject/Shadow Cast/Moving Shadow/Type A - Fixed Axis", false, 20)]
        private static void CreateTypeA() => CreateMovingShadow(MovingShadowType.TypeA_FixedAxis);

        [MenuItem("GameObject/Shadow Cast/Moving Shadow/Type B - Rotation Driven", false, 21)]
        private static void CreateTypeB() => CreateMovingShadow(MovingShadowType.TypeB_RotationDriven);

        [MenuItem("GameObject/Shadow Cast/Key Collectible", false, 30)]
        private static void CreateKey()
        {
            GameObject key = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            key.name = "Level Key";
            key.transform.localScale = new Vector3(0.12f, 0.025f, 0.12f);
            key.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Collider collider3D = key.GetComponent<Collider>();
            if (collider3D != null) Object.DestroyImmediate(collider3D);
            BoxCollider2D trigger = key.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            key.AddComponent<LevelKeyPickup>();
            ApplyColor(key, new Color(1f, 0.72f, 0.08f));
            FinishCreation(key, "Create Key Collectible", "Default");
        }

        [MenuItem("GameObject/Shadow Cast/Power-up/Double Jump", false, 40)]
        private static void CreateDoubleJumpPowerup() => CreatePowerup(CharacterAbilityType.DoubleJump);

        [MenuItem("GameObject/Shadow Cast/Power-up/Dash", false, 41)]
        private static void CreateDashPowerup() => CreatePowerup(CharacterAbilityType.Dash);

        private static void CreatePowerup(CharacterAbilityType ability)
        {
            GameObject powerup = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            powerup.name = ability == CharacterAbilityType.DoubleJump ? "Double Jump Power-up" : "Dash Power-up";
            powerup.transform.localScale = Vector3.one * 0.16f;
            Collider collider3D = powerup.GetComponent<Collider>();
            if (collider3D != null) Object.DestroyImmediate(collider3D);
            CircleCollider2D trigger = powerup.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            CharacterAbilityPowerup component = powerup.AddComponent<CharacterAbilityPowerup>();
            SerializedObject serialized = new SerializedObject(component);
            serialized.FindProperty("ability").enumValueIndex = (int)ability;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ApplyColor(powerup, ability == CharacterAbilityType.DoubleJump
                ? new Color(0.35f, 0.95f, 0.95f)
                : new Color(0.95f, 0.28f, 0.82f));
            FinishCreation(powerup, $"Create {powerup.name}", "Default");
        }

        private static void CreateMovingShadow(MovingShadowType type)
        {
            GameObject target = new GameObject(type == MovingShadowType.TypeA_FixedAxis
                ? "Moving Shadow A (Fixed Axis)"
                : "Moving Shadow B (Rotation Driven)");
            target.AddComponent<MeshFilter>();
            target.AddComponent<MeshRenderer>();
            target.AddComponent<ArrowCutoutCube>();
            MovingShadowPlatform moving = target.AddComponent<MovingShadowPlatform>();
            SerializedObject serialized = new SerializedObject(moving);
            serialized.FindProperty("movementType").enumValueIndex = (int)type;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            FinishCreation(target, "Create Moving Shadow Platform", "ShadowCaster");
        }

        private static void ApplyColor(GameObject target, Color color)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (renderer == null || shader == null) return;
            Material material = new Material(shader) { name = $"{target.name}_Material", color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            renderer.sharedMaterial = material;
        }

        private static void FinishCreation(GameObject target, string undoName, string layerName)
        {
            Undo.RegisterCreatedObjectUndo(target, undoName);
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) target.layer = layer;
            if (UnityEditor.Selection.activeTransform != null)
            {
                target.transform.SetParent(UnityEditor.Selection.activeTransform, false);
                target.transform.localPosition = Vector3.zero;
            }
            else
            {
                SceneView lastView = SceneView.lastActiveSceneView;
                if (lastView != null) target.transform.position = lastView.pivot;
            }
            UnityEditor.Selection.activeGameObject = target;
            EditorGUIUtility.PingObject(target);
        }
    }
}
