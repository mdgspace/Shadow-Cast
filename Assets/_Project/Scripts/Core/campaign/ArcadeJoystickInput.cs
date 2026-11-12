using UnityEngine;
using UnityEngine.EventSystems;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Pointer-driven, single-axis joystick input for the imported Figma arcade control artwork.</summary>
    public sealed class ArcadeJoystickInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform joystickArea;
        [SerializeField] private RectTransform handle;
        [SerializeField] private ArcadeHandControls controls;
        [SerializeField, Range(0.1f, 1f)] private float deadZone = 0.14f;
        [SerializeField, Range(0.2f, 1f)] private float handleTravel = 0.55f;

        private Vector2 handleHome;

        private void Awake()
        {
            if (joystickArea == null) joystickArea = transform as RectTransform;
            if (handle != null) handleHome = handle.anchoredPosition;
        }

        public void OnPointerDown(PointerEventData eventData) => OnDrag(eventData);

        public void OnDrag(PointerEventData eventData)
        {
            if (joystickArea == null || controls == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(joystickArea, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
                return;

            float radius = Mathf.Min(joystickArea.rect.width, joystickArea.rect.height) * 0.5f;
            float horizontal = radius > 0f ? Mathf.Clamp(localPoint.x / radius, -1f, 1f) : 0f;
            float magnitude = Mathf.Abs(horizontal);
            float output = magnitude <= deadZone ? 0f : Mathf.Sign(horizontal) * Mathf.InverseLerp(deadZone, 1f, magnitude);
            controls.SetMovementInput(output);

            if (handle != null)
                handle.anchoredPosition = handleHome + new Vector2(horizontal * radius * handleTravel, 0f);
        }

        public void OnPointerUp(PointerEventData eventData) => ResetInput();

        private void OnDisable() => ResetInput();

        private void ResetInput()
        {
            controls?.ReleaseControls();
            if (handle != null) handle.anchoredPosition = handleHome;
        }
    }
}
