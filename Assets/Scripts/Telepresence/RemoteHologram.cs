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

    // #if !UNITY_EDITOR && UNITY_ANDROID
    //     private ML2Layer _ml2Layer;
    //     private bool _wasAttachedLastFrame = false;
    // #endif

    public bool enableGazeAlignment = true;

    [Header("Hologram Objects")]
    // a: local hologram space
    public Pose localSpeaker; // S_a
    public Pose remoteSpeaker; // S_b
    public Transform remoteSpeakerHologram; // P_b (S_b->a)
    public Pose localHologramAtRemote; // P_a (S_a->b)

    // private bool initHologramPos = false;

    void Start()
    {
        #if UNITY_EDITOR || UNITY_STANDALONE_WIN
            _kinectController = FindObjectOfType<KinectController>();
        #endif

        // #if !UNITY_EDITOR && UNITY_ANDROID
        //     _ml2Layer = FindFirstObjectByType<ML2Layer>();
        // #endif

        _pcReceiver = gameObject.AddComponent<PointCloudNetworkReceiver>();
        _mdReceiver = gameObject.AddComponent<MetaDataNetworkReceiver>();
        _mdSender = gameObject.AddComponent<MetaDataNetworkSender>();
        _gazeAlignment = gameObject.AddComponent<GazeAlignment>();

         Debug.Log("unity new v7");
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

        // if (!initHologramPos)
        // {
        //     CalculatePlacementPosition();
        // }
        // else
        // {
            if (enableGazeAlignment && _gazeAlignment != null)
            {
                // S_a, S_b, P_b, P_a
                _gazeAlignment.ExecuteAlgorithm(localSpeaker, remoteSpeaker, remoteSpeakerHologram, localHologramAtRemote);
            }

            // #if !UNITY_EDITOR && UNITY_ANDROID
            //     HandleHologramAttachment();
            // #endif
        // }

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

    // void CalculatePlacementPosition()
    // {
    //     if (localSpeaker == null || remoteSpeakerHologram == null || remoteSpeaker == null)
    //     {
    //         return;
    //     }

    //     // calculate the default position of hologram
    //     Vector3 forwardVec = localSpeaker.forward;
    //     forwardVec.y = 0;
    //     forwardVec.Normalize();

    //     float distance = 1.5f; // comfortable social distances: 1.2m-3.7m
    //     Vector3 targetPosXZ = localSpeaker.position + forwardVec * distance;

    //     // assume the heights (HMDs) are the same
    //     float defaultY = localSpeaker.position.y - remoteSpeaker.position.y;
    //     Vector3 initialPos = new Vector3(targetPosXZ.x, defaultY, targetPosXZ.z);

    //     // #if !UNITY_EDITOR && UNITY_ANDROID
    //     //     remoteSpeakerHologram.position = _ml2Layer.PlaneDetection(initialPos);
    //     // #else
    //         remoteSpeakerHologram.position = initialPos;
    //     // #endif

    //     // HeightScaling();

    //     initHologramPos = true;
    //     Debug.Log("Inintialized hologram default position");
    // }

    // void HeightScaling()
    // {
    //     float heightDiff = localSpeaker.position.y - remoteSpeakerHologram.position.y;
    //     float scale = heightDiff / remoteSpeaker.position.y;
    //     scale = Mathf.Clamp(scale, 0.1f, 2.0f);

    //     remoteSpeakerHologram.localScale = new Vector3(scale, scale, scale);
    // }

    // #if !UNITY_EDITOR && UNITY_ANDROID
    // void HandleHologramAttachment()
    // {
    //     if (_ml2Layer == null || remoteSpeakerHologram == null) return;

    //     if (_ml2Layer.isAttached)
    //     {
    //         Vector3 rayStart = _ml2Layer.PointerPosition;
    //         Vector3 rayDir = _ml2Layer.PointerRotation * Vector3.forward;

    //         // keep 1.5m in front of the controller
    //         float attachDistance = 1.5f;
    //         Vector3 targetPos = rayStart + rayDir * attachDistance;

    //         remoteSpeakerHologram.position = targetPos;

    //         HeightScaling();
    //         _wasAttachedLastFrame = true;
    //     }
    //     else if (_wasAttachedLastFrame)
    //     {
    //         Vector3 finalPos = _ml2Layer.PlaneDetection(remoteSpeakerHologram.position);
    //         remoteSpeakerHologram.position = finalPos;
            
    //         HeightScaling();
    //         _wasAttachedLastFrame = false;
    //     }
    // }
    // #endif
}
