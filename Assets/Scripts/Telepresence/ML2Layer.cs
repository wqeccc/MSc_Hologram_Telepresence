using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using MagicLeap.OpenXR.Features.Planes;
using MagicLeap.OpenXR.Subsystems;
using MagicLeap.Android;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

public class ML2Layer : MonoBehaviour
{
    // plane
    private ARPlaneManager planeManager;
    private MagicLeapPlanesFeature planeFeature;
    [Header("Plane Settings")]
    [SerializeField]
    private uint maxResults = 100; // Maximum number of planes to return each query
    [SerializeField]
    private float minPlaneArea = 0.09f; // Minimum plane area to treat as a valid plane (m^2)
    private bool permissionGranted = false;

    // controller
    private InputActionAsset inputActionsAsset;
    private InputAction triggerAction, pointerPositionAction, pointerRotationAction;

    public bool isAttached = false;

    IEnumerator Start()
    {
        // wait until the subsystem ready
        yield return new WaitUntil(AreSubsystemsLoaded<XRPlaneSubsystem>);
        planeManager = FindAnyObjectByType<ARPlaneManager>();
        if (planeManager == null)
        {
            Debug.LogError("Failed to find ARPlaneManager in scene. Disabling Script");
            enabled = false; // stop script
        }
        else
        {
            // disable planeManager until we have successfully requested required permissions
            planeManager.enabled = false;
        }
        Permissions.RequestPermission(Permissions.SpatialMapping, OnPermissionGranted, OnPermissionDenied);

        InitController();
    }

    // void Update()
    // {
        
    // }

    private void OnDestroy()
    {
        if (triggerAction != null)
        {
            triggerAction.performed -= HandleTriggerToggle; // unsubscribe
        }

        if (inputActionsAsset != null) 
        {
            inputActionsAsset.Disable();
        }
    }

    // ref: MagicLeap.Examples
    static bool AreSubsystemsLoaded<T>() where T : class, ISubsystem
    {
        if (XRGeneralSettings.Instance == null) return false;
        if (XRGeneralSettings.Instance.Manager == null) return false;
        var activeLoader = XRGeneralSettings.Instance.Manager.activeLoader;
        if (activeLoader == null) return false;
        return activeLoader.GetLoadedSubsystem<T>() != null;
    }

    void InitController()
    {
        var manager = UnityEngine.Object.FindAnyObjectByType<InputActionManager>();
        if (manager == null)
            throw new NullReferenceException("Could not find an InputActionManager to initialize a MagicLeapController from");

        inputActionsAsset = manager.actionAssets[0];
        if (inputActionsAsset == null)
            throw new NullReferenceException("Could not find an InputActionAsset");

        inputActionsAsset.Enable();

        var inputMap = inputActionsAsset.FindActionMap("Controller");

        triggerAction = inputMap.FindAction("Trigger");
        pointerPositionAction = inputMap.FindAction("PointerPosition");
        pointerRotationAction = inputMap.FindAction("PointerRotation");

        triggerAction.performed += HandleTriggerToggle; // subscribe
    }

    public Vector3 PointerPosition => pointerPositionAction.ReadValue<Vector3>();

    public Quaternion PointerRotation => pointerRotationAction.ReadValue<Quaternion>();

    private void HandleTriggerToggle(InputAction.CallbackContext context)
    {
        isAttached = !isAttached;

        if (isAttached)
        {
            Debug.Log("Trigger pressed. Start scanning");
            if (permissionGranted)
            {
                StartPlanesScanning();
            }
        }
        else
        {
            Debug.Log("Trigger released. Stop scanning");
            StartCoroutine(StopPlanesScanning());
        }
    }

    public Vector3 PlaneDetection(Vector3 pos)
    {   
        if (planeManager == null || !permissionGranted || !planeManager.enabled)
        { 
            return pos;
        }

        float finalY = pos.y; 
        bool foundPlane = false;

        foreach (var plane in planeManager.trackables)
        {
            Vector3 pointInPlaneSpace = plane.transform.InverseTransformPoint(pos);

            // plane boundary
            if (Mathf.Abs(pointInPlaneSpace.x) <= plane.extents.x && 
                Mathf.Abs(pointInPlaneSpace.y) <= plane.extents.y &&
                Mathf.Abs(pointInPlaneSpace.z) <= 0.3f) 
            {
                switch (plane.classification)
                {
                    case PlaneClassification.Table:
                    case PlaneClassification.Floor:
                    case PlaneClassification.Seat:
                        finalY = plane.transform.position.y;
                        foundPlane = true;
                        break;

                    default:
                        break;
                }

                if (foundPlane)
                {
                    Debug.Log($"Detected plane: {plane.classification}");
                    break;
                }
            }
        }

        return new Vector3(pos.x, finalY, pos.z);
    }

    void StartPlanesScanning()
    {
        if (planeManager == null) return;

        var newQuery = new MLXrPlaneSubsystem.PlanesQuery
        {
            Flags = planeManager.requestedDetectionMode.ToMLXrQueryFlags() | MLXrPlaneSubsystem.MLPlanesQueryFlags.SemanticAll,
            BoundsCenter = Camera.main.transform.position,
            BoundsRotation = Camera.main.transform.rotation,
            BoundsExtents = Vector3.one * 20f,
            MaxResults = maxResults,
            MinPlaneArea = minPlaneArea
        };

        MLXrPlaneSubsystem.Query = newQuery;
        planeManager.enabled = true;
        Debug.Log("Start scanning planes");
    }

    IEnumerator StopPlanesScanning()
    {
        if (planeFeature != null && planeFeature.enabled)
        {
            planeFeature.InvalidateCurrentPlanes();
        }
        yield return new WaitForEndOfFrame();
        
        if (planeManager != null)
        {
            planeManager.enabled = false;
        }
        Debug.Log("Stop scanning planes");
    }

    void OnPermissionGranted(string permission)
    {
        permissionGranted = true;
        planeFeature = OpenXRSettings.Instance.GetFeature<MagicLeapPlanesFeature>();
        Debug.Log("Spatial Mapping Permission Granted");
    }

    void OnPermissionDenied(string permission)
    {
        Debug.LogError($"Failed to create Planes Subsystem due to missing or denied {permission} permission.");
        enabled = false;
    }
}
