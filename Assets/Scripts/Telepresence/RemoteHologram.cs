using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using Microsoft.Azure.Kinect.Sensor;
using Microsoft.Azure.Kinect.BodyTracking;
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
    public Transform localSpeaker; // S_a
    public Transform remoteSpeaker; // S_b
    public Transform remoteSpeakerHologram; // P_b (S_b->a)
    public Transform localHologramAtRemote; // P_a (S_a->b)

    void Start()
    {
        #if UNITY_EDITOR || UNITY_STANDALONE_WIN
        _kinectController = FindObjectOfType<KinectController>();
        #endif

        _pcReceiver = gameObject.AddComponent<PointCloudNetworkReceiver>();
        _mdReceiver = gameObject.AddComponent<MetaDataNetworkReceiver>();
        _mdSender = gameObject.AddComponent<MetaDataNetworkSender>();
        _gazeAlignment = gameObject.AddComponent<GazeAlignment>();
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
        if (_mdSender != null && localSpeaker != null)
        {
            _mdSender.localTransform = localSpeaker;
            _mdSender.hologramTransform = remoteSpeakerHologram;
        }
    }

    void CalculatePlacementPosition()
    {
        Vector3 forwardVec = localSpeaker.forward;
        forwardVec.y = 0;
        forwardVec.Normalize();

        float distance = 1.5f; // comfortable social distances: 1.2m-3.7m
        Vector3 targetPosXZ = localSpeaker.position + forwardVec * distance;

        // TODO 
        // y controller / plane / floor
        float hologramPosY = 0f;
        remoteSpeakerHologram.position = new Vector3(targetPosXZ.x, hologramPosY, targetPosXZ.z);

        // TODO
        // default scale = (h_local - h_hologram_placement) / h_remote
        // remoteSpeakerHologram.localScale = new Vector3(targetPosXZ.x, hologramPosY, targetPosXZ.z);
    }
}
