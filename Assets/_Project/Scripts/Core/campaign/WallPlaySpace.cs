using System.Collections.Generic;
using UnityEngine;

namespace ShadowCast.Core.Campaign
{
    /// <summary>
    /// Runs Physics2D in an unrotated XY plane, then presents its meshes on the
    /// placed wall. Physics2D cannot simulate a wall rotated around world Y.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class WallPlaySpace : MonoBehaviour
    {
        private struct MovedObject
        {
            public Transform target, parent;
            public Vector3 position, scale;
            public Quaternion rotation;
        }
        private sealed class Visual
        {
            public Renderer source, copy;
            public MeshFilter sourceMesh, copyMesh;
            public LineRenderer sourceLine, copyLine;
            public bool originalForceOff;
        }
        private readonly List<MovedObject> moved = new List<MovedObject>();
        private readonly List<Visual> visuals = new List<Visual>();
        private Transform wall, stage, presentation;
        public Transform PhysicsWall => stage;

        public void Initialize(Transform placedWall)
        {
            wall = placedWall;
            stage = new GameObject("Campaign XY Physics").transform;
            stage.position = new Vector3(0f, -1000f, 0f);
            stage.localScale = wall.lossyScale;
            presentation = new GameObject("Campaign Wall Presentation").transform;
            presentation.SetParent(wall, false);
        }

        public Vector3 ToPhysics(Vector3 world) => stage.TransformPoint(wall.InverseTransformPoint(world));

        public void MoveToPhysics(Transform target)
        {
            if (target == null) return;
            moved.Add(new MovedObject { target = target, parent = target.parent,
                position = target.localPosition, rotation = target.localRotation, scale = target.localScale });
            Vector3 localPosition = wall.InverseTransformPoint(target.position);
            Quaternion localRotation = Quaternion.Inverse(wall.rotation) * target.rotation;
            Vector3 scale = target.lossyScale;
            target.SetParent(stage, false);
            target.localPosition = localPosition;
            target.localRotation = localRotation;
            target.localScale = Divide(scale, wall.lossyScale);
            AddVisuals(target);
        }

        public void AddCharacter(Transform target)
        {
            target.SetParent(stage, true);
            AddVisuals(target);
        }

        private void AddVisuals(Transform root)
        {
            foreach (Renderer source in root.GetComponentsInChildren<Renderer>(true))
            {
                var visual = new Visual { source = source, originalForceOff = source.forceRenderingOff };
                GameObject copy = new GameObject(source.name + " (Wall Visual)");
                copy.transform.SetParent(presentation, false);
                if (source is LineRenderer line)
                {
                    visual.sourceLine = line;
                    visual.copyLine = copy.AddComponent<LineRenderer>();
                    visual.copyLine.useWorldSpace = false;
                    visual.copyLine.loop = line.loop;
                    visual.copyLine.widthCurve = line.widthCurve;
                    visual.copyLine.widthMultiplier = line.widthMultiplier;
                    visual.copyLine.colorGradient = line.colorGradient;
                    visual.copyLine.numCornerVertices = line.numCornerVertices;
                    visual.copyLine.numCapVertices = line.numCapVertices;
                    visual.copy = visual.copyLine;
                }
                else if (source.TryGetComponent(out MeshFilter filter))
                {
                    visual.sourceMesh = filter;
                    visual.copyMesh = copy.AddComponent<MeshFilter>();
                    visual.copy = copy.AddComponent<MeshRenderer>();
                }
                else { Destroy(copy); continue; }
                visual.copy.sharedMaterials = source.sharedMaterials;
                visual.copy.sortingOrder = source.sortingOrder;
                visual.copy.sortingLayerID = source.sortingLayerID;
                visual.copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                source.forceRenderingOff = true;
                visuals.Add(visual);
            }
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (stage == null || wall == null) return;
            foreach (Visual visual in visuals)
            {
                if (visual.source == null) { visual.copy.enabled = false; continue; }
                visual.copy.enabled = visual.source.enabled && visual.source.gameObject.activeInHierarchy;
                Transform original = visual.source.transform;
                Transform copy = visual.copy.transform;
                copy.localPosition = stage.InverseTransformPoint(original.position);
                copy.localRotation = Quaternion.Inverse(stage.rotation) * original.rotation;
                copy.localScale = Divide(original.lossyScale, stage.lossyScale);
                if (visual.copyMesh != null) visual.copyMesh.sharedMesh = visual.sourceMesh.sharedMesh;
                if (visual.copyLine == null) continue;
                LineRenderer line = visual.sourceLine;
                visual.copyLine.positionCount = line.positionCount;
                for (int i = 0; i < line.positionCount; i++)
                {
                    Vector3 point = line.GetPosition(i);
                    visual.copyLine.SetPosition(i, line.useWorldSpace ? original.InverseTransformPoint(point) : point);
                }
            }
        }

        private static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);

        public void Restore()
        {
            foreach (Visual visual in visuals)
                if (visual.source != null) visual.source.forceRenderingOff = visual.originalForceOff;
            visuals.Clear();
            foreach (MovedObject item in moved)
            {
                if (item.target == null || item.parent == null) continue;
                item.target.SetParent(item.parent, false);
                item.target.localPosition = item.position;
                item.target.localRotation = item.rotation;
                item.target.localScale = item.scale;
            }
            moved.Clear();
            if (stage != null) { stage.gameObject.SetActive(false); Destroy(stage.gameObject); }
            if (presentation != null) { presentation.gameObject.SetActive(false); Destroy(presentation.gameObject); }
            stage = presentation = null;
        }

        private void OnDestroy()
        {
            // During scene teardown the former parents may already be destroying.
            // Dispose the detached stage without reparenting into a dying hierarchy.
            moved.Clear();
            Restore();
        }
    }
}
