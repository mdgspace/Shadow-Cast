#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Custom Inspector for ShadowWallBaker.
/// Provides a prominent Bake button, platform statistics, and snapshot texture preview.
/// </summary>
[CustomEditor(typeof(ShadowWallBaker))]
public class ShadowWallBakerEditor : Editor
{
    private GUIStyle headerStyle;
    private GUIStyle bakeButtonStyle;
    private GUIStyle clearButtonStyle;
    private GUIStyle statStyle;

    private void InitStyles()
    {
        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
        }

        if (bakeButtonStyle == null)
        {
            bakeButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                fixedHeight = 42
            };
        }

        if (clearButtonStyle == null)
        {
            clearButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fixedHeight = 26
            };
        }

        if (statStyle == null)
        {
            statStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft
            };
        }
    }

    public override void OnInspectorGUI()
    {
        InitStyles();
        ShadowWallBaker baker = (ShadowWallBaker)target;

        serializedObject.Update();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Mixed Reality Shadow Parkour Baker", headerStyle);
        EditorGUILayout.Space(4);

        // --- Big Bake Button ---
        Color originalColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f);

        if (GUILayout.Button("BAKE SHADOW COLLIDERS", bakeButtonStyle))
        {
            Undo.RecordObject(baker, "Bake Shadows");
            baker.Bake();
            EditorUtility.SetDirty(baker);
            if (baker.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(baker.gameObject.scene);
        }

        GUI.backgroundColor = originalColor;

        EditorGUILayout.Space(4);

        // --- Clear Button ---
        if (GUILayout.Button("🗑️ Clear Baked Platforms", clearButtonStyle))
        {
            Undo.RecordObject(baker, "Clear Platforms");
            baker.ClearBakedPlatforms();
            EditorUtility.SetDirty(baker);
            if (baker.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(baker.gameObject.scene);
        }

        EditorGUILayout.Space(6);

        // --- Statistics Summary ---
        if (baker.bakedCrispCount > 0 || baker.bakedSoftCount > 0)
        {
            EditorGUILayout.BeginVertical(statStyle);
            EditorGUILayout.LabelField($"<b>Platforms Generated:</b> {baker.bakedCrispCount + baker.bakedSoftCount}", new GUIStyle(EditorStyles.label) { richText = true });
            EditorGUILayout.LabelField($"  • <b>Crisp (Solid):</b> {baker.bakedCrispCount}", new GUIStyle(EditorStyles.label) { richText = true });
            EditorGUILayout.LabelField($"  • <b>Soft (Quicksand):</b> {baker.bakedSoftCount}", new GUIStyle(EditorStyles.label) { richText = true });
            EditorGUILayout.LabelField($"  • <b>Bake Time:</b> {baker.lastBakeDurationMs:F1} ms", new GUIStyle(EditorStyles.label) { richText = true });
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }

        // --- Default Inspector Properties ---
        DrawDefaultInspector();

        // --- Snapshot Preview ---
        if (baker.lastSnapshot != null)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("<b>Last Greyscale Snapshot Preview:</b>", new GUIStyle(EditorStyles.boldLabel) { richText = true });

            float aspect = (float)baker.lastSnapshot.width / Mathf.Max(1, baker.lastSnapshot.height);
            float previewWidth = EditorGUIUtility.currentViewWidth - 40f;
            float previewHeight = previewWidth / Mathf.Max(0.1f, aspect);

            Rect previewRect = GUILayoutUtility.GetRect(previewWidth, Mathf.Min(previewHeight, 300f));
            EditorGUI.DrawPreviewTexture(previewRect, baker.lastSnapshot, null, ScaleMode.ScaleToFit);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
