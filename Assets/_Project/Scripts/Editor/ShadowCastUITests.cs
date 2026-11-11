#if UNITY_EDITOR
using NUnit.Framework;
using ShadowCast.Core.Campaign;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public sealed class ShadowCastUITests
{
    private GameObject root;
    private Camera camera;
    private Canvas canvas;
    private Button button;
    private ShadowCastUIInteractor input;
    private int clicks;
    private Ray CenterRay => new Ray(Vector3.zero, Vector3.forward);

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("UI Regression Fixture");
        var view = new GameObject("Camera", typeof(Camera)); view.transform.SetParent(root.transform); camera = view.GetComponent<Camera>();
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        panel.transform.SetParent(root.transform, false);
        canvas = panel.GetComponent<Canvas>();
        ShadowCastUIStyle.ConfigureCanvas(canvas, camera, 1600, ShadowCastUIStyle.MenuWidth);
        panel.GetComponent<RectTransform>().sizeDelta = new Vector2(1600, 1000);
        panel.transform.position = Vector3.forward * ShadowCastUIStyle.Distance;
        clicks = 0;
        button = ShadowCastUIStyle.Button("Select", panel.transform, Vector2.zero, new Vector2(400, 120), () => clicks++);
        input = root.AddComponent<ShadowCastUIInteractor>(); input.Initialize(camera); input.RegisterCanvas(canvas);
    }

    [TearDown]
    public void TearDown()
    {
        input.CancelAllPointers();
        Object.DestroyImmediate(root);
    }

    [Test]
    public void HoverHighlightsWithoutASelectionGesture()
    {
        input.ProcessRay(100, CenterRay, false);
        Assert.That(button.GetComponent<ShadowCastUIFeedback>().IsHovered, Is.True);
        Assert.That(clicks, Is.Zero);
    }

    [Test]
    public void PinchOrTriggerRequiresReleaseAndOnlyClicksOnce()
    {
        input.ProcessRay(100, CenterRay, false);
        input.ProcessRay(100, CenterRay, true);
        input.ProcessRay(100, CenterRay, true);
        Assert.That(clicks, Is.Zero);
        input.ProcessRay(100, CenterRay, false);
        input.ProcessRay(100, CenterRay, false);
        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void OffAxisHandRayHitsInWorldSpace()
    {
        Vector3 origin = new Vector3(.5f, -.2f, .4f);
        camera.transform.rotation = Quaternion.Euler(0, 25, 0);
        var ray = new Ray(origin, (button.transform.position - origin).normalized);
        Assert.That(input.TryRaycast(ray, out Button target, out Vector3 point), Is.True);
        Assert.That(target, Is.SameAs(button));
        Assert.That(Vector3.Distance(point, button.transform.position), Is.LessThan(.001f));
    }

    [Test]
    public void TrackingLossCancelsInsteadOfClicking()
    {
        input.ProcessRay(100, CenterRay, true);
        input.CancelPointer(100);
        input.ProcessRay(100, CenterRay, false);
        Assert.That(clicks, Is.Zero);
    }

    [Test]
    public void MovingOffButtonBeforeReleaseCancelsSelection()
    {
        input.ProcessRay(100, CenterRay, true);
        input.ProcessRay(100, new Ray(Vector3.zero, Vector3.right), false);
        Assert.That(clicks, Is.Zero);
        Assert.That(button.GetComponent<ShadowCastUIFeedback>().IsHovered, Is.False);
    }

    [Test]
    public void TwoPointersKeepHighlightUntilBothLeave()
    {
        input.ProcessRay(100, CenterRay, false);
        input.ProcessRay(101, CenterRay, false);
        input.CancelPointer(100);
        Assert.That(button.GetComponent<ShadowCastUIFeedback>().IsHovered, Is.True);
        input.CancelPointer(101);
        Assert.That(button.GetComponent<ShadowCastUIFeedback>().IsHovered, Is.False);
    }

    [Test]
    public void NearPokeHoversThenPressesAndReleases()
    {
        Vector3 point = button.transform.position;
        Assert.That(input.ProcessNearTouch(100, point - Vector3.forward * .04f), Is.True);
        Assert.That(button.GetComponent<ShadowCastUIFeedback>().IsHovered, Is.True);
        input.ProcessNearTouch(100, point - Vector3.forward * .003f);
        Assert.That(clicks, Is.Zero);
        input.ProcessNearTouch(100, point - Vector3.forward * .04f);
        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void HeldControlsStopOnTrackingLoss()
    {
        bool held = false;
        button.gameObject.AddComponent<ShadowCastUIHold>().Changed = value => held = value;
        input.ProcessRay(100, CenterRay, true);
        Assert.That(held, Is.True);
        input.CancelPointer(100);
        Assert.That(held, Is.False);
    }

    [Test]
    public void DisabledButtonCannotBeActivated()
    {
        button.interactable = false;
        input.ProcessRay(100, CenterRay, true);
        input.ProcessRay(100, CenterRay, false);
        Assert.That(clicks, Is.Zero);
    }

    [Test]
    public void CameraAndUIMaterialSupportBothEyes()
    {
        camera.stereoTargetEye = StereoTargetEyeMask.Left;
        camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
        ShadowCastXRInput.ConfigureStereoCamera(camera);
        Assert.That(camera.stereoTargetEye, Is.EqualTo(StereoTargetEyeMask.Both));
        Assert.That(camera.GetUniversalAdditionalCameraData().allowXRRendering, Is.True);
        Assert.That(button.GetComponent<Image>().material.shader.name, Is.EqualTo("ShadowCast/Stereo UI"));
        Assert.That(ShadowCastUIStyle.Material.enableInstancing, Is.True);
    }

    [Test]
    public void NavigationIsLargeDistantAndHasNoFlattenedArtwork()
    {
        Assert.That(ShadowCastUIStyle.Distance, Is.GreaterThanOrEqualTo(3f));
        Assert.That(canvas.GetComponent<RectTransform>().rect.width * canvas.transform.lossyScale.x, Is.GreaterThan(4f));
        Assert.That(canvas.GetComponentsInChildren<RawImage>().Length, Is.Zero);
        Assert.That(((Image)button.targetGraphic).sprite.texture.name, Is.EqualTo("Arrow"));
        Assert.That(button.GetComponent<Image>().color.a, Is.Zero, "Only the Figma icon and label should draw.");
    }

    [Test]
    public void FigmaArtworkButtonsInitializeWithoutInterruptingMenuConstruction()
    {
        Button card = null;
        Assert.DoesNotThrow(() => card = ShadowCastUIStyle.ArtworkButton("StoryCard", "Story", canvas.transform, Vector2.zero, new Vector2(360, 479), null));
        Assert.That(card.targetGraphic, Is.SameAs(card.GetComponent<Image>()));
        Assert.That(card.GetComponent<Image>().sprite, Is.Not.Null);
        Assert.That(card.GetComponent<Image>().color, Is.EqualTo(Color.white));
    }

    [Test]
    public void FigmaIllustrationsNeverDrawTheWholeReferenceScreen()
    {
        foreach (bool chapter in new[] { true, false })
        {
            Sprite art = ShadowCastFigmaAssets.Illustration(chapter, 0);
            Assert.That(art.rect.width, Is.LessThan(art.texture.width * .3f));
            Assert.That(art.rect.height, Is.LessThan(art.texture.height * .3f));
        }
    }
}
#endif
