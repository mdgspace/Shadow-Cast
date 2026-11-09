using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

/// <summary>
/// Discovers mesh objects that can cast shadows and removes a selected object
/// from the shadow-caster layer when requested by an interaction event.
/// </summary>
public class Selection : MonoBehaviour
{
    [Header("Layer discovery")]
    [SerializeField] private string objectLayerName = "ShadowCaster";
    [SerializeField] private string wallLayerName = "ShadowWall";

    [Header("Removed-object feedback")]
    [SerializeField] private Color removedObjectColor = new Color(1f, 0.15f, 0.1f, 1f);
    [SerializeField] private Material removedObjectMaterial;

    [Header("Events")]
    [Tooltip("Raised after the selected object has been removed from the object layer.")]
    [SerializeField] private UnityEvent objectRemoved;

    private readonly List<MeshFilter> discoveredObjects = new List<MeshFilter>();
    private readonly List<Material> generatedRemovedMaterials = new List<Material>();
    private GameObject selectedObject;
    private int objectLayer;
    private int wallLayer;

    public GameObject SelectedObject => selectedObject;
    public IReadOnlyList<MeshFilter> DiscoveredObjects => discoveredObjects;

    private void Awake()
    {
        objectLayer = LayerMask.NameToLayer(objectLayerName);
        wallLayer = LayerMask.NameToLayer(wallLayerName);

        if (objectLayer < 0)
        {
            Debug.LogError($"[Selection] Layer '{objectLayerName}' does not exist.", this);
            return;
        }

        DiscoverObjects();
    }

    /// <summary>
    /// Finds every active mesh in the scene, except meshes on the wall layer,
    /// and places those meshes on the configured object layer.
    /// </summary>
    public void DiscoverObjects()
    {
        discoveredObjects.Clear();

        MeshFilter[] meshFilters = FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            if (meshFilter == null || meshFilter.sharedMesh == null || IsWall(meshFilter.gameObject))
                continue;

            meshFilter.gameObject.layer = objectLayer;
            discoveredObjects.Add(meshFilter);
        }
    }

    /// <summary>
    /// Selects a mesh object. Passing a child object resolves to its nearest
    /// parent MeshFilter so XR and pointer systems can pass their hit object.
    /// </summary>
    public void SelectObject(GameObject target)
    {
        if (target == null)
        {
            selectedObject = null;
            return;
        }

        MeshFilter meshFilter = target.GetComponentInParent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null || IsWall(meshFilter.gameObject))
            return;

        selectedObject = meshFilter.gameObject;
    }

    /// <summary>
    /// Removes the currently selected object from the object layer and gives it
    /// a clear visual state so it no longer appears to be an active caster.
    /// </summary>
    public void RemoveSelectedObject()
    {
        if (selectedObject == null)
            return;

        RemoveObject(selectedObject);
    }

    /// <summary>
    /// UnityEvent-friendly overload for removing a specific object.
    /// </summary>
    public void RemoveObject(GameObject target)
    {
        if (target == null)
            return;

        MeshFilter meshFilter = target.GetComponentInParent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null || IsWall(meshFilter.gameObject))
            return;

        GameObject objectToRemove = meshFilter.gameObject;
        objectToRemove.layer = LayerMask.NameToLayer("Default");
        SetRemovedVisual(objectToRemove);
        discoveredObjects.Remove(meshFilter);

        if (selectedObject == objectToRemove)
            selectedObject = null;

        objectRemoved?.Invoke();
    }

    private bool IsWall(GameObject target)
    {
        return (wallLayer >= 0 && target.layer == wallLayer) || target.name.ToLowerInvariant().Contains("wall");
    }

    private void SetRemovedVisual(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (removedObjectMaterial != null)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    materials[materialIndex] = removedObjectMaterial;
                renderer.sharedMaterials = materials;
                continue;
            }

            Material[] originalMaterials = renderer.sharedMaterials;
            Material[] indicatorMaterials = new Material[originalMaterials.Length];
            for (int materialIndex = 0; materialIndex < originalMaterials.Length; materialIndex++)
            {
                Material indicatorMaterial = CreateIndicatorMaterial(originalMaterials[materialIndex]);
                indicatorMaterials[materialIndex] = indicatorMaterial;
                if (indicatorMaterial != null)
                    generatedRemovedMaterials.Add(indicatorMaterial);
            }

            renderer.sharedMaterials = indicatorMaterials;
        }
    }

    private Material CreateIndicatorMaterial(Material sourceMaterial)
    {
        Shader shader = sourceMaterial != null ? sourceMaterial.shader : Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            Debug.LogWarning("[Selection] No shader was found for the removed-object indicator.", this);
            return null;
        }

        Material material = new Material(shader)
        {
            name = "Selection_RemovedIndicator"
        };

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", removedObjectColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", removedObjectColor);
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", removedObjectColor);
        }

        return material;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < generatedRemovedMaterials.Count; i++)
        {
            if (generatedRemovedMaterials[i] != null)
                Destroy(generatedRemovedMaterials[i]);
        }

        generatedRemovedMaterials.Clear();
    }
}
