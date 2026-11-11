#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using ShadowCast.Core.BuildMode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.OpenXR.Features.Interactions;

public sealed class ShadowCastRegressionTests
{
    [Test]
    public void ConfiguringBoundsDoesNotMoveTheCampaignOrItsWall()
    {
        var root = new GameObject("Campaign Test");
        var surface = new GameObject("Wall Test");
        try
        {
            root.transform.position = new Vector3(4f, 2f, -3f);
            surface.transform.SetParent(root.transform, false);
            surface.transform.SetPositionAndRotation(new Vector3(1f, 1.5f, 5f), Quaternion.Euler(0, 63, 0));
            var manager = root.AddComponent<BoundingVolumeManager>();
            Vector3 rootBefore = root.transform.position;
            Vector3 wallBefore = surface.transform.position;
            manager.ConfigureForWall(surface.transform, 3.2f, 2.1f);
            Assert.That(Vector3.Distance(root.transform.position, rootBefore), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(surface.transform.position, wallBefore), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(manager.Center, wallBefore), Is.LessThan(.0001f));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void RotatedBoundsKeepTheWholeObjectInside()
    {
        var root = new GameObject("Bounds Test");
        var surface = new GameObject("Wall Test");
        try
        {
            surface.transform.rotation = Quaternion.Euler(0, 45, 0);
            var manager = root.AddComponent<BoundingVolumeManager>();
            manager.ConfigureForWall(surface.transform, 3f, 2f);
            Vector3 extents = new Vector3(.3f, .2f, .3f);
            Vector3 clamped = manager.ClampPosition(surface.transform.TransformPoint(new Vector3(10, 0, 0)), extents);
            Vector3 local = surface.transform.InverseTransformPoint(clamped);
            Assert.That(local.x + Mathf.Sqrt(2) * .3f, Is.LessThanOrEqualTo(2.0001f));
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(surface); }
    }

    [TestCase(64)]
    [TestCase(512)]
    [TestCase(780)]
    public void EdgeTouchingShadowsProduceCompletePlatforms(int size)
    {
        var grid = new float[size, size];
        for (int y = 0; y < size / 4; y++)
            for (int x = 0; x < size / 4; x++) grid[x, y] = 1;
        for (int y = size / 2; y < size * 3 / 4; y++)
            for (int x = size * 3 / 4; x < size; x++) grid[x, y] = 1;
        ShadowContourExtractor.ExtractAllShadowContours(grid, size, size, .65f, .2f, 10, .003f,
            new Bounds(Vector3.zero, Vector3.one), false, out var crisp, out var soft);
        Assert.That(crisp.Count, Is.EqualTo(2), "Clipped shadows must close at both wall edges.");
        foreach (var shape in crisp) Assert.That(shape.area, Is.GreaterThan(.05f), "A whole platform must not collapse into small fragments.");
        Assert.That(soft.Count, Is.EqualTo(0));
    }

    [Test]
    public void OrdinarySceneryCannotBeGrabbed()
    {
        var root = new GameObject("Manipulator Test");
        var scenery = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            var manipulator = root.AddComponent<ObjectManipulator>();
            manipulator.ConfigureAnchors(root.transform, null, null);
            Assert.That(manipulator.BeginPinchGrab(scenery), Is.False);
            Assert.That(manipulator.IsGrabbing, Is.False);
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(scenery); }
    }

    [Test]
    public void UndoAfterAnObjectIsDestroyedDoesNotThrow()
    {
        var item = new GameObject("Deleted Test Object");
        var commands = new IBuildCommand[]
        {
            new SpawnCommand(item), new DeleteCommand(item),
            new TransformCommand(item.transform, Vector3.zero, Quaternion.identity, Vector3.one,
                Vector3.one, Quaternion.identity, Vector3.one)
        };
        Object.DestroyImmediate(item);
        foreach (var command in commands)
            Assert.DoesNotThrow(() => { command.Undo(); command.Redo(); string description = command.Description; });
    }

    [Test]
    public void LinkInputAssetIncludesHeadAndBothControllerBindings()
    {
        InputActionAsset asset = Resources.Load<InputActionAsset>("ShadowCastXR");
        Assert.That(asset, Is.Not.Null);
        foreach (string name in new[] { "HeadPosition", "HeadRotation", "HeadTrackingState", "LeftPosition", "RightPosition", "Grab", "Select", "Move", "Jump", "Dash", "Menu" })
            Assert.That(asset.FindAction("XR/" + name, true).bindings.Count, Is.GreaterThan(0), name);
    }

    [Test]
    public void RealTouchLayoutResolvesControllerButtonsAndTracking()
    {
        var left = InputSystem.AddDevice<OculusTouchControllerProfile.OculusTouchController>();
        var right = InputSystem.AddDevice<OculusTouchControllerProfile.OculusTouchController>();
        var asset = Object.Instantiate(Resources.Load<InputActionAsset>("ShadowCastXR"));
        try
        {
            InputSystem.SetDeviceUsage(left, CommonUsages.LeftHand);
            InputSystem.SetDeviceUsage(right, CommonUsages.RightHand);
            asset.Enable();
            foreach (var expected in new Dictionary<string, InputControl>
            {
                { "Grab", right.gripPressed }, { "Select", right.triggerPressed },
                { "Jump", right.primaryButton }, { "Dash", right.secondaryButton },
                { "RightTracked", right.isTracked }, { "Menu", left.menu },
                { "Redo", left.thumbstickClicked }, { "Depth", left.triggerPressed },
                { "Move", left.thumbstick }, { "RightPointerRotation", right.pointerRotation }
            })
                Assert.That(asset.FindAction("XR/" + expected.Key).controls, Does.Contain(expected.Value), expected.Key);
        }
        finally
        {
            asset.Disable();
            Object.DestroyImmediate(asset);
            InputSystem.RemoveDevice(left);
            InputSystem.RemoveDevice(right);
        }
    }
}
#endif
