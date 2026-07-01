using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LocalHologram : MonoBehaviour
{
    public bool renderLocalUser = false;
    public bool sendData = true;

    private GameObject pointCloudInstance;

    private KinectController _kinectController;
    private PointCloudNetworkSender _networkSender;
    private bool attachedSender = false;

    void Start()
    {
        _kinectController = FindObjectOfType<KinectController>();
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

        if (_networkSender == null && attachedSender == false)
        {
            _networkSender = gameObject.AddComponent<PointCloudNetworkSender>();
            Debug.Log("[LocalHologram] Auto-attached PointCloudNetworkSender");
            attachedSender = true;
        }
    }
}
