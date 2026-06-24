using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Microsoft.Azure.Kinect.Sensor;
using Microsoft.Azure.Kinect.BodyTracking;

public class LocalHologram : MonoBehaviour
{
    public bool renderLocalUser = false;
    public bool sendData = true;

    // local speaker position
    [HideInInspector]
    public Vector3 sp_l;

    // platform: magic leap 2 / pc
    private bool isML2;
    private GameObject pointCloudInstance;
    private NetworkSender _networkSender;
    private KinectController _kinectController;

    void Start()
    {
        isML2 = Application.platform == RuntimePlatform.Android;
        _kinectController = FindObjectOfType<KinectController>();
        _networkSender = FindObjectOfType<NetworkSender>();

        if (isML2)
            Debug.Log("[LocalHologram] Platform: ML2");
        else
            Debug.Log("[LocalHologram] Platform: PC");
    }

    void UpdateLocalUserPosition()
    {
        // ML2: Main Camera
        if (isML2)
        {
            if (Camera.main != null)
            {
                sp_l = Camera.main.transform.position;
                return;
            }
        }
        else {
            // PC: Kinect skeleton head
            lock (_kinectController.m_bufferLock)
            {
                if (_kinectController.m_currentSkeletons != null && _kinectController.m_currentSkeletons.Count > 0)
                {
                    SkeletonInfo info = _kinectController.m_currentSkeletons[0];
                    var headJointPos = info.skeleton.GetJoint(JointId.Head).Position;

                    // kinect(mm): System.Numerics.Vector3 -> unity(m): UnityEngine.Vector3
                    sp_l = new Vector3(-headJointPos.X / 1000f, headJointPos.Y / 1000f, headJointPos.Z / 1000f);
                    return;
                }
            }
        }

        Debug.LogWarning("[LocalHologram] can't find sp_l");
    }

    void CreatePointCloudObject()
    {
        if (pointCloudInstance != null) return;
        Debug.Log("[LocalHologram] Creating GameObject: LocalSpeaker");

        pointCloudInstance = new GameObject("LocalSpeaker");
        pointCloudInstance.transform.SetParent(transform);
        pointCloudInstance.transform.localPosition = Vector3.zero;
        pointCloudInstance.transform.localRotation = Quaternion.identity;

        pointCloudInstance.AddComponent<KinectPointCloud>();
    }

    void RemovePointCloudObject()
    {
        if (pointCloudInstance != null) {
            Debug.Log("[LocalHologram] Deleting GameObject: LocalSpeaker");
            Destroy(pointCloudInstance);
            pointCloudInstance = null;
        }
    }

    void RenderLocalUserHandler(bool r)
    {
        if (r)
            CreatePointCloudObject();
        else
            RemovePointCloudObject();
    }

    void OnValidate()
    {
        if (Application.isPlaying)
        {
            RenderLocalUserHandler(renderLocalUser);
        }
    }

    void Update()
    {
        if (_kinectController == null || !_kinectController.kinectInitialized)
        {
            Debug.LogWarning("Kinect not Initialized");
            return;
        }

        UpdateLocalUserPosition();

        if (sendData && _networkSender != null)
        {
            // Vector3 S_local = sp_l; 
            // Quaternion R_local = isML2 ? Camera.main.transform.rotation : Quaternion.identity;
            // Vector3 P_local = pointCloudInstance != null ? pointCloudInstance.transform.position : Vector3.zero;
            // Quaternion P_rot = pointCloudInstance != null ? pointCloudInstance.transform.rotation : Quaternion.identity;

            // _networkSender.SendLocalData(S_local, R_local, P_local, P_rot);
        }
    }
}
