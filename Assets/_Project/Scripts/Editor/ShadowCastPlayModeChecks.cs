#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using ShadowCast.Core.BuildMode;
using ShadowCast.Core.Campaign;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR.OpenXR.Features.Interactions;
using Object = UnityEngine.Object;

/// <summary>Exercises the real campaign without a headset. Results go to Logs/VR-smoke-checks.json.</summary>
public static class ShadowCastPlayModeChecks
{
    [Serializable] private sealed class Report
    {
        public string status;
        public List<string> passed = new List<string>();
        public string failure;
        public float bakeMilliseconds;
    }
    private static Report report;
    private static CampaignFlowController flow;
    private static BuildModeManager build;
    private static ShadowCastHeadsetUI ui;
    private static CampaignGameplayHUD hud;
    private static CampaignLevelRuntime runtime;
    private static int phase;
    private static double deadline, phaseStarted;
    private static Vector2 start;
    private static float gameTime;
    private static bool controllerChecked;
    private static string controllerFailure;

    [MenuItem("Tools/Shadow Cast/Run Play Mode Smoke Checks")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode before running smoke checks.");
        EditorApplication.update -= Tick;
        report = new Report { status = "running" };
        flow = Object.FindAnyObjectByType<CampaignFlowController>();
        build = Object.FindAnyObjectByType<BuildModeManager>();
        runtime = Object.FindAnyObjectByType<CampaignLevelRuntime>();
        ui = Object.FindAnyObjectByType<ShadowCastHeadsetUI>();
        hud = Object.FindAnyObjectByType<CampaignGameplayHUD>();
        if (flow == null || build == null || runtime == null) throw new InvalidOperationException("Campaign startup failed.");
        EditorApplication.isPaused = false;
        Time.timeScale = 1f;
        ui.enabled = false; // Drive the character deterministically below.
        controllerChecked = false;
        controllerFailure = null;
        InputSystem.onAfterUpdate -= CheckControllerOnInputUpdate;
        InputSystem.onAfterUpdate += CheckControllerOnInputUpdate;
        flow.ReturnToMainMenu();
        flow.OpenChapterSelect();
        Check(flow.SelectChapter(0) && flow.SelectLevel(0), "Campaign navigation");
        Transform view = Camera.main.transform;
        Vector3 direction = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
        flow.ConfirmWallPlacement(new Pose(view.position + direction * 2.8f, Quaternion.LookRotation(direction)));
        Check(flow.CurrentScreen == CampaignScreen.Build && GameObject.Find("LevelGoal").GetComponent<BoxCollider2D>() != null,
            "Wall placement and exit trigger creation");
        Check(Vector3.Distance(runtime.CharacterStartWorldPosition,
            runtime.Wall.position + runtime.Wall.rotation * (flow.SelectedLevel.CharacterStartWallLocalPosition + new Vector3(0, 0, -.02f))) < .001f,
            "Authored positions use metres");
        flow.BakeLevel();
        phase = 0;
        deadline = EditorApplication.timeSinceStartup + 25;
        EditorApplication.update += Tick;
        Save();
    }

    private static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying) { Finish("Play Mode stopped before completion."); return; }
            if (controllerFailure != null) throw new Exception(controllerFailure);
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timed out at phase " + phase);
            var character = Object.FindAnyObjectByType<WallPlatformerController2D>();
            switch (phase)
            {
                case 0:
                    if (!controllerChecked) return;
                    if (flow.CurrentScreen == CampaignScreen.Baking) return;
                    Check(flow.CurrentScreen == CampaignScreen.Play && character != null, "Bake transitions into Play");
                    Check(runtime.Wall.GetComponent<ShadowWallBaker>().bakedCrispCount >= 3, "Playable solid shadow platforms");
                    Check(Object.FindAnyObjectByType<WallPlaySpace>().PhysicsWall.rotation == Quaternion.identity,
                        "Rotated VR wall uses an XY physics plane");
                    report.bakeMilliseconds = runtime.Wall.GetComponent<ShadowWallBaker>().lastBakeDurationMs;
                    gameTime = Time.time;
                    phase++;
                    break;
                case 1:
                    if (Time.time - gameTime < 1.5f) return;
                    Check(character != null && flow.CurrentScreen == CampaignScreen.Play, "Character remains on starting platform");
                    start = character.GetComponent<SoftBlob>().Centroid;
                    character.RequestJump();
                    gameTime = Time.time;
                    phase++;
                    break;
                case 2:
                    if (Time.time - gameTime < .15f) return;
                    Check(character != null && character.GetComponent<SoftBlob>().Centroid.y > start.y + .15f,
                        "Jump lifts the complete soft body");
                    character.SetMovementInput(1f);
                    start = character.GetComponent<SoftBlob>().Centroid;
                    gameTime = Time.time;
                    phase++;
                    break;
                case 3:
                    if (Time.time - gameTime < .15f) return;
                    Check(character != null && character.GetComponent<SoftBlob>().Centroid.x > start.x + .02f,
                        "Movement translates the complete soft body");
                    character.SetMovementInput(0f);
                    hud.TogglePause();
                    Check(hud.IsPaused && Time.timeScale == 0, "Pause stops simulation");
                    gameTime = Time.time;
                    phaseStarted = EditorApplication.timeSinceStartup;
                    phase++;
                    break;
                case 4:
                    if (EditorApplication.timeSinceStartup - phaseStarted < .25) return;
                    Check(Mathf.Approximately(Time.time, gameTime), "Paused simulation remains stopped");
                    hud.Resume();
                    flow.ReturnToBuild();
                    phaseStarted = EditorApplication.timeSinceStartup;
                    phase++;
                    break;
                case 5:
                    if (EditorApplication.timeSinceStartup - phaseStarted < .15) return;
                    Check(flow.CurrentScreen == CampaignScreen.Build && build.BakePipeline.CurrentState == BakeState.Idle &&
                        Object.FindAnyObjectByType<WallPlaySpace>() == null, "Return to Build restores the wall");
                    var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    build.SpawnObject(template, runtime.Wall.position - runtime.Wall.forward);
                    Object.Destroy(template);
                    Check(build.ActiveObjects.Count == 5 && build.UndoRedoManager.CanUndo, "Palette spawning records history");
                    flow.RetryLevel();
                    phaseStarted = EditorApplication.timeSinceStartup;
                    phase++;
                    break;
                case 6:
                    if (EditorApplication.timeSinceStartup - phaseStarted < .15) return;
                    Check(build.ActiveObjects.Count == 4 && !build.UndoRedoManager.CanUndo, "Retry clears palette objects and undo history");
                    flow.BakeLevel();
                    flow.BackToLevelSelect();
                    phaseStarted = EditorApplication.timeSinceStartup;
                    phase++;
                    break;
                case 7:
                    if (EditorApplication.timeSinceStartup - phaseStarted < .25) return;
                    Check(flow.CurrentScreen == CampaignScreen.LevelSelect && build.BakePipeline.CurrentState == BakeState.Idle,
                        "Leaving during bake cancels the pending transition");
                    Finish(null);
                    break;
            }
        }
        catch (Exception exception) { Finish(exception.Message); }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        report.passed.Add(description);
    }

    private static void VerifyControllerEvents()
    {
        var left = InputSystem.AddDevice<OculusTouchControllerProfile.OculusTouchController>();
        var right = InputSystem.AddDevice<OculusTouchControllerProfile.OculusTouchController>();
        try
        {
            InputSystem.SetDeviceUsage(left, CommonUsages.LeftHand);
            InputSystem.SetDeviceUsage(right, CommonUsages.RightHand);
            using (StateEvent.From(right, out var input))
            {
                right.gripPressed.WriteValueIntoEvent(1f, input);
                right.triggerPressed.WriteValueIntoEvent(1f, input);
                right.primaryButton.WriteValueIntoEvent(1f, input);
                right.secondaryButton.WriteValueIntoEvent(1f, input);
                right.isTracked.WriteValueIntoEvent(1f, input);
                InputState.Change(right, input, InputUpdateType.Dynamic);
            }
            using (StateEvent.From(left, out var input))
            {
                left.menu.WriteValueIntoEvent(1f, input);
                left.thumbstickClicked.WriteValueIntoEvent(1f, input);
                left.triggerPressed.WriteValueIntoEvent(1f, input);
                left.thumbstick.WriteValueIntoEvent(new Vector2(.8f, 0), input);
                InputState.Change(left, input, InputUpdateType.Dynamic);
            }
            foreach (string name in new[] { "Grab", "Select", "Jump", "Dash", "RightTracked", "Menu", "Redo", "Depth" })
                if (!ShadowCastXRInput.RawButton(name)) throw new Exception("Controller event did not reach " + name);
            Check(ShadowCastXRInput.Move > .5f, "Synthetic Oculus Touch events reach gameplay input");
        }
        finally { InputSystem.RemoveDevice(left); InputSystem.RemoveDevice(right); }
    }

    private static void CheckControllerOnInputUpdate()
    {
        // Editor callbacks read a separate input-state buffer. Verify gameplay
        // actions while the Input System's Dynamic buffer is actually current.
        if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        InputSystem.onAfterUpdate -= CheckControllerOnInputUpdate;
        try { VerifyControllerEvents(); }
        catch (Exception exception) { controllerFailure = exception.Message; }
        controllerChecked = true;
    }
    private static void Save()
    {
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/VR-smoke-checks.json", JsonUtility.ToJson(report, true));
    }
    private static void Finish(string failure)
    {
        EditorApplication.update -= Tick;
        InputSystem.onAfterUpdate -= CheckControllerOnInputUpdate;
        if (hud != null && hud.IsPaused) hud.Resume();
        if (ui != null) ui.enabled = true;
        if (flow != null) flow.ReturnToMainMenu();
        report.status = failure == null ? "passed" : "failed";
        report.failure = failure;
        Save();
        if (failure == null) Debug.Log("[Shadow Cast] Play Mode smoke checks passed.");
        else Debug.LogError("[Shadow Cast] Smoke check failed: " + failure);
    }
}
#endif
