using BepInEx;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[BepInPlugin("com.yourname.multicam", "MultiMonitorFeed", "1.3.0")]
public class Plugin : BaseUnityPlugin
{
    private enum QualityPreset
    {
        Low,
        Medium,
        High
    }

    private static readonly int[] FpsOptions = { 5, 8, 12, 20, 24, 30 };
    private const int RouteModeOff = 0;
    private const int RouteModeMain = 1;
    private const int RouteModeEmergency = 2;

    private Camera[] playerCameras;
    private Camera[] mapCameras;

    private RenderTexture[] playerTextures;
    private RenderTexture[] mapTextures;

    private RawImage[] largeImages;
    private RawImage[] smallImages;
    private GameObject[] tileObjects;
    private GameObject[] menuPanels;
    private Button[] menuButtons;

    private Text[] qualityButtonTexts;
    private Text[] fpsButtonTexts;
    private Text[] autoButtonTexts;
    private Text[] powerButtonTexts;
    private Text[] pipButtonTexts;
    private Text[] zoomButtonTexts;
    private Text[] mapTypeButtonTexts;
    private Text[] routeButtonTexts;
    private Text[] statusBadges;
    private Text[] playerNameTexts;
    private Text potatoButtonText;
    private Text ratioButtonText;

    private bool[] cameraEnabled;
    private bool[] primaryIsMap;
    private bool[] autoQualityEnabled;
    private bool[] autoDowngradedState;
    private bool[] deadState;
    private bool[] disconnectedState;
    private int[] routeMode;
    private QualityPreset[] cameraQuality;
    private int[] fpsCap;
    private int[] pipScaleIndex;
    private int[] mapZoomIndex;
    private int[] mapTypeIndex;
    private int[] mapPrimeStage;
    private float[] mapPrimeAt;
    private bool[] mapPrimedOnce;

    private float[] nextPlayerRender;
    private float[] nextMapRender;
    private float[] nextAutoAdjust;

    private Canvas canvas;
    private RectTransform gridRoot;
    private RectTransform[] smallViewRects;
    private RectTransform[] largeMapOverlayRoots;
    private RectTransform[] smallMapOverlayRoots;
    private RectTransform rootRect;
    private string[] sessionPlayerNames;
    private List<Image>[] routeLineImages;
    private readonly List<Vector3> routeWorkTargets = new List<Vector3>(8);
    private readonly List<Vector3> routePathWorkPoints = new List<Vector3>(32);
    private readonly NavMeshPath routeNavMeshPath = new NavMeshPath();
    private readonly List<Vector3> mainRouteTargets = new List<Vector3>(8);
    private readonly List<Vector3> emergencyRouteTargets = new List<Vector3>(16);
    private float nextRouteTargetRefreshAt;

    private Text pageText;
    private RectTransform tooltipRect;
    private Text tooltipText;
    private Button localBoxToggleButton;
    private Text localBoxToggleText;
    private Text globalQualityText;
    private Text globalFpsText;
    private Text globalAutoText;
    private Text globalPipText;
    private Text globalZoomText;
    private Text globalRouteText;
    private GameObject activeGlobalDropdown;
    private string activeGlobalDropdownName;
    private Button globalQualityButton;
    private Button globalFpsButton;
    private Button globalAutoButton;
    private Button globalPipButton;
    private Button globalZoomButton;
    private Button globalRouteButton;
    private Material opaqueFeedMaterial;
    private GameObject renamePanel;
    private InputField renameInput;
    private int renamingIndex = -1;
    private bool capturedCursorState;
    private bool originalCursorVisible;
    private CursorLockMode originalCursorLockState;

    private GameObject focusedOverlay;
    private RawImage focusedLargeImage;
    private RawImage focusedSmallImage;
    private RectTransform focusedMapOverlayLargeRoot;
    private RectTransform focusedMapOverlaySmallRoot;
    private Button focusedMenuButton;
    private GameObject focusedMenuPanel;
    private Text focusedMenuPowerText;
    private Text focusedMenuQualityText;
    private Text focusedMenuFpsText;
    private Text focusedMenuPipText;
    private Text focusedMenuZoomText;
    private Text focusedMenuMapTypeText;
    private Text focusedMenuRouteText;
    private int focusedIndex = -1;
    private bool focusedPrimaryIsMap;
    private bool[] hideSelfCamera;

    private bool initialized;
    private bool sessionActive;
    private bool secondDisplayActivated;
    private int localPlayerIndex = -1;
    private int gridSlots = 4;
    private int currentPage;
    private bool forceTileAspect16x9;
    private bool layoutDirty;
    private float lastGridWidth = -1f;
    private float lastGridHeight = -1f;

    private float smoothedFrameTime = 0.016f;

    private FieldInfo mapScreenField;
    private FieldInfo mapCameraField;
    private FieldInfo heldObjectField;
    private FieldInfo heldObjectServerField;
    private Type entranceTeleportType;
    private FieldInfo insideFactoryField;
    private PropertyInfo insideFactoryProperty;
    private Camera defaultRadarCamera;
    private float radarHeightOffset = 34f;
    private const float InteriorRadarHeightOffset = 14f;
    // Slightly higher near clip for indoor maps prevents upper floors from covering players in mansion layouts.
    private const float InteriorMapNearClip = 9f;
    private const float InteriorMapBelowDepth = 2.5f;
    private const float RouteTargetRefreshInterval = 4f;
    private const float RouteLineThickness = 2f;
    private const float RouteDashLength = 10f;
    private const float RouteDashGap = 7f;
    private const float RouteMaxSegmentDistanceFromPlayer = 210f;
    private const float RouteMaxPathDrawDistance = 200f;
    private const float RouteVeryCloseDistance = 20f;
    private const float RouteCloseDistance = 50f;
    private const float RouteMidDistance = 90f;
    private const float RouteFarDistance = 140f;
    private const float RadarContourHeightBoost = -2f; // niveau radar
    private const float PlayerCameraForwardOffset = 0.38f; // camera forward
    private const float PlayerCameraUpOffset = 0.06f; // camera up

    private static readonly Vector2[] PipBaseSizes =
    {
        new Vector2(160f, 90f),
        new Vector2(256f, 144f),
        new Vector2(320f, 180f)
    };

    private static readonly float[] MapZoomSizes = { 44f, 36f, 28f, 22f, 16f, 11f, 8f };
    private const float MapNearClip = 1.5f;
    private const float MapFarClip = 120f;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetWindowText(IntPtr hWnd, string text);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SwHide = 0;
    private const int SwShow = 5;

    private void Start()
    {
        Logger.LogInfo("MultiMonitorFeed Loaded");
    }

    private void Update()
    {
        bool hasSession = HasSession();

        if (hasSession && !sessionActive)
        {
            SetupSystem();
        }

        if (!hasSession && sessionActive)
        {
            TeardownSystem();
            return;
        }

        if (canvas != null)
        {
            canvas.enabled = hasSession;
        }

        if (!hasSession || !sessionActive)
        {
            return;
        }

    }

    private bool HasSession()
    {
        if (StartOfRound.Instance == null || StartOfRound.Instance.allPlayerScripts == null || StartOfRound.Instance.allPlayerScripts.Length == 0)
        {
            return false;
        }

        NetworkManager netManager = NetworkManager.Singleton;
        if (netManager == null)
        {
            return false;
        }

        return netManager.IsListening || netManager.IsClient || netManager.IsServer;
    }

    private void TryRenameSecondaryDisplayWindow()
    {
        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
        {
            return;
        }

        IntPtr secondaryWindow = FindSecondaryDisplayWindow();
        if (secondaryWindow != IntPtr.Zero)
        {
            SetWindowText(secondaryWindow, "Multi Cam Dashboard");
        }
    }

    private IntPtr FindSecondaryDisplayWindow()
    {
        IntPtr byCustomTitle = FindWindow(null, "Multi Cam Dashboard");
        if (byCustomTitle != IntPtr.Zero)
        {
            return byCustomTitle;
        }

        return FindWindow(null, "Unity Secondary Display");
    }

    private void SetSecondaryDisplayWindowVisible(bool visible)
    {
        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
        {
            return;
        }

        IntPtr secondaryWindow = FindSecondaryDisplayWindow();
        if (secondaryWindow != IntPtr.Zero)
        {
            ShowWindow(secondaryWindow, visible ? SwShow : SwHide);
        }
    }

    private void SetupSystem()
    {
        if (StartOfRound.Instance == null || StartOfRound.Instance.allPlayerScripts == null)
        {
            Logger.LogWarning("StartOfRound is not ready yet. Waiting for lobby initialization.");
            return;
        }

        CleanupLegacyDebugArtifacts();
        CaptureCursorState();

        PlayerControllerB[] players = StartOfRound.Instance.allPlayerScripts;
        int count = players.Length;

        if (!secondDisplayActivated && Display.displays.Length > 1)
        {
            Display.displays[1].Activate();
            secondDisplayActivated = true;
            Logger.LogInfo("Second monitor activated for session");
        }

        TryRenameSecondaryDisplayWindow();
        SetSecondaryDisplayWindowVisible(true);

        playerCameras = new Camera[count];
        mapCameras = new Camera[count];

        playerTextures = new RenderTexture[count];
        mapTextures = new RenderTexture[count];

        largeImages = new RawImage[count];
        smallImages = new RawImage[count];
        tileObjects = new GameObject[count];
        menuPanels = new GameObject[count];
        menuButtons = new Button[count];

        qualityButtonTexts = new Text[count];
        fpsButtonTexts = new Text[count];
        autoButtonTexts = new Text[count];
        powerButtonTexts = new Text[count];
        pipButtonTexts = new Text[count];
        zoomButtonTexts = new Text[count];
        mapTypeButtonTexts = new Text[count];
        routeButtonTexts = new Text[count];
        statusBadges = new Text[count];
        playerNameTexts = new Text[count];

        cameraEnabled = new bool[count];
        primaryIsMap = new bool[count];
        autoQualityEnabled = new bool[count];
        autoDowngradedState = new bool[count];
        deadState = new bool[count];
        disconnectedState = new bool[count];
        routeMode = new int[count];
        cameraQuality = new QualityPreset[count];
        fpsCap = new int[count];
        pipScaleIndex = new int[count];
        mapZoomIndex = new int[count];
        mapTypeIndex = new int[count];
        mapPrimeStage = new int[count];
        mapPrimeAt = new float[count];
        mapPrimedOnce = new bool[count];
        hideSelfCamera = new bool[count];

        smallViewRects = new RectTransform[count];
        largeMapOverlayRoots = new RectTransform[count];
        smallMapOverlayRoots = new RectTransform[count];
        sessionPlayerNames = new string[count];
        routeLineImages = new List<Image>[count];

        nextPlayerRender = new float[count];
        nextMapRender = new float[count];
        nextAutoAdjust = new float[count];

        for (int i = 0; i < count; i++)
        {
            cameraEnabled[i] = false;
            primaryIsMap[i] = false;
            autoQualityEnabled[i] = true;
            autoDowngradedState[i] = false;
            deadState[i] = false;
            disconnectedState[i] = false;
            routeMode[i] = RouteModeOff;
            cameraQuality[i] = QualityPreset.Low;
            fpsCap[i] = 12;
            pipScaleIndex[i] = 2;
            mapZoomIndex[i] = 3;
            mapTypeIndex[i] = 2;
            mapPrimeStage[i] = 0;
            mapPrimeAt[i] = 0f;
            mapPrimedOnce[i] = false;
            hideSelfCamera[i] = false;
            nextPlayerRender[i] = 0f;
            nextMapRender[i] = 0f;
            nextAutoAdjust[i] = 0f;
            routeLineImages[i] = new List<Image>(8);
        }

        CacheDefaultRadarCamera();
        localPlayerIndex = FindLocalPlayerIndex(players);

        CreateCameras(players);
        CreateUI(players);
        for (int i = 0; i < count; i++)
        {
            RecreateRenderTargets(i);
            RefreshTileFeeds(i);
            RefreshMenuText(i);
        }

        layoutDirty = true;
        initialized = true;
        sessionActive = true;
        Logger.LogInfo("Camera monitor initialized automatically for lobby");
    }

    private void CreateCameras(PlayerControllerB[] players)
    {
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null)
            {
                continue;
            }

            Camera source = players[i].gameplayCamera;

            GameObject camObj = new GameObject("PlayerCam_" + i);
            Camera cam = camObj.AddComponent<Camera>();
            cam.enabled = false;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.depthTextureMode = DepthTextureMode.None;

            if (source != null)
            {
                cam.clearFlags = source.clearFlags;
                cam.backgroundColor = source.backgroundColor;
                cam.cullingMask = BuildPlayerCameraMask(players[i], source);
                cam.nearClipPlane = source.nearClipPlane;
                cam.farClipPlane = source.farClipPlane;
                cam.fieldOfView = source.fieldOfView;
            }

            playerCameras[i] = cam;

            GameObject mapObj = new GameObject("MapCam_" + i);
            Camera mapCam = mapObj.AddComponent<Camera>();
            mapCam.enabled = false;
            mapCam.orthographic = true;
            mapCam.orthographicSize = 18f;
            mapCam.clearFlags = CameraClearFlags.SolidColor;
            mapCam.backgroundColor = Color.black;
            mapCam.cullingMask = cam.cullingMask;
            mapCam.nearClipPlane = 0.1f;
            mapCam.farClipPlane = 420f;
            mapCam.allowHDR = false;
            mapCam.allowMSAA = false;

            ApplyDefaultMapCameraSettings(mapCam);

            mapCameras[i] = mapCam;
        }
    }

    private GrabbableObject TryGetHeldObject(PlayerControllerB player)
    {
        if (player == null)
        {
            return null;
        }

        if (heldObjectField == null)
        {
            heldObjectField = typeof(PlayerControllerB).GetField("currentlyHeldObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        if (heldObjectServerField == null)
        {
            heldObjectServerField = typeof(PlayerControllerB).GetField("currentlyHeldObjectServer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        object held = heldObjectServerField != null ? heldObjectServerField.GetValue(player) : null;
        if (held == null && heldObjectField != null)
        {
            held = heldObjectField.GetValue(player);
        }

        if (held is GrabbableObject direct)
        {
            return direct;
        }

        if (held is Component component)
        {
            return component.GetComponent<GrabbableObject>();
        }

        if (held is GameObject gameObject)
        {
            return gameObject.GetComponent<GrabbableObject>();
        }

        return null;
    }

    private int BuildPlayerCameraMask(PlayerControllerB player, Camera source)
    {
        return source != null ? source.cullingMask : ~0;
    }

    private bool IsPlayerInsideFactory(PlayerControllerB player)
    {
        if (player == null)
        {
            return false;
        }

        if (insideFactoryField == null)
        {
            insideFactoryField = typeof(PlayerControllerB).GetField("isInsideFactory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        if (insideFactoryProperty == null)
        {
            insideFactoryProperty = typeof(PlayerControllerB).GetProperty("isInsideFactory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        if (insideFactoryField != null)
        {
            object value = insideFactoryField.GetValue(player);
            if (value is bool insideFromField)
            {
                return insideFromField;
            }
        }

        if (insideFactoryProperty != null)
        {
            object value = insideFactoryProperty.GetValue(player, null);
            if (value is bool insideFromProperty)
            {
                return insideFromProperty;
            }
        }

        return false;
    }

    private float GetRadarHeightOffsetForPlayer(PlayerControllerB player)
    {
        return IsPlayerInsideFactory(player) ? InteriorRadarHeightOffset : radarHeightOffset;
    }

    private float GetMapNearClipForPlayer(PlayerControllerB player)
    {
        return IsPlayerInsideFactory(player) ? InteriorMapNearClip : MapNearClip;
    }

    private float GetMapFarClipForPlayer(PlayerControllerB player, float radarOffsetForPlayer)
    {
        if (!IsPlayerInsideFactory(player))
        {
            return MapFarClip;
        }

        float interiorFar = radarOffsetForPlayer + InteriorMapBelowDepth;
        return Mathf.Clamp(interiorFar, InteriorMapNearClip + 1f, MapFarClip);
    }

    private void CacheDefaultRadarCamera()
    {
        if (StartOfRound.Instance == null)
        {
            return;
        }

        if (mapScreenField == null)
        {
            mapScreenField = typeof(StartOfRound).GetField("mapScreen", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        object mapScreen = mapScreenField != null ? mapScreenField.GetValue(StartOfRound.Instance) : null;
        if (mapScreen == null)
        {
            return;
        }

        if (mapCameraField == null)
        {
            mapCameraField = mapScreen.GetType().GetField("mapCamera", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        Camera candidate = mapCameraField != null ? mapCameraField.GetValue(mapScreen) as Camera : null;
        if (candidate == null)
        {
            FieldInfo camField = mapScreen.GetType().GetField("cam", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            candidate = camField != null ? camField.GetValue(mapScreen) as Camera : null;
        }

        if (candidate == null)
        {
            candidate = FindSceneRadarCamera();
        }

        if (candidate == null)
        {
            return;
        }

        defaultRadarCamera = candidate;

        if (StartOfRound.Instance.localPlayerController != null)
        {
            float sampled = defaultRadarCamera.transform.position.y - StartOfRound.Instance.localPlayerController.transform.position.y;
            radarHeightOffset = Mathf.Clamp(sampled, 18f, 90f);
        }
    }

    private Camera FindSceneRadarCamera()
    {
        Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>(true);
        Camera fallback = null;

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null)
            {
                continue;
            }

            string name = camera.gameObject != null ? camera.gameObject.name : string.Empty;
            if (!string.IsNullOrEmpty(name))
            {
                string lower = name.ToLowerInvariant();
                if (lower.Contains("radar") || lower.Contains("map") || lower.Contains("screen"))
                {
                    return camera;
                }
            }

            if (fallback == null && camera.orthographic)
            {
                fallback = camera;
            }
        }

        return fallback;
    }

    private void ApplyDefaultMapCameraSettings(Camera mapCam)
    {
        if (mapCam == null)
        {
            return;
        }

        if (defaultRadarCamera == null)
        {
            CacheDefaultRadarCamera();
        }

        if (defaultRadarCamera == null)
        {
            mapCam.orthographic = true;
            mapCam.orthographicSize = 18f;
            mapCam.clearFlags = CameraClearFlags.SolidColor;
            mapCam.backgroundColor = Color.black;
            mapCam.nearClipPlane = 0.1f;
            mapCam.farClipPlane = 420f;
            return;
        }

        mapCam.orthographic = defaultRadarCamera.orthographic;
        mapCam.orthographicSize = defaultRadarCamera.orthographicSize;
        mapCam.fieldOfView = defaultRadarCamera.fieldOfView;
        mapCam.clearFlags = defaultRadarCamera.clearFlags;
        mapCam.backgroundColor = defaultRadarCamera.backgroundColor;
        mapCam.cullingMask = defaultRadarCamera.cullingMask;
        mapCam.nearClipPlane = defaultRadarCamera.nearClipPlane;
        mapCam.farClipPlane = defaultRadarCamera.farClipPlane;
        mapCam.allowHDR = false;
        mapCam.allowMSAA = false;
    }

    private void CreateUI(PlayerControllerB[] players)
    {
        EnsureEventSystem();

        GameObject canvasObj = new GameObject("CameraCanvas");
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.targetDisplay = Display.displays.Length > 1 ? 1 : 0;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600f, 900f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject root = new GameObject("RootPanel");
        root.transform.SetParent(canvasObj.transform, false);
        Image rootBg = root.AddComponent<Image>();
        rootBg.color = new Color(0f, 0f, 0f, 0.45f);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        this.rootRect = rootRect;

        CreateGridButtons(root.transform);
        CreateTooltip(root.transform);

        GameObject gridObj = new GameObject("GridRoot");
        gridObj.transform.SetParent(root.transform, false);
        gridRoot = gridObj.AddComponent<RectTransform>();
        gridRoot.anchorMin = new Vector2(0f, 0f);
        gridRoot.anchorMax = new Vector2(1f, 1f);
        gridRoot.offsetMin = new Vector2(24f, 24f);
        gridRoot.offsetMax = new Vector2(-24f, -48f);

        for (int i = 0; i < players.Length; i++)
        {
            string playerName = players[i] != null && !string.IsNullOrWhiteSpace(players[i].playerUsername)
                ? players[i].playerUsername
                : "Player #" + (i + 1);

            sessionPlayerNames[i] = playerName;

            CreateTile(i, playerName);
        }

        CreateRenamePanel(root.transform);

        CreateFocusedOverlay(root.transform);
    }

    private void TeardownSystem()
    {
        CleanupLegacyDebugArtifacts();
        RestoreCursorState();

        if (activeGlobalDropdown != null)
        {
            Destroy(activeGlobalDropdown);
            activeGlobalDropdown = null;
        }

        if (canvas != null)
        {
            Destroy(canvas.gameObject);
            canvas = null;
        }

        SetSecondaryDisplayWindowVisible(false);

        DestroyCameraArray(playerCameras);
        DestroyCameraArray(mapCameras);
        DestroyTextures(playerTextures);
        DestroyTextures(mapTextures);

        playerCameras = null;
        mapCameras = null;
        playerTextures = null;
        mapTextures = null;
        largeImages = null;
        smallImages = null;
        tileObjects = null;
        menuPanels = null;
        menuButtons = null;
        qualityButtonTexts = null;
        fpsButtonTexts = null;
        autoButtonTexts = null;
        powerButtonTexts = null;
        pipButtonTexts = null;
        zoomButtonTexts = null;
        mapTypeButtonTexts = null;
        routeButtonTexts = null;
        statusBadges = null;
        playerNameTexts = null;
        cameraEnabled = null;
        primaryIsMap = null;
        autoQualityEnabled = null;
        autoDowngradedState = null;
        deadState = null;
        disconnectedState = null;
        routeMode = null;
        cameraQuality = null;
        fpsCap = null;
        pipScaleIndex = null;
        mapZoomIndex = null;
        mapTypeIndex = null;
        mapPrimeStage = null;
        mapPrimeAt = null;
        mapPrimedOnce = null;
        hideSelfCamera = null;
        smallViewRects = null;
        largeMapOverlayRoots = null;
        smallMapOverlayRoots = null;
        sessionPlayerNames = null;
        routeLineImages = null;
        routeWorkTargets.Clear();
        mainRouteTargets.Clear();
        emergencyRouteTargets.Clear();
        nextRouteTargetRefreshAt = 0f;
        entranceTeleportType = null;
        rootRect = null;
        pageText = null;
        tooltipRect = null;
        tooltipText = null;
        localBoxToggleButton = null;
        localBoxToggleText = null;
        ratioButtonText = null;
        globalQualityText = null;
        globalFpsText = null;
        globalAutoText = null;
        globalPipText = null;
        globalZoomText = null;
        globalRouteText = null;
        activeGlobalDropdownName = null;
        globalQualityButton = null;
        globalFpsButton = null;
        globalAutoButton = null;
        globalPipButton = null;
        globalZoomButton = null;
        globalRouteButton = null;
        if (opaqueFeedMaterial != null)
        {
            Destroy(opaqueFeedMaterial);
            opaqueFeedMaterial = null;
        }
        renamePanel = null;
        renameInput = null;
        renamingIndex = -1;
        capturedCursorState = false;
        focusedOverlay = null;
        focusedLargeImage = null;
        focusedSmallImage = null;
        focusedMapOverlayLargeRoot = null;
        focusedMapOverlaySmallRoot = null;
        focusedMenuButton = null;
        focusedMenuPanel = null;
        focusedMenuPowerText = null;
        focusedMenuQualityText = null;
        focusedMenuFpsText = null;
        focusedMenuPipText = null;
        focusedMenuZoomText = null;
        focusedMenuMapTypeText = null;
        focusedMenuRouteText = null;
        focusedIndex = -1;
        focusedPrimaryIsMap = false;
        initialized = false;
        sessionActive = false;
        localPlayerIndex = -1;
        currentPage = 0;
        forceTileAspect16x9 = false;
        layoutDirty = false;
        lastGridWidth = -1f;
        lastGridHeight = -1f;
    }

    private void CleanupLegacyDebugArtifacts()
    {
        GameObject legacyOverlay = GameObject.Find("DebugOverlay");
        if (legacyOverlay != null)
        {
            Destroy(legacyOverlay);
        }

        GameObject legacyText = GameObject.Find("DebugText");
        if (legacyText != null)
        {
            Destroy(legacyText);
        }
    }

    private void DestroyCameraArray(Camera[] cameras)
    {
        if (cameras == null)
        {
            return;
        }

        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null)
            {
                Destroy(cameras[i].gameObject);
            }
        }
    }

    private void DestroyTextures(RenderTexture[] textures)
    {
        if (textures == null)
        {
            return;
        }

        for (int i = 0; i < textures.Length; i++)
        {
            if (textures[i] != null)
            {
                textures[i].Release();
                Destroy(textures[i]);
            }
        }
    }

    private void CreateGridButtons(Transform parent)
    {
        localBoxToggleButton = CreateButton(parent, "LocalBoxToggle", "HIDE MY BOX", new Vector2(16f, -16f), new Vector2(170f, 30f), delegate { ToggleLocalBox(); });
        localBoxToggleText = GetButtonLabel(localBoxToggleButton.gameObject);

        Button potatoButton = CreateButton(parent, "PotatoMode", "POTATO", new Vector2(198f, -16f), new Vector2(84f, 30f), delegate { ForcePotatoMode(); });
        potatoButtonText = GetButtonLabel(potatoButton.gameObject);

        GameObject sectionDivider = new GameObject("LeftNavDivider");
        sectionDivider.transform.SetParent(parent, false);
        Text dividerText = sectionDivider.AddComponent<Text>();
        dividerText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        dividerText.fontSize = 22;
        dividerText.alignment = TextAnchor.MiddleCenter;
        dividerText.color = new Color(0.9f, 0.9f, 0.9f, 0.92f);
        dividerText.text = "|";

        RectTransform dividerRect = sectionDivider.GetComponent<RectTransform>();
        dividerRect.anchorMin = new Vector2(0f, 1f);
        dividerRect.anchorMax = new Vector2(0f, 1f);
        dividerRect.pivot = new Vector2(0f, 1f);
        dividerRect.sizeDelta = new Vector2(16f, 30f);
        dividerRect.anchoredPosition = new Vector2(294f, -14f);

        Button ratioButton = CreateButton(parent, "RatioMode", "RATIO FULL", new Vector2(0f, 0f), new Vector2(122f, 30f), delegate { ToggleTileRatio(); }, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-314f, -16f));
        ratioButtonText = GetButtonLabel(ratioButton.gameObject);
        if (ratioButtonText != null)
        {
            ratioButtonText.text = "RATIO FULL";
        }

        globalQualityButton = CreateButton(parent, "GlobalQuality", "Q ALL", new Vector2(0f, 0f), new Vector2(106f, 30f), delegate
        {
            OpenGlobalDropdown(parent, "GlobalQualityDropdown", 324f, -16f, 106f, new[] { "LOW", "MED", "HIGH" }, delegate (int option)
            {
                ApplyQualityAll(option);
            });
        });
        globalQualityText = GetButtonLabel(globalQualityButton.gameObject);
        RectTransform globalQualityRect = globalQualityButton.GetComponent<RectTransform>();
        globalQualityRect.anchoredPosition = new Vector2(324f, -16f);

        globalFpsButton = CreateButton(parent, "GlobalFps", "FPS ALL", new Vector2(0f, 0f), new Vector2(106f, 30f), delegate
        {
            OpenGlobalDropdown(parent, "GlobalFpsDropdown", 442f, -16f, 106f, new[] { "5", "8", "12", "20", "24", "30" }, delegate (int option)
            {
                ApplyFpsAll(option);
            });
        });
        globalFpsText = GetButtonLabel(globalFpsButton.gameObject);
        RectTransform globalFpsRect = globalFpsButton.GetComponent<RectTransform>();
        globalFpsRect.anchoredPosition = new Vector2(442f, -16f);

        globalPipButton = CreateButton(parent, "GlobalPip", "PIP ALL", new Vector2(0f, 0f), new Vector2(106f, 30f), delegate
        {
            OpenGlobalDropdown(parent, "GlobalPipDropdown", 560f, -16f, 106f, new[] { "PIP 1", "PIP 2", "PIP 3" }, delegate (int option)
            {
                ApplyPipAll(option);
            });
        });
        globalPipText = GetButtonLabel(globalPipButton.gameObject);
        RectTransform globalPipRect = globalPipButton.GetComponent<RectTransform>();
        globalPipRect.anchoredPosition = new Vector2(560f, -16f);

        globalZoomButton = CreateButton(parent, "GlobalZoom", "ZOOM ALL", new Vector2(0f, 0f), new Vector2(122f, 30f), delegate
        {
            OpenGlobalDropdown(parent, "GlobalZoomDropdown", 678f, -16f, 122f, new[] { "ZOOM 1", "ZOOM 2", "ZOOM 3", "ZOOM 4", "ZOOM 5", "ZOOM 6", "ZOOM 7" }, delegate (int option)
            {
                ApplyZoomAll(option);
            });
        });
        globalZoomText = GetButtonLabel(globalZoomButton.gameObject);
        RectTransform globalZoomRect = globalZoomButton.GetComponent<RectTransform>();
        globalZoomRect.anchoredPosition = new Vector2(678f, -16f);

        globalAutoButton = CreateButton(parent, "GlobalAuto", "AUTO ON", new Vector2(812f, -16f), new Vector2(106f, 30f), delegate
        {
            ToggleAutoAll();
        });
        globalAutoText = GetButtonLabel(globalAutoButton.gameObject);

        globalRouteButton = CreateButton(parent, "GlobalRoute", "ROUTE OFF", new Vector2(930f, -16f), new Vector2(118f, 30f), delegate
        {
            CycleRouteAll();
        });
        globalRouteText = GetButtonLabel(globalRouteButton.gameObject);

        RefreshGlobalAutoText();
        RefreshGlobalRouteText();

        CreateButton(parent, "Grid4", "4", new Vector2(710f, 16f), new Vector2(46f, 30f), delegate { SetGrid(4); }, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-246f, -16f));
        CreateButton(parent, "Grid6", "6", new Vector2(764f, 16f), new Vector2(46f, 30f), delegate { SetGrid(6); }, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-194f, -16f));
        CreateButton(parent, "Grid9", "9", new Vector2(818f, 16f), new Vector2(46f, 30f), delegate { SetGrid(9); }, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-142f, -16f));

        CreateButton(parent, "PrevPage", "<", new Vector2(0f, 0f), new Vector2(34f, 30f), delegate { ChangePage(-1); }, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-96f, -16f));
        CreateButton(parent, "NextPage", ">", new Vector2(0f, 0f), new Vector2(34f, 30f), delegate { ChangePage(1); }, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -16f));

        GameObject pageObj = new GameObject("PageText");
        pageObj.transform.SetParent(parent, false);
        pageText = pageObj.AddComponent<Text>();
        pageText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        pageText.fontSize = 16;
        pageText.alignment = TextAnchor.MiddleCenter;
        pageText.color = Color.white;

        RectTransform pageRect = pageObj.GetComponent<RectTransform>();
        pageRect.anchorMin = new Vector2(1f, 1f);
        pageRect.anchorMax = new Vector2(1f, 1f);
        pageRect.pivot = new Vector2(1f, 1f);
        pageRect.sizeDelta = new Vector2(42f, 28f);
        pageRect.anchoredPosition = new Vector2(-54f, -16f);
    }

    private void ToggleTileRatio()
    {
        forceTileAspect16x9 = !forceTileAspect16x9;

        if (ratioButtonText != null)
        {
            ratioButtonText.text = forceTileAspect16x9 ? "RATIO 16:9" : "RATIO FULL";
        }

        layoutDirty = true;
    }

    private void OpenGlobalDropdown(Transform parent, string panelName, float sourceX, float sourceY, float sourceWidth, string[] options, System.Action<int> onSelect)
    {
        if (activeGlobalDropdown != null && activeGlobalDropdownName == panelName)
        {
            Destroy(activeGlobalDropdown);
            activeGlobalDropdown = null;
            activeGlobalDropdownName = null;
            return;
        }

        if (activeGlobalDropdown != null)
        {
            Destroy(activeGlobalDropdown);
            activeGlobalDropdown = null;
            activeGlobalDropdownName = null;
        }

        GameObject panelObj = new GameObject(panelName);
        panelObj.transform.SetParent(parent, false);
        activeGlobalDropdown = panelObj;
        activeGlobalDropdownName = panelName;

        Image panelBg = panelObj.AddComponent<Image>();
        panelBg.color = new Color(0f, 0f, 0f, 0.92f);

        float panelWidth = Mathf.Max(132f, sourceWidth + 18f);
        float rowHeight = 28f;
        float panelHeight = options.Length * (rowHeight + 4f) + 8f;

        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);
        panelRect.anchoredPosition = new Vector2(sourceX, sourceY - 36f);

        for (int i = 0; i < options.Length; i++)
        {
            int localOption = i;
            Button optionButton = CreateButton(panelObj.transform, "Option_" + i, options[i], Vector2.zero, new Vector2(panelWidth - 8f, rowHeight), delegate
            {
                onSelect(localOption);
                if (activeGlobalDropdown != null)
                {
                    Destroy(activeGlobalDropdown);
                    activeGlobalDropdown = null;
                    activeGlobalDropdownName = null;
                }
            }, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(4f, -4f - (rowHeight + 4f) * i));
            optionButton.image.color = new Color(0.15f, 0.35f, 0.65f, 0.94f);
        }

        panelObj.transform.SetAsLastSibling();
    }

    private void ApplyQualityAll(int option)
    {
        if (cameraQuality == null)
        {
            return;
        }

        QualityPreset selected = QualityPreset.Low;
        if (option == 1)
        {
            selected = QualityPreset.Medium;
        }
        else if (option >= 2)
        {
            selected = QualityPreset.High;
        }

        for (int i = 0; i < cameraQuality.Length; i++)
        {
            cameraQuality[i] = selected;
            autoDowngradedState[i] = false;
            RecreateRenderTargets(i);
            RefreshTileFeeds(i);
            RefreshMenuText(i);
        }

        if (globalQualityText != null)
        {
            globalQualityText.text = "Q " + QualityToString(selected);
        }
    }

    private void ApplyFpsAll(int option)
    {
        if (fpsCap == null || option < 0 || option >= FpsOptions.Length)
        {
            return;
        }

        int selected = FpsOptions[option];
        for (int i = 0; i < fpsCap.Length; i++)
        {
            fpsCap[i] = selected;
            RefreshMenuText(i);
        }

        if (globalFpsText != null)
        {
            globalFpsText.text = "FPS " + selected;
        }
    }

    private void ApplyPipAll(int option)
    {
        if (pipScaleIndex == null)
        {
            return;
        }

        int selected = Mathf.Clamp(option, 0, PipBaseSizes.Length - 1);
        for (int i = 0; i < pipScaleIndex.Length; i++)
        {
            pipScaleIndex[i] = selected;
            UpdateSmallViewRect(i);
            RefreshMenuText(i);
        }

        if (globalPipText != null)
        {
            globalPipText.text = "PIP " + (selected + 1);
        }
    }

    private void ApplyZoomAll(int option)
    {
        if (mapZoomIndex == null)
        {
            return;
        }

        int selected = Mathf.Clamp(option, 0, MapZoomSizes.Length - 1);
        for (int i = 0; i < mapZoomIndex.Length; i++)
        {
            mapZoomIndex[i] = selected;
            RefreshTileFeeds(i);
            RefreshMenuText(i);
        }

        if (globalZoomText != null)
        {
            globalZoomText.text = "ZOOM " + (selected + 1);
        }
    }

    private bool AreAllAutoEnabled()
    {
        if (autoQualityEnabled == null || autoQualityEnabled.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < autoQualityEnabled.Length; i++)
        {
            if (!autoQualityEnabled[i])
            {
                return false;
            }
        }

        return true;
    }

    private void RefreshGlobalAutoText()
    {
        if (globalAutoText != null)
        {
            globalAutoText.text = AreAllAutoEnabled() ? "AUTO ON" : "AUTO OFF";
        }
    }

    private void ToggleAutoAll()
    {
        if (autoQualityEnabled == null)
        {
            return;
        }

        bool targetState = !AreAllAutoEnabled();
        for (int i = 0; i < autoQualityEnabled.Length; i++)
        {
            autoQualityEnabled[i] = targetState;
            if (!targetState)
            {
                autoDowngradedState[i] = false;
            }

            RefreshMenuText(i);
        }

        RefreshGlobalAutoText();
    }

    private string GetRouteModeLabel(int mode)
    {
        if (mode == RouteModeMain)
        {
            return "MAIN";
        }

        if (mode == RouteModeEmergency)
        {
            return "EMERG";
        }

        return "OFF";
    }

    private bool AreAllRouteMode(int mode)
    {
        if (routeMode == null || routeMode.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < routeMode.Length; i++)
        {
            if (routeMode[i] != mode)
            {
                return false;
            }
        }

        return true;
    }

    private int GetNextGlobalRouteMode()
    {
        if (AreAllRouteMode(RouteModeOff))
        {
            return RouteModeMain;
        }

        if (AreAllRouteMode(RouteModeMain))
        {
            return RouteModeEmergency;
        }

        return RouteModeOff;
    }

    private void RefreshGlobalRouteText()
    {
        if (globalRouteText == null || routeMode == null || routeMode.Length == 0)
        {
            return;
        }

        if (AreAllRouteMode(RouteModeMain))
        {
            globalRouteText.text = "ROUTE MAIN";
            return;
        }

        if (AreAllRouteMode(RouteModeEmergency))
        {
            globalRouteText.text = "ROUTE EMERG";
            return;
        }

        globalRouteText.text = "ROUTE OFF";
    }

    private void ApplyRouteModeAll(int mode)
    {
        if (routeMode == null)
        {
            return;
        }

        int selected = Mathf.Clamp(mode, RouteModeOff, RouteModeEmergency);
        for (int i = 0; i < routeMode.Length; i++)
        {
            routeMode[i] = selected;
            RefreshMenuText(i);
        }

        RefreshGlobalRouteText();
    }

    private void CycleRouteAll()
    {
        ApplyRouteModeAll(GetNextGlobalRouteMode());
    }

    private void CreateTooltip(Transform parent)
    {
        GameObject tooltipObj = new GameObject("StatusTooltip");
        tooltipObj.transform.SetParent(parent, false);

        Image bg = tooltipObj.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.86f);

        tooltipRect = tooltipObj.GetComponent<RectTransform>();
        tooltipRect.anchorMin = new Vector2(0.5f, 0.5f);
        tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
        tooltipRect.pivot = new Vector2(0f, 1f);
        tooltipRect.sizeDelta = new Vector2(280f, 28f);
        tooltipRect.anchoredPosition = new Vector2(-300f, 200f);

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(tooltipObj.transform, false);
        tooltipText = textObj.AddComponent<Text>();
        tooltipText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        tooltipText.fontSize = 14;
        tooltipText.alignment = TextAnchor.MiddleLeft;
        tooltipText.color = Color.white;

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 0f);
        textRect.offsetMax = new Vector2(-8f, 0f);

        tooltipObj.SetActive(false);
    }

    private void CreateTile(int index, string playerName)
    {
        GameObject tile = new GameObject("Tile_" + index);
        tile.transform.SetParent(gridRoot, false);
        tileObjects[index] = tile;

        Image bg = tile.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.06f, 0.06f, 0.92f);

        GameObject largeObj = new GameObject("LargeView");
        largeObj.transform.SetParent(tile.transform, false);
        RawImage largeImage = largeObj.AddComponent<RawImage>();
        largeImage.color = Color.white;
        Material tileFeedMaterial = GetOpaqueFeedMaterial();
        if (tileFeedMaterial != null)
        {
            largeImage.material = tileFeedMaterial;
        }
        largeImages[index] = largeImage;

        RectTransform largeRect = largeObj.GetComponent<RectTransform>();
        largeRect.anchorMin = Vector2.zero;
        largeRect.anchorMax = Vector2.one;
        largeRect.offsetMin = Vector2.zero;
        largeRect.offsetMax = Vector2.zero;

        Button largeButton = largeObj.AddComponent<Button>();
        int localIndex = index;
        largeButton.onClick.AddListener(delegate { OpenFocused(localIndex); });

        GameObject smallObj = new GameObject("SmallView");
        smallObj.transform.SetParent(tile.transform, false);
        RawImage smallImage = smallObj.AddComponent<RawImage>();
        smallImage.color = Color.white;
        if (tileFeedMaterial != null)
        {
            smallImage.material = tileFeedMaterial;
        }
        smallImages[index] = smallImage;

        RectTransform smallRect = smallObj.GetComponent<RectTransform>();
        smallRect.anchorMin = new Vector2(1f, 0f);
        smallRect.anchorMax = new Vector2(1f, 0f);
        smallRect.pivot = new Vector2(1f, 0f);
        smallRect.anchoredPosition = new Vector2(-8f, 8f);
        smallViewRects[index] = smallRect;

        RectTransform largeRouteRoot = CreateRouteOverlayRoot(largeObj.transform, "RouteOverlayLarge");
        RectTransform smallRouteRoot = CreateRouteOverlayRoot(smallObj.transform, "RouteOverlaySmall");
        largeMapOverlayRoots[index] = largeRouteRoot;
        smallMapOverlayRoots[index] = smallRouteRoot;

        Button smallButton = smallObj.AddComponent<Button>();
        smallButton.onClick.AddListener(delegate { SwapLargeAndSmall(localIndex); });

        Text nameText = CreateOverlayText(tile.transform, "Name", playerName, new Vector2(12f, -10f), TextAnchor.UpperLeft, 22);
        playerNameTexts[index] = nameText;
        MakePlayerNameEditable(nameText, index);

        GameObject badgeObj = new GameObject("StatusBadge");
        badgeObj.transform.SetParent(tile.transform, false);
        Text badge = badgeObj.AddComponent<Text>();
        badge.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        badge.fontSize = 16;
        badge.alignment = TextAnchor.MiddleRight;
        badge.color = Color.white;

        RectTransform badgeRect = badge.GetComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(1f, 1f);
        badgeRect.anchorMax = new Vector2(1f, 1f);
        badgeRect.pivot = new Vector2(1f, 1f);
        badgeRect.sizeDelta = new Vector2(120f, 20f);
        badgeRect.anchoredPosition = new Vector2(-8f, -40f);

        badge.gameObject.SetActive(false);
        statusBadges[index] = badge;

        Button powerButton = CreateButton(tile.transform, "PowerButton", "ON", new Vector2(0f, 0f), new Vector2(52f, 30f), delegate
        {
            ToggleCameraEnabled(localIndex);
        }, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-46f, -6f));
        powerButton.image.color = new Color(0f, 0f, 0f, 0.8f);
        powerButtonTexts[index] = GetButtonLabel(powerButton.gameObject);

        CreateHamburger(tile.transform, localIndex);
        UpdateRouteOverlayVisibility(localIndex);
    }

    private RectTransform CreateRouteOverlayRoot(Transform parent, string objectName)
    {
        GameObject overlayObj = new GameObject(objectName);
        overlayObj.transform.SetParent(parent, false);

        RectTransform overlayRect = overlayObj.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        return overlayRect;
    }

    private void MakePlayerNameEditable(Text nameText, int index)
    {
        if (nameText == null)
        {
            return;
        }

        GameObject clickObj = new GameObject("NameHitbox");
        clickObj.transform.SetParent(nameText.transform, false);

        RectTransform nameRect = nameText.rectTransform;
        RectTransform clickRect = clickObj.AddComponent<RectTransform>();
        clickRect.anchorMin = Vector2.zero;
        clickRect.anchorMax = Vector2.one;
        clickRect.pivot = nameRect.pivot;
        clickRect.offsetMin = Vector2.zero;
        clickRect.offsetMax = Vector2.zero;

        Image clickArea = clickObj.AddComponent<Image>();
        clickArea.color = new Color(0f, 0f, 0f, 0f);

        Button button = clickObj.AddComponent<Button>();
        int localIndex = index;
        button.onClick.AddListener(delegate
        {
            OpenRenamePanel(localIndex);
        });
    }

    private void CreateRenamePanel(Transform parent)
    {
        GameObject panelObj = new GameObject("RenamePanel");
        panelObj.transform.SetParent(parent, false);
        renamePanel = panelObj;

        Image panelBg = panelObj.AddComponent<Image>();
        panelBg.color = new Color(0f, 0f, 0f, 0.9f);

        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(460f, 86f);
        panelRect.anchoredPosition = Vector2.zero;

        GameObject inputObj = new GameObject("RenameInput");
        inputObj.transform.SetParent(panelObj.transform, false);
        Image inputBg = inputObj.AddComponent<Image>();
        inputBg.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);

        RectTransform inputRect = inputObj.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0f, 0.5f);
        inputRect.anchorMax = new Vector2(1f, 0.5f);
        inputRect.pivot = new Vector2(0.5f, 0.5f);
        inputRect.offsetMin = new Vector2(10f, -16f);
        inputRect.offsetMax = new Vector2(-96f, 16f);

        renameInput = inputObj.AddComponent<InputField>();
        renameInput.lineType = InputField.LineType.SingleLine;
        renameInput.characterLimit = 28;

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(inputObj.transform, false);
        Text text = textObj.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 20;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 0f);
        textRect.offsetMax = new Vector2(-10f, 0f);

        GameObject placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(inputObj.transform, false);
        Text placeholder = placeholderObj.AddComponent<Text>();
        placeholder.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        placeholder.fontSize = 18;
        placeholder.alignment = TextAnchor.MiddleLeft;
        placeholder.color = new Color(0.7f, 0.7f, 0.7f, 0.7f);
        placeholder.text = "Name";

        RectTransform placeholderRect = placeholderObj.GetComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = new Vector2(10f, 0f);
        placeholderRect.offsetMax = new Vector2(-10f, 0f);

        renameInput.textComponent = text;
        renameInput.placeholder = placeholder;

        Button confirmButton = CreateButton(panelObj.transform, "RenameConfirm", "OK", Vector2.zero, new Vector2(72f, 32f), delegate
        {
            CommitRename();
        }, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f));
        confirmButton.image.color = new Color(0.18f, 0.4f, 0.75f, 0.95f);

        renameInput.onEndEdit.AddListener(delegate (string _)
        {
            if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
            {
                CommitRename();
            }
        });

        panelObj.SetActive(false);
    }

    private void OpenRenamePanel(int index)
    {
        if (renamePanel == null || renameInput == null || sessionPlayerNames == null || index < 0 || index >= sessionPlayerNames.Length)
        {
            return;
        }

        renamingIndex = index;
        renamePanel.SetActive(true);
        renamePanel.transform.SetAsLastSibling();

        renameInput.text = sessionPlayerNames[index] ?? string.Empty;
        renameInput.ActivateInputField();
        renameInput.Select();
        UpdateCursorState(true);
    }

    private void CommitRename()
    {
        if (renamingIndex < 0 || renameInput == null || sessionPlayerNames == null || renamingIndex >= sessionPlayerNames.Length)
        {
            CloseRenamePanel();
            return;
        }

        string newName = renameInput.text != null ? renameInput.text.Trim() : string.Empty;
        if (!string.IsNullOrEmpty(newName))
        {
            sessionPlayerNames[renamingIndex] = newName;
            if (playerNameTexts != null && renamingIndex < playerNameTexts.Length && playerNameTexts[renamingIndex] != null)
            {
                playerNameTexts[renamingIndex].text = newName;
            }
        }

        CloseRenamePanel();
    }

    private void CloseRenamePanel()
    {
        renamingIndex = -1;
        if (renamePanel != null)
        {
            renamePanel.SetActive(false);
        }

        UpdateCursorState(false);
    }

    private void CaptureCursorState()
    {
        if (capturedCursorState)
        {
            return;
        }

        originalCursorVisible = Cursor.visible;
        originalCursorLockState = Cursor.lockState;
        capturedCursorState = true;
    }

    private void RestoreCursorState()
    {
        if (!capturedCursorState)
        {
            return;
        }

        Cursor.visible = originalCursorVisible;
        Cursor.lockState = originalCursorLockState;
    }

    private void UpdateCursorState(bool renameOpen = false)
    {
        if (!sessionActive)
        {
            return;
        }

        if (renameOpen)
        {
            if (!Cursor.visible)
            {
                Cursor.visible = true;
            }

            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
            }
            return;
        }

        Cursor.visible = originalCursorVisible;
        Cursor.lockState = originalCursorLockState;
    }

    private void CreateHamburger(Transform parent, int index)
    {
        Button menuButton = CreateButton(
            parent,
            "MenuButton",
            "≡",
            new Vector2(0f, 0f),
            new Vector2(36f, 30f),
            delegate { ToggleMenu(index); },
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-6f, -6f));
        menuButton.image.color = new Color(0f, 0f, 0f, 0.8f);
        if (menuButtons != null && index >= 0 && index < menuButtons.Length)
        {
            menuButtons[index] = menuButton;
        }

        GameObject panelObj = new GameObject("MenuPanel");
        panelObj.transform.SetParent(parent, false);
        menuPanels[index] = panelObj;

        Image panelBg = panelObj.AddComponent<Image>();
        panelBg.color = new Color(0f, 0f, 0f, 0.9f);

        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.sizeDelta = new Vector2(164f, 150f);
        panelRect.anchoredPosition = new Vector2(-6f, -42f);

        int localIndex = index;

        Button qualityButton = CreateButton(panelObj.transform, "Quality", "Q", new Vector2(0f, -20f), new Vector2(148f, 28f), delegate
        {
            CycleQuality(localIndex);
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f));
        qualityButtonTexts[index] = GetButtonLabel(qualityButton.gameObject);

        Button fpsButton = CreateButton(panelObj.transform, "Fps", "FPS", new Vector2(0f, -52f), new Vector2(148f, 28f), delegate
        {
            CycleFps(localIndex);
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -52f));
        fpsButtonTexts[index] = GetButtonLabel(fpsButton.gameObject);

        Button pipButton = CreateButton(panelObj.transform, "Pip", "PIP", new Vector2(0f, -84f), new Vector2(148f, 28f), delegate
        {
            CyclePipSize(localIndex);
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f));
        pipButtonTexts[index] = GetButtonLabel(pipButton.gameObject);

        Button zoomButton = CreateButton(panelObj.transform, "MapZoom", "ZOOM", new Vector2(0f, -116f), new Vector2(148f, 28f), delegate
        {
            CycleMapZoom(localIndex);
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -116f));
        zoomButtonTexts[index] = GetButtonLabel(zoomButton.gameObject);

        Button mapTypeButton = CreateButton(panelObj.transform, "MapType", "MAP", new Vector2(0f, -148f), new Vector2(148f, 28f), delegate
        {
            CycleMapType(localIndex);
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -148f));
        mapTypeButtonTexts[index] = GetButtonLabel(mapTypeButton.gameObject);

        Button autoButton = CreateButton(panelObj.transform, "Auto", "AUTO", new Vector2(0f, -180f), new Vector2(148f, 28f), delegate
        {
            autoQualityEnabled[localIndex] = !autoQualityEnabled[localIndex];
            RefreshMenuText(localIndex);
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -180f));
        autoButtonTexts[index] = GetButtonLabel(autoButton.gameObject);

        Button routeButton = CreateButton(panelObj.transform, "Route", "ROUTE OFF", new Vector2(0f, -212f), new Vector2(148f, 28f), delegate
        {
            routeMode[localIndex] = (routeMode[localIndex] + 1) % 3;
            RefreshMenuText(localIndex);
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -212f));
        routeButtonTexts[index] = GetButtonLabel(routeButton.gameObject);

        panelRect.sizeDelta = new Vector2(164f, 244f);
        panelObj.SetActive(false);
    }

    private void CreateFocusedOverlay(Transform parent)
    {
        focusedOverlay = new GameObject("FocusedOverlay");
        focusedOverlay.transform.SetParent(parent, false);

        Image overlayBg = focusedOverlay.AddComponent<Image>();
        overlayBg.color = new Color(0f, 0f, 0f, 0.9f);

        RectTransform overlayRect = focusedOverlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        Button overlayClose = focusedOverlay.AddComponent<Button>();
        overlayClose.onClick.AddListener(delegate { CloseFocused(); });

        GameObject largeObj = new GameObject("FocusedLarge");
        largeObj.transform.SetParent(focusedOverlay.transform, false);
        focusedLargeImage = largeObj.AddComponent<RawImage>();
        Material focusedFeedMaterial = GetOpaqueFeedMaterial();
        if (focusedFeedMaterial != null)
        {
            focusedLargeImage.material = focusedFeedMaterial;
        }

        RectTransform largeRect = largeObj.GetComponent<RectTransform>();
        largeRect.anchorMin = new Vector2(0.03f, 0.06f);
        largeRect.anchorMax = new Vector2(0.97f, 0.94f);
        largeRect.offsetMin = Vector2.zero;
        largeRect.offsetMax = Vector2.zero;

        Button largeClose = largeObj.AddComponent<Button>();
        largeClose.onClick.AddListener(delegate { CloseFocused(); });

        focusedMapOverlayLargeRoot = CreateRouteOverlayRoot(largeObj.transform, "FocusedRouteOverlayLarge");

        GameObject smallObj = new GameObject("FocusedSmall");
        smallObj.transform.SetParent(focusedOverlay.transform, false);
        focusedSmallImage = smallObj.AddComponent<RawImage>();
        if (focusedFeedMaterial != null)
        {
            focusedSmallImage.material = focusedFeedMaterial;
        }

        RectTransform smallRect = smallObj.GetComponent<RectTransform>();
        smallRect.anchorMin = new Vector2(1f, 0f);
        smallRect.anchorMax = new Vector2(1f, 0f);
        smallRect.pivot = new Vector2(1f, 0f);
        smallRect.sizeDelta = new Vector2(288f, 162f);
        smallRect.anchoredPosition = new Vector2(-44f, 44f);

        Button smallSwap = smallObj.AddComponent<Button>();
        smallSwap.onClick.AddListener(delegate
        {
            if (focusedIndex >= 0)
            {
                SwapLargeAndSmall(focusedIndex);
            }
        });

        focusedMapOverlaySmallRoot = CreateRouteOverlayRoot(smallObj.transform, "FocusedRouteOverlaySmall");

        focusedMenuButton = CreateButton(
            focusedOverlay.transform,
            "FocusedMenuButton",
            "≡",
            Vector2.zero,
            new Vector2(40f, 32f),
            delegate { ToggleFocusedMenu(); },
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-16f, -16f));
        focusedMenuButton.image.color = new Color(0f, 0f, 0f, 0.8f);

        focusedMenuPanel = new GameObject("FocusedMenuPanel");
        focusedMenuPanel.transform.SetParent(focusedOverlay.transform, false);
        Image focusedMenuBg = focusedMenuPanel.AddComponent<Image>();
        focusedMenuBg.color = new Color(0f, 0f, 0f, 0.9f);

        RectTransform focusedPanelRect = focusedMenuPanel.GetComponent<RectTransform>();
        focusedPanelRect.anchorMin = new Vector2(1f, 1f);
        focusedPanelRect.anchorMax = new Vector2(1f, 1f);
        focusedPanelRect.pivot = new Vector2(1f, 1f);
        focusedPanelRect.sizeDelta = new Vector2(170f, 246f);
        focusedPanelRect.anchoredPosition = new Vector2(-16f, -54f);

        Button focusedPowerButton = CreateButton(focusedMenuPanel.transform, "FocusedPower", "OFF", new Vector2(0f, -20f), new Vector2(154f, 28f), delegate
        {
            if (focusedIndex < 0)
            {
                return;
            }

            ToggleCameraEnabled(focusedIndex);
            RefreshFocusedMenuText();
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f));
        focusedMenuPowerText = GetButtonLabel(focusedPowerButton.gameObject);

        Button focusedQualityButton = CreateButton(focusedMenuPanel.transform, "FocusedQuality", "Q", new Vector2(0f, -52f), new Vector2(154f, 28f), delegate
        {
            if (focusedIndex < 0)
            {
                return;
            }

            CycleQuality(focusedIndex);
            RefreshFocusedMenuText();
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -52f));
        focusedMenuQualityText = GetButtonLabel(focusedQualityButton.gameObject);

        Button focusedFpsButton = CreateButton(focusedMenuPanel.transform, "FocusedFps", "FPS", new Vector2(0f, -84f), new Vector2(154f, 28f), delegate
        {
            if (focusedIndex < 0)
            {
                return;
            }

            CycleFps(focusedIndex);
            RefreshFocusedMenuText();
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f));
        focusedMenuFpsText = GetButtonLabel(focusedFpsButton.gameObject);

        Button focusedPipButton = CreateButton(focusedMenuPanel.transform, "FocusedPip", "PIP", new Vector2(0f, -116f), new Vector2(154f, 28f), delegate
        {
            if (focusedIndex < 0)
            {
                return;
            }

            CyclePipSize(focusedIndex);
            RefreshFocusedMenuText();
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -116f));
        focusedMenuPipText = GetButtonLabel(focusedPipButton.gameObject);

        Button focusedZoomButton = CreateButton(focusedMenuPanel.transform, "FocusedZoom", "ZOOM", new Vector2(0f, -148f), new Vector2(154f, 28f), delegate
        {
            if (focusedIndex < 0)
            {
                return;
            }

            CycleMapZoom(focusedIndex);
            RefreshFocusedMenuText();
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -148f));
        focusedMenuZoomText = GetButtonLabel(focusedZoomButton.gameObject);

        Button focusedMapTypeButton = CreateButton(focusedMenuPanel.transform, "FocusedMapType", "MAP", new Vector2(0f, -180f), new Vector2(154f, 28f), delegate
        {
            if (focusedIndex < 0)
            {
                return;
            }

            CycleMapType(focusedIndex);
            RefreshFocusedMenuText();
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -180f));
        focusedMenuMapTypeText = GetButtonLabel(focusedMapTypeButton.gameObject);

        Button focusedRouteButton = CreateButton(focusedMenuPanel.transform, "FocusedRoute", "ROUTE OFF", new Vector2(0f, -212f), new Vector2(154f, 28f), delegate
        {
            if (focusedIndex < 0)
            {
                return;
            }

            routeMode[focusedIndex] = (routeMode[focusedIndex] + 1) % 3;
            RefreshMenuText(focusedIndex);
            RefreshFocusedMenuText();
        }, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -212f));
        focusedMenuRouteText = GetButtonLabel(focusedRouteButton.gameObject);

        focusedMenuPanel.SetActive(false);

        focusedOverlay.SetActive(false);
    }

    private void ToggleFocusedMenu()
    {
        if (focusedMenuPanel == null || focusedIndex < 0)
        {
            return;
        }

        bool opening = !focusedMenuPanel.activeSelf;
        focusedMenuPanel.SetActive(opening);
        if (opening)
        {
            RefreshFocusedMenuText();
            focusedMenuPanel.transform.SetAsLastSibling();
        }
    }

    private void RefreshFocusedMenuText()
    {
        if (focusedIndex < 0 || cameraEnabled == null || focusedIndex >= cameraEnabled.Length)
        {
            return;
        }

        int index = focusedIndex;

        if (focusedMenuPowerText != null)
        {
            focusedMenuPowerText.text = cameraEnabled[index] ? "ON" : "OFF";
        }

        if (focusedMenuQualityText != null)
        {
            focusedMenuQualityText.text = "Q " + QualityToString(cameraQuality[index]) + " " + GetPlayerTextureSize(cameraQuality[index]);
        }

        if (focusedMenuFpsText != null)
        {
            focusedMenuFpsText.text = "FPS " + fpsCap[index];
        }

        if (focusedMenuPipText != null)
        {
            focusedMenuPipText.text = "PIP " + (pipScaleIndex[index] + 1);
        }

        if (focusedMenuZoomText != null)
        {
            focusedMenuZoomText.text = "ZOOM " + (mapZoomIndex[index] + 1);
        }

        if (focusedMenuMapTypeText != null)
        {
            focusedMenuMapTypeText.text = "MAP " + GetMapTypeLabel(mapTypeIndex[index]);
        }

        if (focusedMenuRouteText != null)
        {
            focusedMenuRouteText.text = "ROUTE " + GetRouteModeLabel(routeMode[index]);
        }
    }

    private Material GetOpaqueFeedMaterial()
    {
        if (opaqueFeedMaterial != null)
        {
            return opaqueFeedMaterial;
        }

        Shader shader = Shader.Find("Unlit/Texture");
        if (shader == null)
        {
            return null;
        }

        opaqueFeedMaterial = new Material(shader);
        return opaqueFeedMaterial;
    }

    private Text CreateOverlayText(Transform parent, string name, string value, Vector2 anchored, TextAnchor anchor, int fontSize)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        Text text = obj.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = anchor;
        text.text = value;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(420f, 36f);
        rect.anchoredPosition = anchored;

        return text;
    }

    private Button CreateButton(
        Transform parent,
        string objectName,
        string label,
        Vector2 anchored,
        Vector2 size,
        UnityEngine.Events.UnityAction onClick,
        Vector2? anchorMin = null,
        Vector2? anchorMax = null,
        Vector2? anchorPos = null)
    {
        GameObject buttonObj = new GameObject(objectName);
        buttonObj.transform.SetParent(parent, false);

        Image image = buttonObj.AddComponent<Image>();
        image.color = new Color(0.18f, 0.4f, 0.75f, 0.92f);

        Button button = buttonObj.AddComponent<Button>();
        button.onClick.AddListener(onClick);

        RectTransform rect = buttonObj.GetComponent<RectTransform>();
        if (anchorMin.HasValue)
        {
            rect.anchorMin = anchorMin.Value;
            rect.anchorMax = anchorMax ?? anchorMin.Value;
            rect.pivot = rect.anchorMin;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchorPos ?? Vector2.zero;
        }
        else
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchored;
        }

        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(buttonObj.transform, false);

        Text text = labelObj.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 18;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.text = label;

        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        return button;
    }

    private Text GetButtonLabel(GameObject buttonObj)
    {
        Transform label = buttonObj.transform.Find("Label");
        return label != null ? label.GetComponent<Text>() : null;
    }

    private void ToggleMenu(int index)
    {
        for (int i = 0; i < menuPanels.Length; i++)
        {
            if (menuPanels[i] == null)
            {
                continue;
            }

            bool makeActive = i == index && !menuPanels[i].activeSelf;
            menuPanels[i].SetActive(makeActive);
        }
    }

    private void CloseAllMenus()
    {
        if (menuPanels == null)
        {
            return;
        }

        for (int i = 0; i < menuPanels.Length; i++)
        {
            if (menuPanels[i] != null)
            {
                menuPanels[i].SetActive(false);
            }
        }
    }

    private bool IsPointerInsideRect(RectTransform rect, Vector2 screenPos)
    {
        return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, null);
    }

    private void HandleOpenPopupDismiss()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
        {
            return;
        }

        bool hasOpenPopup = activeGlobalDropdown != null;
        if (!hasOpenPopup && menuPanels != null)
        {
            for (int i = 0; i < menuPanels.Length; i++)
            {
                if (menuPanels[i] != null && menuPanels[i].activeSelf)
                {
                    hasOpenPopup = true;
                    break;
                }
            }
        }

        if (!hasOpenPopup)
        {
            return;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();

        if (activeGlobalDropdown != null)
        {
            RectTransform dropRect = activeGlobalDropdown.GetComponent<RectTransform>();
            if (IsPointerInsideRect(dropRect, mousePos))
            {
                return;
            }
        }

        if (menuPanels != null)
        {
            for (int i = 0; i < menuPanels.Length; i++)
            {
                if (menuPanels[i] == null || !menuPanels[i].activeSelf)
                {
                    continue;
                }

                RectTransform panelRect = menuPanels[i].GetComponent<RectTransform>();
                if (IsPointerInsideRect(panelRect, mousePos))
                {
                    return;
                }
            }
        }

        if (menuButtons != null)
        {
            for (int i = 0; i < menuButtons.Length; i++)
            {
                if (menuButtons[i] == null)
                {
                    continue;
                }

                RectTransform btnRect = menuButtons[i].GetComponent<RectTransform>();
                if (IsPointerInsideRect(btnRect, mousePos))
                {
                    return;
                }
            }
        }

        Button[] globalButtons = { globalQualityButton, globalFpsButton, globalAutoButton, globalPipButton, globalZoomButton, globalRouteButton };
        for (int i = 0; i < globalButtons.Length; i++)
        {
            if (globalButtons[i] == null)
            {
                continue;
            }

            RectTransform btnRect = globalButtons[i].GetComponent<RectTransform>();
            if (IsPointerInsideRect(btnRect, mousePos))
            {
                return;
            }
        }

        CloseAllMenus();
        if (activeGlobalDropdown != null)
        {
            Destroy(activeGlobalDropdown);
            activeGlobalDropdown = null;
            activeGlobalDropdownName = null;
        }
    }

    private void SetGrid(int slots)
    {
        gridSlots = Mathf.Clamp(slots, 4, 9);
        currentPage = 0;
        layoutDirty = true;
    }

    private void ToggleLocalBox()
    {
        if (localPlayerIndex < 0 || tileObjects == null || localPlayerIndex >= tileObjects.Length || tileObjects[localPlayerIndex] == null)
        {
            return;
        }

        hideSelfCamera[localPlayerIndex] = !hideSelfCamera[localPlayerIndex];

        if (cameraEnabled != null && localPlayerIndex < cameraEnabled.Length)
        {
            cameraEnabled[localPlayerIndex] = !hideSelfCamera[localPlayerIndex];
            if (!cameraEnabled[localPlayerIndex])
            {
                autoDowngradedState[localPlayerIndex] = false;
            }
        }

        if (hideSelfCamera[localPlayerIndex] && focusedIndex == localPlayerIndex)
        {
            CloseFocused();
        }

        RefreshTileFeeds(localPlayerIndex);
        RefreshMenuText(localPlayerIndex);

        if (localBoxToggleText != null)
        {
            localBoxToggleText.text = hideSelfCamera[localPlayerIndex] ? "SHOW MY BOX" : "HIDE MY BOX";
        }

        layoutDirty = true;
    }

    private void ToggleCameraEnabled(int index)
    {
        if (cameraEnabled == null || index < 0 || index >= cameraEnabled.Length)
        {
            return;
        }

        cameraEnabled[index] = !cameraEnabled[index];
        if (cameraEnabled[index])
        {
            if (mapTypeIndex != null && index < mapTypeIndex.Length)
            {
                mapTypeIndex[index] = 2;
            }

            if (mapPrimedOnce != null && mapPrimeStage != null && mapPrimeAt != null && index < mapPrimedOnce.Length && index < mapPrimeStage.Length && index < mapPrimeAt.Length && !mapPrimedOnce[index])
            {
                mapPrimeStage[index] = 1;
                mapPrimeAt[index] = Time.unscaledTime + 0.05f;
            }

            if (nextMapRender != null && index < nextMapRender.Length)
            {
                nextMapRender[index] = 0f;
            }

            if (nextPlayerRender != null && index < nextPlayerRender.Length)
            {
                nextPlayerRender[index] = 0f;
            }

            if (playerCameras != null && index < playerCameras.Length && playerCameras[index] != null)
            {
                playerCameras[index].Render();
            }

            if (mapCameras != null && index < mapCameras.Length && mapCameras[index] != null)
            {
                mapCameras[index].Render();
            }
        }
        else
        {
            autoDowngradedState[index] = false;
        }

        RefreshTileFeeds(index);
        RefreshMenuText(index);
    }

    private void ForcePotatoMode()
    {
        if (cameraQuality == null || fpsCap == null)
        {
            return;
        }

        for (int i = 0; i < cameraQuality.Length; i++)
        {
            cameraQuality[i] = QualityPreset.Low;
            fpsCap[i] = 5;
            autoQualityEnabled[i] = false;
            autoDowngradedState[i] = false;
            if (pipScaleIndex != null && i < pipScaleIndex.Length)
            {
                pipScaleIndex[i] = 0;
            }
            if (mapZoomIndex != null && i < mapZoomIndex.Length)
            {
                mapZoomIndex[i] = 0;
            }

            RecreateRenderTargets(i);
            RefreshTileFeeds(i);
            RefreshMenuText(i);
        }

        if (potatoButtonText != null)
        {
            potatoButtonText.text = "POTATO";
        }
    }

    private void ChangePage(int delta)
    {
        if (tileObjects == null || tileObjects.Length == 0)
        {
            return;
        }

        int pageCount = Mathf.Max(1, Mathf.CeilToInt(tileObjects.Length / (float)gridSlots));
        currentPage = Mathf.Clamp(currentPage + delta, 0, pageCount - 1);
        layoutDirty = true;
    }

    private void ApplyGridLayout()
    {
        if (activeGlobalDropdown != null)
        {
            Destroy(activeGlobalDropdown);
            activeGlobalDropdown = null;
            activeGlobalDropdownName = null;
        }

        if (tileObjects == null || gridRoot == null)
        {
            return;
        }

        List<int> visibleTiles = new List<int>();
        for (int i = 0; i < tileObjects.Length; i++)
        {
            if (tileObjects[i] == null)
            {
                continue;
            }

            if (i == localPlayerIndex && hideSelfCamera[i])
            {
                continue;
            }

            visibleTiles.Add(i);
        }

        int columns;
        int rows;

        if (gridSlots == 1)
        {
            columns = 1;
            rows = 1;
        }
        else if (gridSlots == 4)
        {
            columns = 2;
            rows = 2;
        }
        else if (gridSlots == 6)
        {
            columns = 3;
            rows = 2;
        }
        else
        {
            columns = 3;
            rows = 3;
        }

        int pageCount = Mathf.Max(1, Mathf.CeilToInt(visibleTiles.Count / (float)gridSlots));
        currentPage = Mathf.Clamp(currentPage, 0, pageCount - 1);
        int startIndex = currentPage * gridSlots;
        int shown = Mathf.Min(gridSlots, Mathf.Max(0, visibleTiles.Count - startIndex));

        if (pageText != null)
        {
            pageText.text = (currentPage + 1) + "/" + pageCount;
        }

        float width = gridRoot.rect.width;
        float height = gridRoot.rect.height;
        if (width < 10f || height < 10f)
        {
            return;
        }

        float gap = 14f;
        float cellWidth = (width - gap * (columns - 1f)) / columns;
        float cellHeight = (height - gap * (rows - 1f)) / rows;

        for (int i = 0; i < tileObjects.Length; i++)
        {
            if (tileObjects[i] == null)
            {
                continue;
            }

            tileObjects[i].SetActive(false);
        }

        for (int local = 0; local < shown; local++)
        {
            int i = visibleTiles[startIndex + local];
            GameObject tile = tileObjects[i];
            if (tile == null)
            {
                continue;
            }

            tile.SetActive(true);

            int row = local / columns;
            int col = local % columns;

            float tileWidth = cellWidth;
            float tileHeight = cellHeight;

            if (forceTileAspect16x9)
            {
                const float targetAspect = 16f / 9f;
                float cellAspect = cellWidth / Mathf.Max(1f, cellHeight);

                if (cellAspect > targetAspect)
                {
                    tileWidth = cellHeight * targetAspect;
                }
                else
                {
                    tileHeight = cellWidth / targetAspect;
                }
            }

            float offsetX = (cellWidth - tileWidth) * 0.5f;
            float offsetY = (cellHeight - tileHeight) * 0.5f;

            RectTransform rect = tile.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(tileWidth, tileHeight);
            rect.anchoredPosition = new Vector2(col * (cellWidth + gap) + offsetX, -row * (cellHeight + gap) - offsetY);

            UpdateSmallViewRect(i);
        }

        if (focusedIndex >= 0 && focusedIndex < tileObjects.Length && !tileObjects[focusedIndex].activeSelf)
        {
            CloseFocused();
        }

        layoutDirty = false;
    }

    private void OpenFocused(int index)
    {
        if (index < 0 || index >= playerTextures.Length)
        {
            return;
        }

        focusedIndex = index;
        focusedPrimaryIsMap = primaryIsMap[index];

        UpdateFocusedTextures();
        if (focusedMenuPanel != null)
        {
            focusedMenuPanel.SetActive(false);
        }
        RefreshFocusedMenuText();
        focusedOverlay.SetActive(true);
        UpdateRouteOverlayVisibility(index);
    }

    private void CloseFocused()
    {
        int previousFocusedIndex = focusedIndex;
        if (focusedMenuPanel != null)
        {
            focusedMenuPanel.SetActive(false);
        }
        focusedIndex = -1;
        focusedOverlay.SetActive(false);

        if (previousFocusedIndex >= 0)
        {
            UpdateRouteOverlayVisibility(previousFocusedIndex);
        }
    }

    private void SwapLargeAndSmall(int index)
    {
        if (index < 0 || index >= primaryIsMap.Length)
        {
            return;
        }

        primaryIsMap[index] = !primaryIsMap[index];
        RefreshTileFeeds(index);

        if (focusedIndex == index)
        {
            focusedPrimaryIsMap = primaryIsMap[index];
            UpdateFocusedTextures();
            UpdateRouteOverlayVisibility(index);
        }
    }

    private void UpdateFocusedTextures()
    {
        if (focusedIndex < 0 || focusedIndex >= playerTextures.Length)
        {
            return;
        }

        if (focusedSmallImage != null)
        {
            focusedSmallImage.gameObject.SetActive(true);
        }

        if (focusedPrimaryIsMap)
        {
            focusedLargeImage.texture = mapTextures[focusedIndex];
            focusedSmallImage.texture = playerTextures[focusedIndex];
        }
        else
        {
            focusedLargeImage.texture = playerTextures[focusedIndex];
            focusedSmallImage.texture = mapTextures[focusedIndex];
        }
    }

    private void CycleQuality(int index)
    {
        if (cameraQuality[index] == QualityPreset.Low)
        {
            cameraQuality[index] = QualityPreset.Medium;
        }
        else if (cameraQuality[index] == QualityPreset.Medium)
        {
            cameraQuality[index] = QualityPreset.High;
        }
        else
        {
            cameraQuality[index] = QualityPreset.Low;
        }

        RecreateRenderTargets(index);
        autoDowngradedState[index] = false;
        RefreshTileFeeds(index);
        RefreshMenuText(index);
    }

    private void CycleFps(int index)
    {
        int current = fpsCap[index];
        int selected = FpsOptions[0];

        for (int i = 0; i < FpsOptions.Length; i++)
        {
            if (FpsOptions[i] == current)
            {
                selected = FpsOptions[(i + 1) % FpsOptions.Length];
                break;
            }
        }

        fpsCap[index] = selected;
        RefreshMenuText(index);
    }

    private void CyclePipSize(int index)
    {
        pipScaleIndex[index] = (pipScaleIndex[index] + 1) % PipBaseSizes.Length;
        UpdateSmallViewRect(index);
        RefreshMenuText(index);
    }

    private void RefreshMenuText(int index)
    {
        if (powerButtonTexts != null && index < powerButtonTexts.Length && powerButtonTexts[index] != null)
        {
            powerButtonTexts[index].text = cameraEnabled[index] ? "ON" : "OFF";
        }

        if (qualityButtonTexts != null && index < qualityButtonTexts.Length && qualityButtonTexts[index] != null)
        {
            qualityButtonTexts[index].text = "Q " + QualityToString(cameraQuality[index]) + " " + GetPlayerTextureSize(cameraQuality[index]);
        }

        if (fpsButtonTexts != null && index < fpsButtonTexts.Length && fpsButtonTexts[index] != null)
        {
            fpsButtonTexts[index].text = "FPS " + fpsCap[index];
        }

        if (autoButtonTexts != null && index < autoButtonTexts.Length && autoButtonTexts[index] != null)
        {
            autoButtonTexts[index].text = autoQualityEnabled[index] ? "AUTO ON" : "AUTO OFF";
        }

        if (pipButtonTexts != null && index < pipButtonTexts.Length && pipButtonTexts[index] != null)
        {
            pipButtonTexts[index].text = "PIP " + (pipScaleIndex[index] + 1);
        }

        if (zoomButtonTexts != null && index < zoomButtonTexts.Length && zoomButtonTexts[index] != null)
        {
            zoomButtonTexts[index].text = "ZOOM " + (mapZoomIndex[index] + 1);
        }

        if (mapTypeButtonTexts != null && index < mapTypeButtonTexts.Length && mapTypeButtonTexts[index] != null)
        {
            mapTypeButtonTexts[index].text = "MAP " + GetMapTypeLabel(mapTypeIndex[index]);
        }

        if (routeButtonTexts != null && index < routeButtonTexts.Length && routeButtonTexts[index] != null)
        {
            routeButtonTexts[index].text = "ROUTE " + GetRouteModeLabel(routeMode[index]);
        }

        if (localBoxToggleText != null)
        {
            bool localHidden = localPlayerIndex >= 0 && hideSelfCamera != null && localPlayerIndex < hideSelfCamera.Length && hideSelfCamera[localPlayerIndex];
            localBoxToggleText.text = localHidden ? "SHOW MY BOX" : "HIDE MY BOX";
        }

        RefreshGlobalAutoText();
        RefreshGlobalRouteText();

        if (focusedIndex == index)
        {
            RefreshFocusedMenuText();
        }

        if (ratioButtonText != null)
        {
            ratioButtonText.text = forceTileAspect16x9 ? "RATIO 16:9" : "RATIO FULL";
        }
    }

    private int FindLocalPlayerIndex(PlayerControllerB[] players)
    {
        if (StartOfRound.Instance == null || StartOfRound.Instance.localPlayerController == null || players == null)
        {
            return -1;
        }

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == StartOfRound.Instance.localPlayerController)
            {
                return i;
            }
        }

        return -1;
    }

    private string QualityToString(QualityPreset preset)
    {
        if (preset == QualityPreset.Low)
        {
            return "LOW";
        }

        if (preset == QualityPreset.Medium)
        {
            return "MED";
        }

        return "HIGH";
    }

    private float GetIntervalFromFps(int fps)
    {
        return fps <= 0 ? 0.2f : 1f / fps;
    }

    private int GetPlayerTextureSize(QualityPreset preset)
    {
        if (preset == QualityPreset.Low)
        {
            return 96;
        }

        if (preset == QualityPreset.Medium)
        {
            return 192;
        }

        return 384;
    }

    private int GetMapTextureSize(QualityPreset preset)
    {
        if (preset == QualityPreset.Low)
        {
            return 64;
        }

        if (preset == QualityPreset.Medium)
        {
            return 96;
        }

        return 192;
    }

    private void UpdateSmallViewRect(int index)
    {
        if (smallViewRects == null || index < 0 || index >= smallViewRects.Length || smallViewRects[index] == null)
        {
            return;
        }

        RectTransform smallRect = smallViewRects[index];
        Vector2 desired = PipBaseSizes[pipScaleIndex[index]];

        RectTransform tileRect = tileObjects != null && index < tileObjects.Length && tileObjects[index] != null
            ? tileObjects[index].GetComponent<RectTransform>()
            : null;

        if (tileRect != null)
        {
            float maxWidth = Mathf.Max(90f, tileRect.rect.width * 0.78f);
            float maxHeight = Mathf.Max(70f, tileRect.rect.height * 0.78f);
            float widthScale = maxWidth / desired.x;
            float heightScale = maxHeight / desired.y;
            float scale = Mathf.Min(1f, widthScale, heightScale);
            desired *= scale;
        }

        smallRect.sizeDelta = desired;
    }

    private void CycleMapZoom(int index)
    {
        mapZoomIndex[index] = (mapZoomIndex[index] + 1) % MapZoomSizes.Length;
        RefreshTileFeeds(index);
        RefreshMenuText(index);
    }

    private void CycleMapType(int index)
    {
        mapTypeIndex[index] = (mapTypeIndex[index] + 1) % 3;
        RefreshTileFeeds(index);
        RefreshMenuText(index);
    }

    private string GetMapTypeLabel(int mapType)
    {
        if (mapType == 0)
        {
            return "RADAR";
        }

        if (mapType == 1)
        {
            return "ALT";
        }

        return "VANILLA";
    }

    private void RecreateRenderTargets(int index)
    {
        if (index < 0 || index >= playerCameras.Length)
        {
            return;
        }

        if (playerCameras[index] != null)
        {
            int playerSize = GetPlayerTextureSize(cameraQuality[index]);
            ReplaceRenderTexture(ref playerTextures[index], playerSize, playerSize);
            playerCameras[index].targetTexture = playerTextures[index];
        }

        if (mapCameras[index] != null)
        {
            int mapSize = GetMapTextureSize(cameraQuality[index]);
            ReplaceRenderTexture(ref mapTextures[index], mapSize, mapSize);
            mapCameras[index].targetTexture = mapTextures[index];
        }
    }

    private void ReplaceRenderTexture(ref RenderTexture texture, int width, int height)
    {
        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
        }

        texture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32);
        texture.antiAliasing = 1;
        texture.useMipMap = false;
        texture.autoGenerateMips = false;
        texture.filterMode = FilterMode.Point;
        texture.Create();
    }

    private void RefreshTileFeeds(int index)
    {
        if (index < 0 || index >= largeImages.Length || largeImages[index] == null || smallImages[index] == null)
        {
            return;
        }

        Texture largeTexture = primaryIsMap[index] ? (Texture)mapTextures[index] : playerTextures[index];
        Texture smallTexture = primaryIsMap[index] ? (Texture)playerTextures[index] : mapTextures[index];

        largeImages[index].texture = largeTexture;
        smallImages[index].texture = smallTexture;

        smallImages[index].gameObject.SetActive(true);

        bool enabled = cameraEnabled[index] && playerCameras[index] != null && !disconnectedState[index];
        Color tint = enabled ? Color.white : new Color(0.25f, 0.25f, 0.25f, 1f);
        largeImages[index].color = tint;
        smallImages[index].color = tint;

        UpdateRouteOverlayVisibility(index);

        UpdateSmallViewRect(index);

        RefreshStatusBadge(index);

        if (focusedIndex == index)
        {
            focusedPrimaryIsMap = primaryIsMap[index];
            UpdateFocusedTextures();
        }
    }

    private void UpdateRouteOverlayVisibility(int index)
    {
        if (largeMapOverlayRoots == null || smallMapOverlayRoots == null || index < 0 || index >= largeMapOverlayRoots.Length || index >= smallMapOverlayRoots.Length)
        {
            return;
        }

        if (largeMapOverlayRoots[index] != null)
        {
            largeMapOverlayRoots[index].gameObject.SetActive(primaryIsMap[index]);
        }

        if (smallMapOverlayRoots[index] != null)
        {
            smallMapOverlayRoots[index].gameObject.SetActive(!primaryIsMap[index]);
        }

        bool focusedForIndex = focusedOverlay != null && focusedOverlay.activeSelf && focusedIndex == index;
        if (focusedMapOverlayLargeRoot != null)
        {
            focusedMapOverlayLargeRoot.gameObject.SetActive(focusedForIndex && focusedPrimaryIsMap);
        }

        if (focusedMapOverlaySmallRoot != null)
        {
            focusedMapOverlaySmallRoot.gameObject.SetActive(focusedForIndex && !focusedPrimaryIsMap);
        }
    }

    private RectTransform GetActiveRouteOverlayRoot(int index)
    {
        if (focusedOverlay != null && focusedOverlay.activeSelf && focusedIndex == index)
        {
            if (focusedPrimaryIsMap)
            {
                if (focusedMapOverlayLargeRoot != null)
                {
                    return focusedMapOverlayLargeRoot;
                }
            }

            if (focusedMapOverlaySmallRoot != null)
            {
                return focusedMapOverlaySmallRoot;
            }
        }

        if (primaryIsMap[index])
        {
            return largeMapOverlayRoots != null && index < largeMapOverlayRoots.Length ? largeMapOverlayRoots[index] : null;
        }

        return smallMapOverlayRoots != null && index < smallMapOverlayRoots.Length ? smallMapOverlayRoots[index] : null;
    }

    private void SetRouteLinesActive(int index, int activeCount)
    {
        if (routeLineImages == null || index < 0 || index >= routeLineImages.Length || routeLineImages[index] == null)
        {
            return;
        }

        List<Image> lines = routeLineImages[index];
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i] != null)
            {
                lines[i].gameObject.SetActive(i < activeCount);
            }
        }
    }

    private Image EnsureRouteLineImage(int index, int lineIndex, Transform parent)
    {
        if (routeLineImages == null || index < 0 || index >= routeLineImages.Length)
        {
            return null;
        }

        if (routeLineImages[index] == null)
        {
            routeLineImages[index] = new List<Image>(8);
        }

        List<Image> lines = routeLineImages[index];
        while (lines.Count <= lineIndex)
        {
            GameObject lineObj = new GameObject("RouteLine_" + lines.Count);
            lineObj.transform.SetParent(parent, false);
            Image image = lineObj.AddComponent<Image>();
            image.raycastTarget = false;
            lines.Add(image);
        }

        Image line = lines[lineIndex];
        if (line != null && line.transform.parent != parent)
        {
            line.transform.SetParent(parent, false);
        }

        return line;
    }

    private Vector2 ViewportToOverlayPoint(RectTransform overlay, Vector3 viewport)
    {
        float x = (viewport.x - 0.5f) * overlay.rect.width;
        float y = (viewport.y - 0.5f) * overlay.rect.height;
        return new Vector2(x, y);
    }

    private Color GetRouteColor(float distance)
    {
        Color farColor = new Color(0.95f, 0.2f, 0.18f, 0.95f);
        Color midColor = new Color(0.98f, 0.56f, 0.12f, 0.95f);
        Color closeColor = new Color(0.95f, 0.9f, 0.12f, 0.95f);
        Color nearColor = new Color(0.2f, 0.95f, 0.22f, 0.95f);

        if (distance >= RouteFarDistance)
        {
            return farColor;
        }

        if (distance >= RouteMidDistance)
        {
            float t = Mathf.InverseLerp(RouteMidDistance, RouteFarDistance, distance);
            return Color.Lerp(midColor, farColor, t);
        }

        if (distance >= RouteCloseDistance)
        {
            float t = Mathf.InverseLerp(RouteCloseDistance, RouteMidDistance, distance);
            return Color.Lerp(closeColor, midColor, t);
        }

        if (distance >= RouteVeryCloseDistance)
        {
            float t = Mathf.InverseLerp(RouteVeryCloseDistance, RouteCloseDistance, distance);
            return Color.Lerp(nearColor, closeColor, t);
        }

        return nearColor;
    }

    private bool TryGetBoolMember(Type type, object instance, string memberName, out bool value)
    {
        value = false;
        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            object raw = field.GetValue(instance);
            if (raw is bool boolValue)
            {
                value = boolValue;
                return true;
            }
        }

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null)
        {
            object raw = property.GetValue(instance, null);
            if (raw is bool boolValue)
            {
                value = boolValue;
                return true;
            }
        }

        return false;
    }

    private bool TryGetIntMember(Type type, object instance, string memberName, out int value)
    {
        value = 0;
        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            object raw = field.GetValue(instance);
            if (raw is int intValue)
            {
                value = intValue;
                return true;
            }
        }

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null)
        {
            object raw = property.GetValue(instance, null);
            if (raw is int intValue)
            {
                value = intValue;
                return true;
            }
        }

        return false;
    }

    private bool TryGetTransformMember(Type type, object instance, string memberName, out Transform value)
    {
        value = null;
        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            object raw = field.GetValue(instance);
            if (raw is Transform transformValue)
            {
                value = transformValue;
                return true;
            }

            if (raw is Component componentValue)
            {
                value = componentValue.transform;
                return value != null;
            }

            if (raw is GameObject gameObjectValue)
            {
                value = gameObjectValue.transform;
                return value != null;
            }
        }

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null)
        {
            object raw = property.GetValue(instance, null);
            if (raw is Transform transformValue)
            {
                value = transformValue;
                return true;
            }

            if (raw is Component componentValue)
            {
                value = componentValue.transform;
                return value != null;
            }

            if (raw is GameObject gameObjectValue)
            {
                value = gameObjectValue.transform;
                return value != null;
            }
        }

        return false;
    }

    private bool ContainsNearbyRouteTarget(List<Vector3> targets, Vector3 position)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            if ((targets[i] - position).sqrMagnitude < 6.25f)
            {
                return true;
            }
        }

        return false;
    }

    private void AddRouteTarget(List<Vector3> targets, Vector3 position)
    {
        if (!ContainsNearbyRouteTarget(targets, position))
        {
            targets.Add(position);
        }
    }

    private void ResolveEntranceTeleportType()
    {
        if (entranceTeleportType != null)
        {
            return;
        }

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type candidate = assemblies[i].GetType("EntranceTeleport", false, false);
            if (candidate != null)
            {
                entranceTeleportType = candidate;
                return;
            }
        }
    }

    private void RefreshRouteTargetsIfNeeded()
    {
        if (Time.unscaledTime < nextRouteTargetRefreshAt && (mainRouteTargets.Count > 0 || emergencyRouteTargets.Count > 0))
        {
            return;
        }

        nextRouteTargetRefreshAt = Time.unscaledTime + RouteTargetRefreshInterval;

        mainRouteTargets.Clear();
        emergencyRouteTargets.Clear();
        ResolveEntranceTeleportType();

        MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            Type type = behaviour.GetType();
            bool matches = entranceTeleportType != null
                ? entranceTeleportType.IsAssignableFrom(type) || type.Name == "EntranceTeleport"
                : type.Name == "EntranceTeleport";
            if (!matches)
            {
                continue;
            }

            bool isMain = false;
            bool hasMainFlag = TryGetBoolMember(type, behaviour, "isMainEntrance", out isMain);
            if (!hasMainFlag)
            {
                int entranceId;
                if (TryGetIntMember(type, behaviour, "entranceId", out entranceId))
                {
                    isMain = entranceId == 0;
                }
            }

            string name = behaviour.gameObject != null ? behaviour.gameObject.name : string.Empty;
            if (!isMain && !string.IsNullOrEmpty(name) && name.IndexOf("main", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isMain = true;
            }

            Transform entrancePoint;
            if (TryGetTransformMember(type, behaviour, "entrancePoint", out entrancePoint) && entrancePoint != null)
            {
                AddRouteTarget(isMain ? mainRouteTargets : emergencyRouteTargets, entrancePoint.position);
            }

            Transform exitPoint;
            if (TryGetTransformMember(type, behaviour, "exitPoint", out exitPoint) && exitPoint != null)
            {
                AddRouteTarget(isMain ? mainRouteTargets : emergencyRouteTargets, exitPoint.position);
            }

            AddRouteTarget(isMain ? mainRouteTargets : emergencyRouteTargets, behaviour.transform.position);

            if (entrancePoint == null && exitPoint == null)
            {
                Transform[] childTransforms = behaviour.GetComponentsInChildren<Transform>(true);
                for (int c = 0; c < childTransforms.Length; c++)
                {
                    Transform child = childTransforms[c];
                    if (child == null || child == behaviour.transform)
                    {
                        continue;
                    }

                    string childName = child.name != null ? child.name.ToLowerInvariant() : string.Empty;
                    if (childName.Contains("entrance") || childName.Contains("exit"))
                    {
                        AddRouteTarget(isMain ? mainRouteTargets : emergencyRouteTargets, child.position);
                    }
                }
            }
        }

        if (mainRouteTargets.Count == 0 && emergencyRouteTargets.Count == 0)
        {
            Transform[] allTransforms = UnityEngine.Object.FindObjectsOfType<Transform>(true);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                Transform transform = allTransforms[i];
                if (transform == null || !transform.gameObject.activeInHierarchy)
                {
                    continue;
                }

                string name = transform.name != null ? transform.name.ToLowerInvariant() : string.Empty;
                bool looksLikeEntrance = name.Contains("entrance") || name.Contains("fireexit") || name.Contains("emergency");
                if (!looksLikeEntrance)
                {
                    continue;
                }

                // Validate that the position is actually on the NavMesh (walkable)
                Vector3 pos = transform.position;
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(pos, out hit, 2f, NavMesh.AllAreas))
                {
                    continue;
                }

                bool isMainByName = name.Contains("main");
                AddRouteTarget(isMainByName ? mainRouteTargets : emergencyRouteTargets, hit.position);
            }
        }

        if (mainRouteTargets.Count == 0 && emergencyRouteTargets.Count > 0)
        {
            mainRouteTargets.Add(emergencyRouteTargets[0]);
        }
    }

    private void CollectRouteTargetsForPlayer(int index, Vector3 playerPosition, List<Vector3> output)
    {
        output.Clear();
        RefreshRouteTargetsIfNeeded();

        int mode = RouteModeOff;
        if (routeMode != null && index >= 0 && index < routeMode.Length)
        {
            mode = routeMode[index];
        }

        if (mode == RouteModeOff)
        {
            return;
        }

        if (mode == RouteModeEmergency)
        {
            for (int i = 0; i < emergencyRouteTargets.Count; i++)
            {
                output.Add(emergencyRouteTargets[i]);
            }
            return;
        }

        List<Vector3> source = mainRouteTargets.Count > 0 ? mainRouteTargets : emergencyRouteTargets;
        if (source.Count == 0)
        {
            return;
        }

        int bestIndex = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < source.Count; i++)
        {
            float distance = (source[i] - playerPosition).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        output.Add(source[bestIndex]);
    }

    private void BuildRoutePathPoints(Vector3 from, Vector3 to, List<Vector3> output)
    {
        output.Clear();

        bool hasPath = NavMesh.CalculatePath(from, to, NavMesh.AllAreas, routeNavMeshPath)
            && routeNavMeshPath != null
            && routeNavMeshPath.status != NavMeshPathStatus.PathInvalid
            && routeNavMeshPath.corners != null
            && routeNavMeshPath.corners.Length >= 2;

        if (hasPath)
        {
            Vector3[] corners = routeNavMeshPath.corners;
            for (int i = 0; i < corners.Length; i++)
            {
                output.Add(corners[i]);
            }
            return;
        }

        output.Add(from);
        output.Add(to);
    }

    private void UpdateRouteLines(int index, PlayerControllerB player)
    {
        if (player == null || mapCameras == null || index < 0 || index >= mapCameras.Length || mapCameras[index] == null)
        {
            SetRouteLinesActive(index, 0);
            return;
        }

        if (!cameraEnabled[index])
        {
            SetRouteLinesActive(index, 0);
            return;
        }

        UpdateRouteOverlayVisibility(index);

        RectTransform overlayRoot = GetActiveRouteOverlayRoot(index);
        if (overlayRoot == null || !overlayRoot.gameObject.activeInHierarchy)
        {
            SetRouteLinesActive(index, 0);
            return;
        }

        Vector3 playerWorld = player.transform.position;
        CollectRouteTargetsForPlayer(index, playerWorld, routeWorkTargets);
        if (routeWorkTargets.Count == 0)
        {
            SetRouteLinesActive(index, 0);
            return;
        }

        Camera mapCam = mapCameras[index];

        int drawn = 0;
        for (int i = 0; i < routeWorkTargets.Count; i++)
        {
            Vector3 targetWorld = routeWorkTargets[i];
            BuildRoutePathPoints(playerWorld, targetWorld, routePathWorkPoints);
            if (routePathWorkPoints.Count < 2)
            {
                continue;
            }

            float totalDistance = 0f;
            for (int p = 1; p < routePathWorkPoints.Count; p++)
            {
                totalDistance += Vector3.Distance(routePathWorkPoints[p - 1], routePathWorkPoints[p]);
            }

            float traveled = 0f;
            int drawnBeforeTarget = drawn;

            for (int p = 1; p < routePathWorkPoints.Count; p++)
            {
                Vector3 worldA = routePathWorkPoints[p - 1];
                Vector3 worldB = routePathWorkPoints[p];

                float playerDistA = Vector3.Distance(playerWorld, worldA);
                float playerDistB = Vector3.Distance(playerWorld, worldB);
                if (playerDistA > RouteMaxSegmentDistanceFromPlayer && playerDistB > RouteMaxSegmentDistanceFromPlayer)
                {
                    continue;
                }

                Vector3 viewportA = mapCam.WorldToViewportPoint(worldA);
                Vector3 viewportB = mapCam.WorldToViewportPoint(worldB);

                bool aInside = viewportA.x >= 0f && viewportA.x <= 1f && viewportA.y >= 0f && viewportA.y <= 1f;
                bool bInside = viewportB.x >= 0f && viewportB.x <= 1f && viewportB.y >= 0f && viewportB.y <= 1f;
                if (!aInside || !bInside)
                {
                    continue;
                }

                Vector2 pointA = ViewportToOverlayPoint(overlayRoot, new Vector3(viewportA.x, viewportA.y, 0f));
                Vector2 pointB = ViewportToOverlayPoint(overlayRoot, new Vector3(viewportB.x, viewportB.y, 0f));

                Vector2 delta = pointB - pointA;
                float length = delta.magnitude;
                float segmentWorldDistance = Vector3.Distance(worldA, worldB);
                traveled += segmentWorldDistance;
                if (traveled > RouteMaxPathDrawDistance)
                {
                    break;
                }

                if (length < 2f)
                {
                    continue;
                }

                float remainingDistance = Mathf.Max(0f, totalDistance - traveled);
                Color segmentColor = GetRouteColor(remainingDistance);
                Vector2 direction = delta / length;
                float dashStep = RouteDashLength + RouteDashGap;
                int dashCount = Mathf.Max(1, Mathf.CeilToInt(length / dashStep));

                for (int d = 0; d < dashCount; d++)
                {
                    float dashStart = d * dashStep;
                    if (dashStart >= length)
                    {
                        break;
                    }

                    float dashLen = Mathf.Min(RouteDashLength, length - dashStart);
                    float dashCenterDist = dashStart + dashLen * 0.5f;
                    Vector2 dashCenter = pointA + direction * dashCenterDist;

                    Image line = EnsureRouteLineImage(index, drawn, overlayRoot);
                    if (line == null)
                    {
                        continue;
                    }

                    RectTransform lineRect = line.rectTransform;
                    lineRect.anchorMin = new Vector2(0.5f, 0.5f);
                    lineRect.anchorMax = new Vector2(0.5f, 0.5f);
                    lineRect.pivot = new Vector2(0.5f, 0.5f);
                    lineRect.anchoredPosition = dashCenter;
                    lineRect.sizeDelta = new Vector2(dashLen, RouteLineThickness);
                    lineRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                    line.color = segmentColor;
                    line.gameObject.SetActive(true);
                    drawn++;
                }
            }

            if (drawn == drawnBeforeTarget)
            {
                Vector3 playerViewport = mapCam.WorldToViewportPoint(playerWorld);
                Vector3 targetViewport = mapCam.WorldToViewportPoint(targetWorld);
                bool playerInside = playerViewport.x >= 0f && playerViewport.x <= 1f && playerViewport.y >= 0f && playerViewport.y <= 1f;
                bool targetInside = targetViewport.x >= 0f && targetViewport.x <= 1f && targetViewport.y >= 0f && targetViewport.y <= 1f;
                if (playerInside && targetInside)
                {
                    Vector2 pointA = ViewportToOverlayPoint(overlayRoot, new Vector3(playerViewport.x, playerViewport.y, 0f));
                    Vector2 pointB = ViewportToOverlayPoint(overlayRoot, new Vector3(targetViewport.x, targetViewport.y, 0f));
                    Vector2 delta = pointB - pointA;
                    float length = delta.magnitude;
                    if (length >= 2f)
                    {
                        Vector2 direction = delta / length;
                        float dashStep = RouteDashLength + RouteDashGap;
                        int dashCount = Mathf.Max(1, Mathf.CeilToInt(length / dashStep));
                        Color segmentColor = GetRouteColor(Vector3.Distance(playerWorld, targetWorld));

                        for (int d = 0; d < dashCount; d++)
                        {
                            float dashStart = d * dashStep;
                            if (dashStart >= length)
                            {
                                break;
                            }

                            float dashLen = Mathf.Min(RouteDashLength, length - dashStart);
                            float dashCenterDist = dashStart + dashLen * 0.5f;
                            Vector2 dashCenter = pointA + direction * dashCenterDist;

                            Image line = EnsureRouteLineImage(index, drawn, overlayRoot);
                            if (line == null)
                            {
                                continue;
                            }

                            RectTransform lineRect = line.rectTransform;
                            lineRect.anchorMin = new Vector2(0.5f, 0.5f);
                            lineRect.anchorMax = new Vector2(0.5f, 0.5f);
                            lineRect.pivot = new Vector2(0.5f, 0.5f);
                            lineRect.anchoredPosition = dashCenter;
                            lineRect.sizeDelta = new Vector2(dashLen, RouteLineThickness);
                            lineRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                            line.color = segmentColor;
                            line.gameObject.SetActive(true);
                            drawn++;
                        }
                    }
                }
            }
        }

        SetRouteLinesActive(index, drawn);
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObj = new GameObject("MultiCamEventSystem");
        eventSystemObj.AddComponent<EventSystem>();
        eventSystemObj.AddComponent<InputSystemUIInputModule>();
    }

    private bool ShouldRenderCamera(int index)
    {
        bool visibleInGrid = index >= 0 && index < tileObjects.Length && tileObjects[index] != null && tileObjects[index].activeSelf;
        bool visibleInFocus = focusedIndex == index && focusedOverlay != null && focusedOverlay.activeSelf;
        return visibleInGrid || visibleInFocus;
    }

    private void EvaluateAutoQuality(int index)
    {
        if (!autoQualityEnabled[index] || Time.unscaledTime < nextAutoAdjust[index])
        {
            if (!autoQualityEnabled[index])
            {
                autoDowngradedState[index] = false;
            }
            return;
        }

        QualityPreset current = cameraQuality[index];
        QualityPreset updated = current;

        if (smoothedFrameTime > 0.029f)
        {
            if (current == QualityPreset.High)
            {
                updated = QualityPreset.Medium;
            }
            else if (current == QualityPreset.Medium)
            {
                updated = QualityPreset.Low;
            }
        }

        if (updated != current)
        {
            cameraQuality[index] = updated;
            autoDowngradedState[index] = updated < current;
            RecreateRenderTargets(index);
            RefreshTileFeeds(index);
            RefreshMenuText(index);
        }

        nextAutoAdjust[index] = Time.unscaledTime + 2.5f;
    }

    private void RefreshStatusBadge(int index)
    {
        if (statusBadges == null || index < 0 || index >= statusBadges.Length || statusBadges[index] == null)
        {
            return;
        }

        Text badge = statusBadges[index];

        if (deadState != null && index >= 0 && index < deadState.Length && deadState[index])
        {
            badge.text = "DEAD";
            badge.color = new Color(1f, 0.72f, 0.32f, 1f);
            badge.gameObject.SetActive(true);
            AttachBadgeTooltip(badge, "This player is dead");
            return;
        }

        if (disconnectedState[index])
        {
            badge.text = "DC";
            badge.color = new Color(1f, 0.45f, 0.45f, 1f);
            badge.gameObject.SetActive(true);
            AttachBadgeTooltip(badge, "DC: Player disconnected or slot inactive");
            return;
        }

        if (!cameraEnabled[index])
        {
            badge.text = "OFF";
            badge.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            badge.gameObject.SetActive(true);
            AttachBadgeTooltip(badge, "OFF: Camera manually disabled");
            return;
        }

        if (autoDowngradedState[index])
        {
            badge.text = "AUTO";
            badge.color = new Color(1f, 0.86f, 0.2f, 1f);
            badge.gameObject.SetActive(true);
            AttachBadgeTooltip(badge, "AUTO: Quality was lowered to reduce lag");
            return;
        }

        badge.gameObject.SetActive(false);
    }

    private void AttachBadgeTooltip(Text badge, string message)
    {
        if (badge == null)
        {
            return;
        }

        EventTrigger trigger = badge.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = badge.gameObject.AddComponent<EventTrigger>();
        }

        trigger.triggers.Clear();

        EventTrigger.Entry enter = new EventTrigger.Entry();
        enter.eventID = EventTriggerType.PointerEnter;
        enter.callback.AddListener(delegate
        {
            ShowTooltip(message);
        });

        EventTrigger.Entry exit = new EventTrigger.Entry();
        exit.eventID = EventTriggerType.PointerExit;
        exit.callback.AddListener(delegate
        {
            HideTooltip();
        });

        trigger.triggers.Add(enter);
        trigger.triggers.Add(exit);
    }

    private void ShowTooltip(string message)
    {
        if (tooltipRect == null || tooltipText == null)
        {
            return;
        }

        tooltipText.text = message;
        tooltipRect.SetAsLastSibling();
        UpdateTooltipPosition();
        tooltipRect.gameObject.SetActive(true);
    }

    private void HideTooltip()
    {
        if (tooltipRect != null)
        {
            tooltipRect.gameObject.SetActive(false);
        }
    }

    private void UpdateTooltipPosition()
    {
        if (tooltipRect == null || rootRect == null)
        {
            return;
        }

        Vector2 mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        Vector2 localPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, mouse, null, out localPos);

        float x = localPos.x + 18f;
        float y = localPos.y - 18f;

        x = Mathf.Clamp(x, rootRect.rect.xMin + 8f, rootRect.rect.xMax - tooltipRect.rect.width - 8f);
        y = Mathf.Clamp(y, rootRect.rect.yMin + tooltipRect.rect.height + 8f, rootRect.rect.yMax - 8f);

        tooltipRect.anchoredPosition = new Vector2(x, y);
    }

    private void LateUpdate()
    {
        if (!initialized || playerCameras == null || StartOfRound.Instance == null || StartOfRound.Instance.allPlayerScripts == null)
        {
            return;
        }

        if (defaultRadarCamera == null)
        {
            CacheDefaultRadarCamera();
        }

        HandleOpenPopupDismiss();

        if (gridRoot != null)
        {
            float currentWidth = gridRoot.rect.width;
            float currentHeight = gridRoot.rect.height;
            if (Mathf.Abs(currentWidth - lastGridWidth) > 1f || Mathf.Abs(currentHeight - lastGridHeight) > 1f)
            {
                lastGridWidth = currentWidth;
                lastGridHeight = currentHeight;
                layoutDirty = true;
            }
        }

        if (layoutDirty)
        {
            ApplyGridLayout();
        }

        if (tooltipRect != null && tooltipRect.gameObject.activeSelf)
        {
            UpdateTooltipPosition();
        }

        PlayerControllerB[] players = StartOfRound.Instance.allPlayerScripts;
        if (players.Length != playerCameras.Length)
        {
            return;
        }

        smoothedFrameTime = Mathf.Lerp(smoothedFrameTime, Time.unscaledDeltaTime, 0.12f);

        for (int i = 0; i < players.Length; i++)
        {
            bool wasDead = deadState[i];
            bool wasDisconnected = disconnectedState[i];

            PlayerControllerB player = players[i];
            bool hasCameras = playerCameras[i] != null && mapCameras[i] != null;
            bool isDead = player != null && player.isPlayerDead;
            bool isDisconnected = player == null || (!player.isPlayerControlled && !isDead);
            bool valid = !isDisconnected && hasCameras;
            if (!valid)
            {
                deadState[i] = false;
                disconnectedState[i] = isDisconnected;
                cameraEnabled[i] = false;
                autoDowngradedState[i] = false;
                RefreshTileFeeds(i);
                RefreshMenuText(i);
                SetRouteLinesActive(i, 0);
                continue;
            }

            Transform playerAnchor = player.gameplayCamera != null
                ? player.gameplayCamera.transform
                : (player.playerGlobalHead != null ? player.playerGlobalHead : player.cameraContainerTransform);

            if (playerAnchor == null)
            {
                if (isDead && player.transform != null)
                {
                    playerAnchor = player.transform;
                }
                else
                {
                    deadState[i] = false;
                    disconnectedState[i] = true;
                    cameraEnabled[i] = false;
                    autoDowngradedState[i] = false;
                    RefreshTileFeeds(i);
                    RefreshMenuText(i);
                    continue;
                }
            }

            deadState[i] = isDead;
            disconnectedState[i] = false;

            if (wasDead != deadState[i] || wasDisconnected != disconnectedState[i])
            {
                RefreshTileFeeds(i);
                RefreshMenuText(i);
            }

            Vector3 anchorPosition = playerAnchor.position;
            Vector3 anchorForward = playerAnchor.forward;
            Vector3 anchorUp = playerAnchor.up;

            if (player.gameplayCamera != null)
            {
                anchorPosition = player.gameplayCamera.transform.position;
                anchorForward = player.gameplayCamera.transform.forward;
                anchorUp = player.gameplayCamera.transform.up;
            }

            playerCameras[i].transform.position = anchorPosition + anchorForward * PlayerCameraForwardOffset + anchorUp * PlayerCameraUpOffset;
            playerCameras[i].transform.rotation = playerAnchor.rotation;

            if (player.gameplayCamera != null)
            {
                playerCameras[i].fieldOfView = player.gameplayCamera.fieldOfView;
                playerCameras[i].cullingMask = BuildPlayerCameraMask(player, player.gameplayCamera);
            }

            if (mapPrimeStage != null && mapPrimeAt != null && mapPrimedOnce != null && i < mapPrimeStage.Length && i < mapPrimeAt.Length && i < mapPrimedOnce.Length)
            {
                if (!cameraEnabled[i])
                {
                    mapPrimeStage[i] = 0;
                }
                else if (mapPrimeStage[i] == 1 && Time.unscaledTime >= mapPrimeAt[i])
                {
                    mapPrimeStage[i] = 2;
                    mapPrimeAt[i] = Time.unscaledTime + 0.05f;
                }
                else if (mapPrimeStage[i] == 2 && Time.unscaledTime >= mapPrimeAt[i])
                {
                    mapPrimeStage[i] = 0;
                    mapPrimedOnce[i] = true;
                }
            }

            int mapType = mapTypeIndex[i];
            if (mapPrimeStage != null && i < mapPrimeStage.Length && mapPrimeStage[i] == 1)
            {
                mapType = 1;
            }

            float radarOffsetForPlayer = GetRadarHeightOffsetForPlayer(player);
            float mapNearClipForPlayer = GetMapNearClipForPlayer(player);
            float mapFarClipForPlayer = GetMapFarClipForPlayer(player, radarOffsetForPlayer);

            // Vanilla map depends on the game's radar camera; until it exists, render via radar path.
            if (mapType == 2 && defaultRadarCamera == null)
            {
                mapType = 0;
            }

            if (mapType == 0)
            {
                ApplyDefaultMapCameraSettings(mapCameras[i]);
                mapCameras[i].orthographicSize = MapZoomSizes[mapZoomIndex[i]];
                mapCameras[i].clearFlags = CameraClearFlags.SolidColor;
                mapCameras[i].backgroundColor = Color.black;
                mapCameras[i].nearClipPlane = mapNearClipForPlayer;
                mapCameras[i].farClipPlane = mapFarClipForPlayer;
                mapCameras[i].transform.position = player.transform.position + Vector3.up * (radarOffsetForPlayer + RadarContourHeightBoost);
                mapCameras[i].transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                if (defaultRadarCamera != null)
                {
                    mapCameras[i].cullingMask = defaultRadarCamera.cullingMask;
                }
            }
            else if (mapType == 1)
            {
                Camera source = player.gameplayCamera;

                mapCameras[i].orthographic = true;
                mapCameras[i].orthographicSize = MapZoomSizes[mapZoomIndex[i]];
                mapCameras[i].clearFlags = source != null ? source.clearFlags : CameraClearFlags.Skybox;
                mapCameras[i].backgroundColor = source != null ? source.backgroundColor : Color.black;
                mapCameras[i].cullingMask = source != null ? source.cullingMask : ~0;
                mapCameras[i].nearClipPlane = mapNearClipForPlayer;
                mapCameras[i].farClipPlane = mapFarClipForPlayer;
                mapCameras[i].transform.position = player.transform.position + Vector3.up * (radarOffsetForPlayer + 12f);
                mapCameras[i].transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            else
            {
                // Vanilla mode: clone ship radar camera settings, but render per-player map on our own camera.
                if (defaultRadarCamera != null)
                {
                    mapCameras[i].CopyFrom(defaultRadarCamera);
                    mapCameras[i].orthographic = true;
                    mapCameras[i].clearFlags = CameraClearFlags.SolidColor;
                    mapCameras[i].backgroundColor = defaultRadarCamera.backgroundColor;
                    // Keep radar-style layer filtering and strip UI overlays from the feed.
                    mapCameras[i].cullingMask = defaultRadarCamera.cullingMask & ~(1 << 5);
                    mapCameras[i].nearClipPlane = mapNearClipForPlayer;
                    mapCameras[i].farClipPlane = mapFarClipForPlayer;
                    mapCameras[i].transform.position = player.transform.position + Vector3.up * (radarOffsetForPlayer + RadarContourHeightBoost);
                    mapCameras[i].transform.rotation = Quaternion.Euler(90f, defaultRadarCamera.transform.eulerAngles.y, 0f);
                    mapCameras[i].orthographicSize = MapZoomSizes[mapZoomIndex[i]];
                    mapCameras[i].rect = new Rect(0f, 0f, 1f, 1f);
                    mapCameras[i].targetTexture = mapTextures[i];
                    mapCameras[i].enabled = false;
                    mapCameras[i].allowHDR = false;
                    mapCameras[i].allowMSAA = false;
                }
                else
                {
                    // Last-resort fallback when the ship radar camera is unavailable.
                    mapCameras[i].orthographic = true;
                    mapCameras[i].orthographicSize = MapZoomSizes[mapZoomIndex[i]];
                    mapCameras[i].clearFlags = CameraClearFlags.SolidColor;
                    mapCameras[i].backgroundColor = Color.black;
                    mapCameras[i].cullingMask = ~0;
                    mapCameras[i].nearClipPlane = mapNearClipForPlayer;
                    mapCameras[i].farClipPlane = mapFarClipForPlayer;
                    mapCameras[i].transform.position = player.transform.position + Vector3.up * (radarHeightOffset + 60f);
                    mapCameras[i].transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    mapCameras[i].enabled = false;
                }
            }

            UpdateRouteLines(i, player);

            if (!cameraEnabled[i] || !ShouldRenderCamera(i))
            {
                continue;
            }

            EvaluateAutoQuality(i);

            float interval = GetIntervalFromFps(fpsCap[i]);
            if (focusedIndex == i && focusedOverlay.activeSelf)
            {
                interval = Mathf.Min(interval, 0.08f);
            }

            if (Time.unscaledTime >= nextPlayerRender[i])
            {
                nextPlayerRender[i] = Time.unscaledTime + interval;
                playerCameras[i].Render();
            }

            float mapInterval = interval * 1.35f;
            if (Time.unscaledTime >= nextMapRender[i])
            {
                nextMapRender[i] = Time.unscaledTime + mapInterval;
                mapCameras[i].Render();
            }
        }
    }

    private void OnDestroy()
    {
        if (playerTextures != null)
        {
            for (int i = 0; i < playerTextures.Length; i++)
            {
                if (playerTextures[i] != null)
                {
                    playerTextures[i].Release();
                    Destroy(playerTextures[i]);
                }
            }
        }

        if (mapTextures != null)
        {
            for (int i = 0; i < mapTextures.Length; i++)
            {
                if (mapTextures[i] != null)
                {
                    mapTextures[i].Release();
                    Destroy(mapTextures[i]);
                }
            }
        }

        if (playerCameras != null)
        {
            for (int i = 0; i < playerCameras.Length; i++)
            {
                if (playerCameras[i] != null)
                {
                    Destroy(playerCameras[i].gameObject);
                }
            }
        }

        if (mapCameras != null)
        {
            for (int i = 0; i < mapCameras.Length; i++)
            {
                if (mapCameras[i] != null)
                {
                    Destroy(mapCameras[i].gameObject);
                }
            }
        }

        if (opaqueFeedMaterial != null)
        {
            Destroy(opaqueFeedMaterial);
            opaqueFeedMaterial = null;
        }
    }
}
