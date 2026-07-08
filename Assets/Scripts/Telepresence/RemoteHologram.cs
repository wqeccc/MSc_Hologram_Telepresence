using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using Microsoft.Azure.Kinect.Sensor;
using Microsoft.Azure.Kinect.BodyTracking;
#endif

#if !UNITY_EDITOR && UNITY_ANDROID
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using MagicLeap.OpenXR.Features.Planes;
using MagicLeap.OpenXR.Subsystems;
// using UnityEngine.InputSystem;
#endif

public class RemoteHologram : MonoBehaviour
{
    private MetaDataNetworkSender _mdSender;
    private PointCloudNetworkReceiver _pcReceiver;
    private MetaDataNetworkReceiver _mdReceiver;
    private GazeAlignment _gazeAlignment;

    #if UNITY_EDITOR || UNITY_STANDALONE_WIN
        private KinectController _kinectController;
    #endif

    public bool enableGazeAlignment = true;

    [Header("Hologram Objects")]
    // a: local hologram space
    public Pose localSpeaker; // S_a
    public Pose remoteSpeaker; // S_b
    public Transform remoteSpeakerHologram; // P_b (S_b->a)
    public Pose localHologramAtRemote; // P_a (S_a->b)

    private bool initHologramPos = false;

    #if !UNITY_EDITOR && UNITY_ANDROID
        private ARPlaneManager planeManager;
        private MagicLeapPlanesFeature planeFeature;
        [SerializeField]
        private uint maxResults = 100; // Maximum number of planes to return each query
        [SerializeField]
        private float minPlaneArea = 0.09f; // Minimum plane area to treat as a valid plane (m^2)
        private bool permissionGranted = false;
        // private MagicLeapController Controller => MagicLeapController.Instance;
    #endif

    IEnumerator Start()
    {
        #if UNITY_EDITOR || UNITY_STANDALONE_WIN
            _kinectController = FindObjectOfType<KinectController>();
        #endif

        _pcReceiver = gameObject.AddComponent<PointCloudNetworkReceiver>();
        _mdReceiver = gameObject.AddComponent<MetaDataNetworkReceiver>();
        _mdSender = gameObject.AddComponent<MetaDataNetworkSender>();
        _gazeAlignment = gameObject.AddComponent<GazeAlignment>();

        #if !UNITY_EDITOR && UNITY_ANDROID
            // wait until the subsystem ready
            yield return new WaitUntil(Utils.AreSubsystemsLoaded<XRPlaneSubsystem>);
            planeManager = FindObjectOfType<ARPlaneManager>();
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
        #endif

        yield return null;
    }

    void Update()
    {
        #if UNITY_EDITOR || UNITY_STANDALONE_WIN
            if (_kinectController == null || !_kinectController.kinectInitialized)
            {
                Debug.LogWarning("Kinect not Initialized");
                return;
            }
        #endif

        if (_mdReceiver != null)
        {
            remoteSpeaker.position = _mdReceiver.GetRemoteSpeakerPos;
            remoteSpeaker.rotation = _mdReceiver.GetRemoteSpeakerRot;
            localHologramAtRemote.position = _mdReceiver.GetLocalHologramAtRemotePos;
            localHologramAtRemote.rotation = _mdReceiver.GetLocalHologramAtRemoteRot;
        }

        if (_pcReceiver != null && remoteSpeakerHologram == null)
        {
            remoteSpeakerHologram = _pcReceiver.GetRemoteSpeakerTransform;
        }

        UpdateLocalUserPosition();
        CalculatePlacementPosition();

        if (enableGazeAlignment)
        {
            // S_a, S_b, P_b, P_a
            _gazeAlignment.ExecuteAlgorithm(localSpeaker, remoteSpeaker, remoteSpeakerHologram, localHologramAtRemote);
        }

        UpdateSenderData();
    }

    void UpdateLocalUserPosition()
    {
        #if UNITY_EDITOR || UNITY_STANDALONE_WIN
            // PC: Kinect skeleton head
            lock (_kinectController.m_bufferLock)
            {
                if (_kinectController.m_currentSkeletons != null && _kinectController.m_currentSkeletons.Count > 0)
                {
                    SkeletonInfo info = _kinectController.m_currentSkeletons[0];
                    var headJoint = info.skeleton.GetJoint(JointId.Head);
                    var headJointPos = headJoint.Position;

                    // kinect(mm): System.Numerics.Vector3 -> unity(m): UnityEngine.Vector3
                    Vector3 pos = new Vector3(-headJointPos.X / 1000f, headJointPos.Y / 1000f, headJointPos.Z / 1000f);

                    Quaternion rot = new Quaternion(
                        headJoint.Quaternion.X, 
                        headJoint.Quaternion.Y, 
                        headJoint.Quaternion.Z, 
                        headJoint.Quaternion.W
                    );
                    
                    localSpeaker.position = pos;
                    localSpeaker.rotation = rot;
                    return;
                }
            }
        #else
            // ML2: Main Camera
            if (Camera.main != null)
            {
                localSpeaker.position = Camera.main.transform.position;
                localSpeaker.rotation = Camera.main.transform.rotation;
                return;
            }
        #endif

        Debug.LogWarning("[LocalHologram] can't find camera or skeleton source");
    }

    void UpdateSenderData()
    {
        if (_mdSender != null && localSpeaker != null && remoteSpeakerHologram != null)
        {
            _mdSender.localTransform_pos = localSpeaker.position;
            _mdSender.localTransform_rot = localSpeaker.rotation;
            _mdSender.hologramTransform_pos = remoteSpeakerHologram.position;
            _mdSender.hologramTransform_rot = remoteSpeakerHologram.rotation;
        }
    }

    void CalculatePlacementPosition()
    {
        if (localSpeaker == null || remoteSpeakerHologram == null || remoteSpeaker == null || remoteSpeaker.position.y < 0.1f)
        {
            return;
        }

        // default position
        if (!initHologramPos)
        {
            Vector3 forwardVec = localSpeaker.forward;
            forwardVec.y = 0;
            forwardVec.Normalize();

            float distance = 1.5f; // comfortable social distances: 1.2m-3.7m
            Vector3 targetPosXZ = localSpeaker.position + forwardVec * distance;

            // assume the heights (HMDs) are the same
            float defaultY = localSpeaker.position.y - remoteSpeaker.position.y;
            Vector3 initialPos = new Vector3(targetPosXZ.x, defaultY, targetPosXZ.z);

            #if !UNITY_EDITOR && UNITY_ANDROID
                PlaneDetection(initialPos);
            #else
                remoteSpeakerHologram.position = initialPos;
            #endif

            initHologramPos = true;
            Debug.Log("Inintialized hologram default position");
            return;
        }

        // TODO
        // placing with controller/gesture
        // PlaneDetection
        // raycast hit??
        // #if !UNITY_EDITOR && UNITY_ANDROID
        // #endif

        // TODO
        // height scaling
        // default scale = (h_local - h_hologram_placement) / h_remote
        // remoteSpeakerHologram.localScale = new Vector3(targetPosXZ.x, hologramPosY, targetPosXZ.z);
    }

    #if !UNITY_EDITOR && UNITY_ANDROID
    void PlaneDetection(Vector3 pos)
    {   
        if (planeManager != null && !permissionGranted)
        { 
            remoteSpeakerHologram.position = pos;
            return;
        }

        float finalY = pos.y; 
        bool foundPlane = false;

        foreach (var plane in planeManager.trackables)
        {
            // check if the pos is inside this plane
            Vector2 pointInPlaneSpace = plane.transform.InverseTransformPoint(pos);
                
            if (Mathf.Abs(pointInPlaneSpace.x) <= plane.extents.x && Mathf.Abs(pointInPlaneSpace.y) <= plane.extents.y)
            {
                // ref: https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@6.0/api/UnityEngine.XR.ARSubsystems.PlaneClassifications.html
                switch (plane.classifications)
                {
                    case PlaneClassifications.Table:
                    case PlaneClassifications.Floor:
                    case PlaneClassifications.Seat:
                    case PlaneClassifications.Couch:
                    case PlaneClassifications.SeatOfAnyType:     
                        finalY = plane.transform.position.y;
                        foundPlane = true;
                        break;

                    default:
                        break;
                }

                if (foundPlane) break;
            }
        }

        remoteSpeakerHologram.position = new Vector3(pos.x, finalY, pos.z);

        StartCoroutine(StopPlanesScanning());
    }

    void StartPlanesScanning()
    {
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
        // Skip a frame for the changes to take effect and prefabs get removed.
        yield return new WaitForEndOfFrame();
        planeManager.enabled = false;
        Debug.Log("Stop scanning planes");
    }

    void OnPermissionGranted(string permission)
    {   
        StartPlanesScanning();
        permissionGranted = true;
        planeFeature = OpenXRSettings.Instance.GetFeature<MagicLeapPlanesFeature>();
    }

    void OnPermissionDenied(string permission)
    {
        Debug.LogError($"Failed to create Planes Subsystem due to missing or denied {Permissions.SpatialMapping} permission. Please add to manifest. Disabling script.");
        enabled = false;
    }
    #endif
}
