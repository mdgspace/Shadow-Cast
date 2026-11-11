using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Handedness = Oculus.Interaction.Input.Handedness;

namespace ShadowCast.Core.Campaign
{
    /// <summary>World-space UI input, independent of either eye's screen projection.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class ShadowCastUIInteractor : MonoBehaviour
    {
        private sealed class Pointer
        {
            public Button hover, press;
            public bool down, mustRelease;
            public PointerEventData data;
            public LineRenderer line;
            public Transform cursor;
        }
        private readonly List<Canvas> canvases = new List<Canvas>();
        private readonly List<Button> buttons = new List<Button>();
        private readonly List<Button> canvasButtons = new List<Button>();
        private readonly Dictionary<int, Pointer> pointers = new Dictionary<int, Pointer>();
        private Camera view;
        public Canvas ModalCanvas { get; set; }
        public bool IsPointingAtUI { get; private set; }

        public void Initialize(Camera camera) => view = camera;
        public void RegisterCanvas(Canvas canvas) { if (canvas != null && !canvases.Contains(canvas)) canvases.Add(canvas); RefreshTargets(); }
        public void RefreshTargets()
        {
            buttons.Clear();
            foreach (Canvas canvas in canvases)
            {
                if (canvas == null) continue;
                canvas.GetComponentsInChildren(true, canvasButtons);
                buttons.AddRange(canvasButtons);
            }
        }

        private Pointer GetPointer(int id)
        {
            if (pointers.TryGetValue(id, out Pointer pointer)) return pointer;
            pointer = new Pointer { data = new PointerEventData(EventSystem.current) { pointerId = id, button = PointerEventData.InputButton.Left } };
            pointers.Add(id, pointer);
            return pointer;
        }

        private void Update()
        {
            if (view == null || EventSystem.current == null) return;
            IsPointingAtUI = false;
            SampleHand(Handedness.Left, -101);
            SampleHand(Handedness.Right, -102);
            SampleController(true, -103);
            SampleController(false, -104);
        }

        private void SampleHand(Handedness side, int id)
        {
            Pointer pointer = GetPointer(id);
            bool pinch = pointer.down ? ShadowCastXRInput.IndexPinchStrength(side) > .35f : ShadowCastXRInput.IndexPinchStrength(side) >= .75f;
            if (ShadowCastXRInput.TryIndexTip(side, out Pose tip) && ProcessNearTouch(id, tip.position, pinch)) return;
            if (ShadowCastXRInput.TryHandPointer(side, out Pose aim))
                ProcessRay(id, new Ray(aim.position, aim.rotation * Vector3.forward), pinch);
            else CancelPointer(id);
        }

        private void SampleController(bool left, int id)
        {
            if (ShadowCastXRInput.TryControllerPointer(left, out Pose aim))
                ProcessRay(id, new Ray(aim.position, aim.rotation * Vector3.forward), ShadowCastXRInput.RawButton(left ? "LeftSelect" : "Select"));
            else CancelPointer(id);
        }

        public bool TryRaycast(Ray ray, out Button target, out Vector3 point)
        {
            target = null;
            point = ray.GetPoint(ShadowCastUIStyle.Distance);
            float closest = float.PositiveInfinity;
            int order = int.MinValue;
            foreach (Button button in buttons)
            {
                if (!CanTarget(button)) continue;
                RectTransform rect = button.transform as RectTransform;
                float denominator = Vector3.Dot(rect.forward, ray.direction);
                if (Mathf.Abs(denominator) < .00001f) continue;
                float distance = Vector3.Dot(rect.position - ray.origin, rect.forward) / denominator;
                if (distance <= 0 || distance > 12) continue;
                Vector3 candidate = ray.GetPoint(distance);
                if (!rect.rect.Contains(rect.InverseTransformPoint(candidate))) continue;
                int sorting = button.GetComponentInParent<Canvas>().sortingOrder;
                if (sorting < order || (sorting == order && distance >= closest)) continue;
                order = sorting;
                closest = distance;
                target = button;
                point = candidate;
            }
            return target != null;
        }

        public void ProcessRay(int id, Ray ray, bool pressed)
        {
            TryRaycast(ray, out Button target, out Vector3 point);
            Dispatch(id, target, point, pressed);
            Draw(GetPointer(id), ray.origin, point, true, target != null);
        }

        public bool ProcessNearTouch(int id, Vector3 tip, bool pinching = false)
        {
            if (!TryNearTarget(tip, out Button near, out Vector3 point, out float distance)) return false;
            Pointer pointer = GetPointer(id);
            bool touching = pointer.down ? distance >= -.025f : distance >= -.006f;
            Dispatch(id, near, point, touching || pinching);
            Draw(pointer, tip, point, false, true);
            return true;
        }

        private bool TryNearTarget(Vector3 tip, out Button target, out Vector3 point, out float signedDistance)
        {
            target = null; point = tip; signedDistance = 0;
            float closest = .11f;
            foreach (Button button in buttons)
            {
                if (!CanTarget(button)) continue;
                RectTransform rect = button.transform as RectTransform;
                float distance = Vector3.Dot(tip - rect.position, rect.forward);
                if (distance < -.10f || distance > .035f || Mathf.Abs(distance) >= closest) continue;
                Vector3 projected = tip - rect.forward * distance;
                if (!rect.rect.Contains(rect.InverseTransformPoint(projected))) continue;
                closest = Mathf.Abs(distance); target = button; point = projected; signedDistance = distance;
            }
            return target != null;
        }

        private bool CanTarget(Button button) => button != null && button.isActiveAndEnabled && button.IsInteractable() &&
            (ModalCanvas == null || button.transform.IsChildOf(ModalCanvas.transform));

        private void Dispatch(int id, Button target, Vector3 point, bool down)
        {
            Pointer pointer = GetPointer(id);
            PointerEventData data = pointer.data;
            if (pointer.mustRelease) { if (!down) pointer.mustRelease = false; else down = false; }
            if (pointer.hover != target)
            {
                if (pointer.hover != null) ExecuteEvents.Execute(pointer.hover.gameObject, data, ExecuteEvents.pointerExitHandler);
                pointer.hover = target;
                if (target != null) ExecuteEvents.Execute(target.gameObject, data, ExecuteEvents.pointerEnterHandler);
            }
            IsPointingAtUI |= target != null;
            data.position = view != null ? (Vector2)view.WorldToScreenPoint(point) : Vector2.zero;
            data.pointerCurrentRaycast = new RaycastResult { gameObject = target != null ? target.gameObject : null, worldPosition = point };
            if (down && !pointer.down)
            {
                pointer.press = target;
                if (target != null)
                {
                    data.pointerPress = target.gameObject;
                    data.pointerPressRaycast = data.pointerCurrentRaycast;
                    ExecuteEvents.Execute(target.gameObject, data, ExecuteEvents.pointerDownHandler);
                }
            }
            else if (!down && pointer.down)
            {
                Button pressed = pointer.press;
                pointer.press = null;
                if (pressed != null)
                {
                    ExecuteEvents.Execute(pressed.gameObject, data, ExecuteEvents.pointerUpHandler);
                    if (pressed == target && CanTarget(pressed)) ExecuteEvents.Execute(pressed.gameObject, data, ExecuteEvents.pointerClickHandler);
                }
                data.pointerPress = null;
            }
            pointer.down = down;
        }

        public void CancelPointer(int id)
        {
            if (!pointers.TryGetValue(id, out Pointer pointer)) return;
            // Losing tracking or switching screens cancels a press; it never clicks.
            if (pointer.press != null) ExecuteEvents.Execute(pointer.press.gameObject, pointer.data, ExecuteEvents.pointerUpHandler);
            if (pointer.hover != null) ExecuteEvents.Execute(pointer.hover.gameObject, pointer.data, ExecuteEvents.pointerExitHandler);
            pointer.press = pointer.hover = null;
            pointer.down = false;
            pointer.mustRelease = true;
            if (pointer.line != null) pointer.line.enabled = false;
            if (pointer.cursor != null) pointer.cursor.gameObject.SetActive(false);
        }

        public void CancelAllPointers() { foreach (int id in pointers.Keys) CancelPointer(id); }

        private void Draw(Pointer pointer, Vector3 origin, Vector3 point, bool ray, bool hit)
        {
            if (pointer.line == null)
            {
                var visual = new GameObject("UI Aim Ray"); visual.transform.SetParent(transform, false);
                pointer.line = visual.AddComponent<LineRenderer>();
                pointer.line.sharedMaterial = ShadowCastUIStyle.Material;
                pointer.line.positionCount = 2;
                pointer.line.startWidth = .003f; pointer.line.endWidth = .006f;
                var dot = new GameObject("UI Target", typeof(RectTransform), typeof(Canvas), typeof(Image));
                dot.transform.SetParent(transform, false);
                var canvas = dot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = view; canvas.sortingOrder = 2000;
                var image = dot.GetComponent<Image>(); image.material = ShadowCastUIStyle.Material; image.raycastTarget = false;
                image.color = ShadowCastUIStyle.Accent;
                dot.GetComponent<RectTransform>().sizeDelta = Vector2.one;
                pointer.cursor = dot.transform;
            }
            Color color = hit ? ShadowCastUIStyle.Accent : new Color(.5f, .75f, .8f, .45f);
            pointer.line.enabled = ray;
            pointer.line.startColor = pointer.line.endColor = color;
            pointer.line.SetPosition(0, origin); pointer.line.SetPosition(1, point);
            pointer.cursor.gameObject.SetActive(true);
            pointer.cursor.SetPositionAndRotation(point, view.transform.rotation);
            pointer.cursor.localScale = Vector3.one * Mathf.Max(.012f, Vector3.Distance(view.transform.position, point) * .006f);
        }

        private void OnDisable() => CancelAllPointers();
    }
}
