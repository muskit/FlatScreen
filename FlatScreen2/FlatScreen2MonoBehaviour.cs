﻿
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using VTOLVR.Multiplayer;

using Triquetra.FlatScreen.TrackIR;

namespace Triquetra.FlatScreen2
{
    public class FlatScreen2MonoBehaviour : MonoBehaviour
    {
        // https://vtolvr-mods.com/viewbugs/zj7ylyrf/
        // TODO?: Custom cursors
        // TODO?: WASDEQ controls
        // TODO?: Bobblehead gets a VRInteractable

        public static FlatScreen2MonoBehaviour instance { get; private set; }

        public bool flatScreenEnabled { get; private set; } = false;

        public TrackIRTransformer trackIRTransformer { get; private set; }

        // tick loop tracking
        const int FRAMES_PER_TICK = 60;
        const int FRAMES_PER_SUBTICK = 5;
        int frameTick = 0;

        // UI: main window
        private bool showMainWindow = true;
        private Rect mainWindowRect = new Rect(25, 25, 350, 550);
        private Vector2 mainWindowScroll;

        // UI: EndMission
        private bool showEndMissionWindow = false;
        private bool endMissionWindowAutoShown = false;
        private Rect endMissionWindowRect = new Rect(Screen.width / 2 - 300, Screen.height / 2 - 250, 600, 500);
        private Vector2 endMisWinLogScroll = Vector2.zero;
        private EndMission endMission; // is always populated in a flying scene

        // VRInteractable tracks
        public VRInteractable targetedVRInteractable;
        public VRInteractable heldVRInteractable;
        public IEnumerable<VRInteractable> vrInteractables = new List<VRInteractable>();

        // camera looking preferences
        private float rotLimX = 160f; // set to -1 to disable
        private float rotLimY = 89f; // set to -1 to disable
        public bool LimitXRotation
        {
            get { return rotLimX >= 0; }
            set { rotLimX = value ? 160f : -1f; }
        }
        public bool LimitYRotation
        {
            get { return rotLimY >= 0; }
            set { rotLimY = value ? 89f : -1f; }
        }

        // current FOV
        public const int DEFAULT_FOV = 60;
        int currentFOV = DEFAULT_FOV;

        // camera tracks
        private GameObject cameraEyeGameObject;
        private GameObject cameraHMDGameObject;
        private GameObject cameraHelmetGameObject;
        private Vector2 cameraRotation = Vector2.zero;

        // player avatar
        private GameObject playerBody = null;
        private GameObject playerLeftHand = null;
        private GameObject playerRightHand = null;

        // cursor hiding
        const int CURSOR_HIDE_AFTER = 5;
        private float cursorHideTimer = CURSOR_HIDE_AFTER;

        private bool cursorOverWindow = false;

        private bool viewIsSpec = false;

        /// <summary>
        /// Check if the mission has ended. Also populates this.endMission if it's null.
        /// </summary>
        /// <returns></returns>
        public bool CheckMissionEnded()
        {
            if (endMission == null)
                endMission = GameObject.FindObjectOfType<EndMission>(false);
            if (endMission == null)
                return false;

            return !endMission.inProgressObject.activeSelf;
        }

        public FlatScreen2MonoBehaviour()
        {
            if (instance != null)
            {
                Plugin.Write("WARNING: Tried to create another MonoBehaviour instance when one already exists! Destroying self.");
                Destroy(this);
            }
            else
                instance = this;
        }

        public void Activate()
        {
            Plugin.Write("Activating!");
            VRHead.OnVRHeadChanged += ResetState;
            SceneManager.activeSceneChanged += OnSceneChange;

            trackIRTransformer = gameObject.AddComponent<TrackIRTransformer>();

            VideoSettings();
            ResetState();
        }

        public void OnSceneChange(Scene sc1, Scene scn2)
        {
            Preferences.instance.Save();
            VideoSettings();
            ResetState();
        }

        public void ResetState()
        {
            Plugin.Write("State reset!");
            showEndMissionWindow = false;
            endMissionWindowAutoShown = false;
            endMisWinLogScroll = Vector2.zero;
            viewIsSpec = false;

            currentFOV = DEFAULT_FOV;
            RegrabTracks();
        }

        public void VideoSettings()
        {
            Plugin.Write("Setting video settings...");
            StartCoroutine(DisableVR());
            Application.targetFrameRate = Mathf.Min(120, Screen.currentResolution.refreshRate);
            QualitySettings.antiAliasing = 2;
        }

        IEnumerator DisableVR()
        {
            VRUtils.DisableVR();
            yield return 0; // allow VR to fully disable by next frame

            // reset camera transform
            cameraEyeGameObject.transform.localPosition = Vector3.zero;
            cameraEyeGameObject.transform.localRotation = Quaternion.identity;

            // reset playspace transform
            var ps = cameraEyeGameObject.transform.parent;
            ps.localPosition = new Vector3(0, 1.1f, 0);
            ps.localRotation = Quaternion.identity;
        }

        public void OnGUI()
        {
            cursorOverWindow = false;
            var mousePos = Input.mousePosition;
            mousePos.y = Screen.height - mousePos.y;

            if (showMainWindow)
            {
                mainWindowRect = GUI.Window(405, mainWindowRect, GUIMainWindow, "FlatScreen 2 Control Panel");
                if (mainWindowRect.Contains(mousePos))
                    cursorOverWindow = true;
            }
            if (endMission != null && showEndMissionWindow)
            {
                endMissionWindowRect = GUI.Window(406, endMissionWindowRect, GUIEndMissionWindow, "FlatScreen 2 End Mission");
                if (endMissionWindowRect.Contains(mousePos))
                    cursorOverWindow = true;
            }
        }

        public void ToggleEndMissionWindow()
        {
            showEndMissionWindow = !showEndMissionWindow;
            if (showEndMissionWindow)
                GUI.FocusWindow(406);
        }

        void GUIMainWindow(int windowID)
        {
            GUI.DragWindow(new Rect(0, 0, 10000, 20));

            GUILayout.Label("F9: show/hide this window");
            GUILayout.Space(-8);
            GUILayout.Label("Ctrl+Z: reset camera rotation");
            GUILayout.Space(20);

            if (flatScreenEnabled)
            {
                mainWindowScroll = GUILayout.BeginScrollView(mainWindowScroll);
                {
                    if (cameraEyeGameObject != null || TryUpdateCameraTracks())
                    {
                        GUILayout.BeginHorizontal();
                        {
                            GUILayout.Label($"FOV: {currentFOV}");
                            int newFOV = (int)Mathf.Round(GUILayout.HorizontalSlider(currentFOV, 30f, 120f));

                            if (newFOV != currentFOV)
                                SetCameraFOV(newFOV);
                        }
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Mouse Sensitivity: {Preferences.instance.mouseSensitivity}");
                    Preferences.instance.mouseSensitivity = (int) Mathf.Round(
                        GUILayout.HorizontalSlider(Preferences.instance.mouseSensitivity, 1f, 9f)
                    );
                    GUILayout.EndHorizontal();

                    Preferences.instance.zoomReqCtrlRMB = 
                        GUILayout.Toggle(
                            Preferences.instance.zoomReqCtrlRMB,
                            " Require Ctrl/RMB to scroll-zoom\n (might be useful for trackpad pinch-zoomers!"
                        );

                    LimitXRotation =
                        Preferences.instance.limitXRot = GUILayout.Toggle(LimitXRotation, " Limit X Rotation");
                    LimitYRotation =
                        Preferences.instance.limitYRot = GUILayout.Toggle(LimitYRotation, " Limit Y Rotation");

                    GUILayout.Space(25);

                    GUILayout.BeginHorizontal();
                    {
                        GUILayout.Label($"Hovered VRInteractable:");
                        if (targetedVRInteractable != null)
                            GUILayout.Label(targetedVRInteractable?.interactableName ?? "???");
                        else
                            GUILayout.Label("[None]");
                    }
                    GUILayout.EndHorizontal();

                    //if (targetedVRInteractable != null)
                    //    foreach (var comp in targetedVRInteractable?.GetComponents<MonoBehaviour>())
                    //    {
                    //        GUILayout.Label(comp.GetType().Name);
                    //    }

                    GUILayout.BeginHorizontal();
                    {
                        GUILayout.Label($"Held-Down VRInteractable:");
                        if (heldVRInteractable != null)
                            GUILayout.Label(heldVRInteractable?.interactableName ?? "???");
                        else
                            GUILayout.Label("[None]");
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Label("Use the scroll wheel on non-integer knobs");

                    GUILayout.Space(20);

                    if (GUILayout.Button("Fix Camera"))
                    {
                        RegrabTracks();
                    }

                    if (GUILayout.Button("Reset Camera Rotation"))
                        ResetCameraRotation();

                    if (GUILayout.Button("View: " + (viewIsSpec ? "S-CAM" : "First Person")))
                    {
                        viewIsSpec = !viewIsSpec;

                        SetSpecActive(viewIsSpec);
                        SetAvatarVisibility(viewIsSpec);
                    }

                    GUI.enabled = true;

                    /*if (IsReadyRoomScene())
                    {
                        if (GUILayout.Button("Quick Select Vehicle"))
                        {
                            PilotSelectUI pilotSelectUI = FindObjectOfType<PilotSelectUI>();
                            pilotSelectUI.StartSelectedPilotButton();
                            pilotSelectUI.SelectVehicleButton();
                        }
                    }*/

                    GUILayout.Space(30);

                    if (GUILayout.Button(trackIRTransformer.IsRunning ? "Stop TrackIR" : "Start TrackIR"))
                    {
                        if (trackIRTransformer.IsRunning)
                        {
                            // stop tracking
                            trackIRTransformer.StopTracking();
                        }
                        else
                        {
                            // start tracking
                            ResetCameraRotation();
                            trackIRTransformer.StartTracking();
                        }
                    }

                    GUILayout.Space(30);
                    /*
                    Camera camera = GetEyeCamera();
                    if (camera != null)
                    {
                        GUILayout.Label($"Camera: {camera.name}");
                        GUILayout.Label($"Camera GameObject: {GetEyeCameraGameObject()?.name}");
                        GUILayout.Label($"Depth: {camera.depth}");
                        GUILayout.Label($"Enabled: {camera.enabled}");
                        GUILayout.Label($"Is Active and Enabled: {camera.isActiveAndEnabled}");
                        GUILayout.Label($"Quad Parent: {camera.transform.parent?.parent?.parent?.parent?.name}");
                    }

                    GUILayout.Space(30);

                    if (targetedVRInteractable != null)
                    {
                        VRThrottle throttle = targetedVRInteractable.GetComponent<VRThrottle>();
                        GUILayout.Label($"Throttle: {throttle}");
                        GUILayout.Label($"Throttle Transform: {throttle?.throttleTransform?.name}");
                    }*/
                }
                GUILayout.EndScrollView();
            }
            else
            {
                bool toEnable = GUILayout.Button("Activate FlatScreen2");
                GUILayout.Space(-5);
                GUILayout.BeginHorizontal();
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("WARNING: MUST RESTART TO GO BACK TO VR");
                    GUILayout.FlexibleSpace();
                }
                GUILayout.EndHorizontal();
                if (toEnable)
                {
                    flatScreenEnabled = true;
                    Activate();
                }
            }
            
            // Credits
            GUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("FlatScreen 2 by muskit");
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(-8);
            GUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("FlatScreen originally by frdhog");
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
        }

        private void GUIEndMissionWindow(int id)
        {
            GUI.DragWindow(new Rect(0, 0, 10000, 20));

            if (!VTOLMPUtils.IsMultiplayer() && GUILayout.Button("Restart Mission"))
            {
                FlightSceneManager.instance.ReloadScene();
                showEndMissionWindow = false;
            }
            if (GUILayout.Button("Finish Mission"))
            {
                FlightSceneManager.instance.ReturnToBriefingOrExitScene();
                showEndMissionWindow = false;
            }

            GUILayout.BeginHorizontal();
            {
                GUILayout.Label($"Mission Status: ", GUILayout.ExpandWidth(false));
                if (endMission.inProgressObject.activeSelf)
                {
                    GUILayout.Label("IN PROGRESS");
                }
                if (endMission.completeObject.activeSelf)
                {
                    GUI.contentColor = Color.green;
                    GUILayout.Label("COMPLETE");
                }
                if (endMission.failedObject.activeSelf)
                {
                    GUI.contentColor = Color.red;
                    GUILayout.Label("FAILED");
                }
                GUI.contentColor = Color.white;
            }
            GUILayout.EndHorizontal();
            
            if (CheckMissionEnded())
            {
                GUILayout.Space(-7);
                GUILayout.Label($"Mission Completion Time: {endMission.metCompleteText?.text}");
            }

            GUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Flight Log:");
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(-7);

            endMisWinLogScroll = GUILayout.BeginScrollView(endMisWinLogScroll, GUILayout.MaxHeight(400), GUILayout.ExpandHeight(true));
            {
                var stringBuilder = new System.Text.StringBuilder();
                foreach (FlightLogger.LogEntry logEntry in FlightLogger.GetLog())
                    stringBuilder.AppendLine(logEntry.timestampedMessage);
                GUILayout.TextArea(stringBuilder.ToString(), GUILayout.ExpandHeight(true));
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Dump Flight Log"))
                endMission.DumpFlightLog();

            GUILayout.Space(24);

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            {
                GUILayout.Label($"Quicksave:");

                GUI.enabled = QuicksaveManager.instance.CheckQsEligibility();
                if (GUILayout.Button("SAVE"))
                {
                    QuicksaveManager.instance.Quicksave();
                }
                    
                GUI.enabled = QuicksaveManager.quickloadAvailable;
                if (GUILayout.Button("LOAD"))
                {
                    QuicksaveManager.instance.Quickload();
                    showEndMissionWindow = false;
                }

                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
        }

        public void SetAvatarVisibility(bool isVis)
        {
            /* The scene paths here will likely need to be updated if a game update changes any of these. */
            if (playerBody == null)
                playerBody = GameObject.Find("suit2/RiggedSuit.001");
            if (playerLeftHand == null)
                playerLeftHand = GameObject.Find("Controller (left)/newGlove/SWAT_glower_pivot.002");
            if (playerRightHand == null)
                playerRightHand = GameObject.Find("Controller (right)/newGlove/SWAT_glower_pivot.002");

            // helmet if in team select
            var teamSelectAv = GameObject.Find("TeamSelectSpawn/BriefingAvatar");
            if (teamSelectAv != null)
            {
                teamSelectAv.transform
                    .Find("Local/CameraRigParent/[CameraRig]/Camera (eye)/Helmet/hqh")?
                    .gameObject.SetActive(isVis);
            }

            // helmet if in MP briefing room
            var seshPms = FindObjectsOfType<PlayerModelSync>();
            foreach (var pms in seshPms)
            {
                if (pms.isMine && pms.gameObject.name.Contains("BriefingAvatar"))
                {
                    pms.transform
                    .Find("Local/CameraRigParent/[CameraRig]/Camera (eye)/Helmet/hqh")
                    .gameObject.SetActive(isVis);
                    break;
                }
            }
            
            // body visiblity
            Plugin.Write($"Setting body ({playerBody}) vis to {isVis}");
            playerBody?.SetActive(isVis);

            // hands visibility
            Plugin.Write($"Setting left hand ({playerLeftHand}) vis to {isVis}");
            playerLeftHand?.SetActive(isVis);
            Plugin.Write($"Setting right hand ({playerRightHand}) vis to {isVis}");
            playerRightHand?.SetActive(isVis);
        }

        public void SetSpecActive(bool active)
        {
            foreach (Camera specCam in Util.GetSpectatorCameras())
            {
                specCam.depth = active ? 50 : -6;
            }
        }

        public void MouseMoveCamera()
        {
            if (Input.GetMouseButton(1) && !trackIRTransformer.IsRunning && !cursorOverWindow)
            {
                cameraRotation.x += Input.GetAxis("Mouse X") * Preferences.instance.mouseSensitivity;
                cameraRotation.y += Input.GetAxis("Mouse Y") * Preferences.instance.mouseSensitivity;
                if (rotLimX > 0)
                    cameraRotation.x = Mathf.Clamp(cameraRotation.x, -rotLimX, rotLimX);
                if (rotLimY > 0)
                    cameraRotation.y = Mathf.Clamp(cameraRotation.y, -rotLimY, rotLimY);
                var xQuat = Quaternion.AngleAxis(cameraRotation.x, Vector3.up);
                var yQuat = Quaternion.AngleAxis(cameraRotation.y, Vector3.left);

                cameraEyeGameObject.transform.localRotation = xQuat * yQuat; //Quaternions seem to rotate more consistently than EulerAngles. Sensitivity seemed to change slightly at certain degrees using Euler.
                                                                             //transform.localEulerAngles = new Vector3(-rotation.y, rotation.x, 0);

                // TODO?: change to subtle cursor location indication (ie. cross)
                Cursor.visible = false;
            }
        }
        
        public void ResetCameraRotation()
        {
            cameraEyeGameObject.transform.localRotation = Quaternion.identity;
            cameraRotation = Vector2.zero;
        }

        public void SetCameraFOV(int fov)
        {
            fov = Mathf.Clamp(fov, 30, 120);
            currentFOV = fov;

            if (cameraEyeGameObject != null)
                cameraEyeGameObject.GetComponent<Camera>().fieldOfView = fov;
            if (cameraHMDGameObject != null)
                cameraHMDGameObject.GetComponent<Camera>().fieldOfView = fov;
        }
        public float GetCameraFOV()
        {
            return cameraEyeGameObject.GetComponent<Camera>().fieldOfView;
        }

        /// <summary>
        /// Update this instance's camera variables.
        /// </summary>
        /// <returns>If the main camera was grabbed successfully.</returns>
        public bool TryUpdateCameraTracks()
        {
            if (cameraEyeGameObject == null)
            {
                cameraEyeGameObject = Util.GetEyeCamera()?.gameObject;

                if (cameraEyeGameObject != null)
                {
                    trackIRTransformer.trackedObject = cameraEyeGameObject.transform;
                    cameraHMDGameObject = cameraEyeGameObject.transform.Find("Camera HMD HUD")?.gameObject;
                    cameraHelmetGameObject = cameraEyeGameObject.transform.Find("Camera (eye) Helmet")?.gameObject;

                    var res = Screen.currentResolution;

                    // unwarp cameras if activating during VR session
                    cameraEyeGameObject.GetComponent<Camera>()
                        .pixelRect = new Rect(0, 0, res.width, res.height);

                    if (cameraHMDGameObject != null)
                    {
                        cameraHMDGameObject.GetComponent<Camera>()
                            .pixelRect = new Rect(0, 0, res.width, res.height);
                    }

                    if (cameraHelmetGameObject != null)
                    {
                        cameraHelmetGameObject.GetComponent<Camera>()
                            .pixelRect = new Rect(0, 0, res.width, res.height);

                        cameraHelmetGameObject.GetComponent<Camera>().fieldOfView = 20;
                    }
                }
            }

            return cameraEyeGameObject != null;
        }

        public void RegrabTracks()
        {
            Plugin.Write("Regrabbing tracked player objects...");
            cameraEyeGameObject = null;
            cameraHMDGameObject = null;
            playerBody = null;
            playerLeftHand = null;
            playerRightHand = null;

            if (TryUpdateCameraTracks())
            {
                Plugin.Write($"    Camera grabbed: {cameraEyeGameObject}");
                ResetCameraRotation();
                SetCameraFOV(currentFOV);
            }
            else
            {
                Plugin.Write($"    Could not find camera!");
            }

            foreach (Camera specCam in Util.GetSpectatorCameras())
            {
                specCam.depth = -6;
            }

            SetSpecActive(false);
            SetAvatarVisibility(false);
        }

        public void GetHoveredObject()
        {
            Camera camera = cameraEyeGameObject.GetComponent<Camera>();
            Ray ray = camera.ScreenPointToRay(Input.mousePosition, Camera.MonoOrStereoscopicEye.Mono);

            List<VRInteractable> intersectedInteractables = new List<VRInteractable>();

            foreach (VRInteractable interactable in vrInteractables)
            {
                if (interactable == null || interactable.transform == null)
                    continue;

                Bounds bounds;

                float radius = Mathf.Min(Mathf.Max(0.01f, interactable.radius), 0.1f); // have a minimum (and maximum) radius to avoid 0 size radius (and to avoid having to calculate rect sizes)
                bounds = new Bounds(interactable.transform.position, Vector3.one * radius);

                if (bounds.IntersectRay(ray))
                {
                    intersectedInteractables.Add(interactable);
                }
            }

            float depth = 0.5f;
            targetedVRInteractable = intersectedInteractables
                .Where(x => x != null && x.transform != null)
                .OrderBy((x) => Vector3.Distance(x.transform.position, ray.origin + (ray.direction * depth)))
                .FirstOrDefault();
        }

        MeshRenderer PreviouslyHighlightedInteractableRenderer;
        Dictionary<MeshRenderer, Color> VRInteractableOriginalColors = new Dictionary<MeshRenderer, Color>();
        private void HighlightObject(VRInteractable targetedVRInteractable)
        {
            HighlightImage(targetedVRInteractable);

            if (PreviouslyHighlightedInteractableRenderer != null)
            {
                PreviouslyHighlightedInteractableRenderer.material.color = VRInteractableOriginalColors[PreviouslyHighlightedInteractableRenderer];
                PreviouslyHighlightedInteractableRenderer = null;
            }

            if (targetedVRInteractable == null)
                return;

            MeshRenderer renderer = GetMeshRendererFromVRInteractable(targetedVRInteractable);
            if (renderer == null)
            {
                return;
            }

            VRInteractableOriginalColors[renderer] = renderer.material.color;

            if (renderer != null)
                renderer.material.color = Color.yellow;

            if (targetedVRInteractable != null)
                PreviouslyHighlightedInteractableRenderer = renderer;
        }

        Image PreviouslyHighlightedInteractableImage;
        Dictionary<Image, Color> VRInteractableImageOriginalColors = new Dictionary<Image, Color>();
        private void HighlightImage(VRInteractable targetedVRInteractable)
        {
            if (PreviouslyHighlightedInteractableImage != null)
            {
                PreviouslyHighlightedInteractableImage.color = VRInteractableImageOriginalColors[PreviouslyHighlightedInteractableImage];
                PreviouslyHighlightedInteractableImage = null;
            }

            if (targetedVRInteractable == null)
                return;

            Image image = GetImageFromVRInteractable(targetedVRInteractable);
            if (image == null)
            {
                return;
            }

            VRInteractableImageOriginalColors[image] = image.color;

            if (image != null)
                image.color = Color.yellow;

            if (targetedVRInteractable != null)
                PreviouslyHighlightedInteractableImage = image;
        }

        private MeshRenderer GetMeshRendererFromVRInteractable(VRInteractable interactable)
        {
            VRButton button = interactable.GetComponent<VRButton>();
            VRLever lever = interactable.GetComponent<VRLever>();
            VRTwistKnob twistKnob = interactable.GetComponent<VRTwistKnob>();
            VRTwistKnobInt twistKnobInt = interactable.GetComponent<VRTwistKnobInt>();
            MeshRenderer meshRenderer = interactable.GetComponent<MeshRenderer>();
            MeshRenderer childMeshRenderer = interactable.GetComponentInChildren<MeshRenderer>();

            if (meshRenderer != null)
                return meshRenderer;
            if (childMeshRenderer != null)
                return childMeshRenderer;

            Transform transform = button?.buttonTransform ??
                lever?.leverTransform ??
                twistKnob?.knobTransform ??
                twistKnobInt?.knobTransform;

            return transform?.GetComponent<MeshRenderer>() ??
                transform?.parent?.GetComponent<MeshRenderer>() ??
                transform?.parent?.parent?.GetComponent<MeshRenderer>() ??
                transform?.GetComponentInChildren<MeshRenderer>();
        }

        private Image GetImageFromVRInteractable(VRInteractable interactable)
        {
            Image image = interactable.GetComponent<Image>();
            Image childImage = interactable.GetComponentInChildren<Image>();
            Image parentImage = interactable.GetComponentInParent<Image>();
            Image parentParentImage = interactable.transform.parent?.GetComponentInParent<Image>();

            return image ??
                childImage ??
                parentImage ??
                parentParentImage;
        }

        public void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                showMainWindow = !showMainWindow;
                Preferences.instance.Save();
            }

            if (!flatScreenEnabled)
                return;

            if (Input.GetKeyDown(KeyCode.Escape))
                ToggleEndMissionWindow();

            if (cameraEyeGameObject == null)
                return;

            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Z))
                ResetCameraRotation();

            HighlightObject(targetedVRInteractable);
            // TODO: set cursor texture

            if (Input.GetMouseButtonDown(0) && !cursorOverWindow) // left mouse down
            {
                if (targetedVRInteractable != null && heldVRInteractable == null)
                {
                    Interactions.Interact(targetedVRInteractable);
                    heldVRInteractable = targetedVRInteractable;
                }
            }
            if (Input.GetMouseButtonUp(0)) // left mouse up
            {
                if (heldVRInteractable != null)
                {
                    Interactions.AntiInteract(heldVRInteractable);
                    heldVRInteractable = null;
                }
            }

            // TODO: UI scrollbar dragging

            // scroll wheel
            if (Input.mouseScrollDelta.y != 0 && !cursorOverWindow)
            {
                // eww
                if (
                        (Preferences.instance.zoomReqCtrlRMB &&
                            (Input.GetMouseButton(1) || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                        ) ||
                        (!Preferences.instance.zoomReqCtrlRMB &&
                            (targetedVRInteractable == null ||
                            targetedVRInteractable.GetComponent<VRButton>() != null ||
                            targetedVRInteractable.GetComponent<VRInteractableUIButton>() != null ||
                            targetedVRInteractable.GetComponent<VRIHoverToggle>() != null)
                        )
                    )
                {
                    int newFOV = currentFOV + (Input.mouseScrollDelta.y < 0 ? 5 : -5);
                    SetCameraFOV(newFOV);
                }
                else if (targetedVRInteractable != null) // otherwise, scrollable interact
                {
                    // Scrollables interactables
                    VRTwistKnob twistKnob = targetedVRInteractable?.GetComponent<VRTwistKnob>();
                    VRTwistKnobInt twistKnobInt = targetedVRInteractable?.GetComponent<VRTwistKnobInt>();
                    VRLever lever = targetedVRInteractable?.GetComponent<VRLever>();
                    VRThrottle throttle = targetedVRInteractable?.GetComponent<VRThrottle>();
                    VRIntUIScroller uiScroll = targetedVRInteractable?.GetComponent<VRIntUIScroller>();

                    if (twistKnob != null)
                    {
                        Interactions.TwistKnob(twistKnob, Input.mouseScrollDelta.y < 0 ? true : false, 0.05f);
                    }
                    else if (twistKnobInt != null)
                    {
                        Interactions.MoveTwistKnobInt(twistKnobInt, Input.mouseScrollDelta.y < 0 ? 1 : -1, true);
                    }
                    else if (lever != null)
                    {
                        Interactions.MoveLever(lever, Input.mouseScrollDelta.y < 0 ? 1 : -1, true);
                    }
                    else if (throttle != null)
                    {
                        Interactions.MoveThrottle(throttle, Input.mouseScrollDelta.y > 0 ? -0.05f : 0.05f);
                    }
                    else if (uiScroll != null)
                    {
                        uiScroll.scrollRect.normalizedPosition += 0.1f * Input.mouseScrollDelta;
                    }
                }
            }
        }

        public void LateUpdate()
        {
            if (!flatScreenEnabled || cameraEyeGameObject == null)
                return;

            // Cursor autohide
            if (Input.GetAxis("Mouse X") != 0 || Input.GetAxis("Mouse Y") != 0 ||
                Input.GetMouseButton(0) || Input.GetMouseButton(1))
            {
                cursorHideTimer = CURSOR_HIDE_AFTER;
            }
            else if (cursorHideTimer > 0)
            {
                cursorHideTimer -= Time.deltaTime;
            }
            Cursor.visible = cursorHideTimer > 0;

            MouseMoveCamera();
        }
        
        // tick loop
        public void FixedUpdate()
        {
            if (!flatScreenEnabled || cameraEyeGameObject == null)
                return;

            frameTick++;

            if (frameTick % FRAMES_PER_SUBTICK == 0) // every sub-tick
            {
                GetHoveredObject();
            }

            if (frameTick >= FRAMES_PER_TICK) // every tick
            {
                frameTick = 0;
                
                // check if mission has ended
                if (!endMissionWindowAutoShown && CheckMissionEnded())
                {
                    Plugin.Write("Ended mission! Auto-showing mission end window...");
                    showEndMissionWindow = false;
                    ToggleEndMissionWindow();
                    endMissionWindowAutoShown = true;
                }

                vrInteractables = GameObject.FindObjectsOfType<VRInteractable>(false);
            }
        }

        public void OnDestroy()
        {
            showMainWindow = false;
            showEndMissionWindow = false;

            if (flatScreenEnabled)
            {
                SceneManager.activeSceneChanged -= OnSceneChange;
                VRHead.OnVRHeadChanged -= ResetState;
            }

            instance = null;
        }
    }
}
