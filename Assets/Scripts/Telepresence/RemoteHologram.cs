using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using Microsoft.Azure.Kinect.Sensor;
using Microsoft.Azure.Kinect.BodyTracking;
#endif

public class RemoteHologram : MonoBehaviour
{
    // [HideInInspector]
    // local speaker position
    private Vector3 sp_l;
    // local speaker rotation
    private Quaternion sr_l;

    private PointCloudNetworkSender _networkSender;
    private PointCloudNetworkReceiver _networkReceiver;

    #if UNITY_EDITOR || UNITY_STANDALONE_WIN
    private KinectController _kinectController;
    #endif

    // [Header("Hologram B")]
    // [Tooltip("S_b -> A")]
    // public Transform remotePlacement;

    // network Timer
    // private float sendTimer = 0f;
    // // CameraFPS is set to 30 fps in KinectController
    // private const float SEND_INTERVAL = 0.033f; // 1/30 ~= 30 FPS

    // [Header("自定義的遠端渲染擺放點")]
    // [Tooltip("你想把遠端全息人像放在你本地空間的哪個虛擬位置 (對應論文的 P_A)")]
    // public Transform remotePlacement;

    // [Header("從網絡接收到的遠端 Metadata")]
    // public Vector3 sp_r;      // 遠端傳過來的講者位置 (S_r)
    // public Quaternion sr_r;   // 遠端傳過來的講者朝向 (R_r)

    // private Transform localSpeaker; // 代表本地講者的 Transform (ML2 的 MainCamera)

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
                    sp_l = new Vector3(-headJointPos.X / 1000f, headJointPos.Y / 1000f, headJointPos.Z / 1000f);
                    sr_l = new Quaternion( // TODO or Quaternion.identity?
                        headJoint.Quaternion.X, 
                        headJoint.Quaternion.Y, 
                        headJoint.Quaternion.Z, 
                        headJoint.Quaternion.W
                    );
                    return;
                }
            }
        #else
            // ML2: Main Camera
            if (Camera.main != null)
            {
                sp_l = Camera.main.transform.position;
                sr_l = Camera.main.transform.rotation;
                return;
            }
        #endif

        Debug.LogWarning("[LocalHologram] can't find camera");
    }

    void ApplyHologramTransformation()
    {
        // 根據論文場景（場景一、二、或三），使用：
        // 本地講者 (localSpeaker.position)
        // 遠端講者 (sp_r)
        // 渲染擺放點 (remotePlacement.position)
        // 來即時去計算、縮放、並調整【目前這個 Remote 全息物件】的 transform.position 與 transform.localScale
    }

    void Start()
    {
        #if UNITY_EDITOR || UNITY_STANDALONE_WIN
        _kinectController = FindObjectOfType<KinectController>();
        #endif

        _networkReceiver = gameObject.AddComponent<PointCloudNetworkReceiver>();
        Debug.Log("[LocalHologram] Auto-attached PointCloudNetworkReceiver");

        // if (Camera.main != null)
        // {
        //     localSpeaker = Camera.main.transform;
        // }
    }

    // Update is called once per frame
    void Update()
    {
        #if UNITY_EDITOR || UNITY_STANDALONE_WIN
        if (_kinectController == null || !_kinectController.kinectInitialized)
        {
            Debug.LogWarning("Kinect not Initialized");
            return;
        }
        #endif

        // // 1. 從網絡層拉取最新收到的遠端講者數據
        // if (_networkReceiver != null && _networkReceiver.HasNewMetadata())
        // {
        //     sp_r = _networkReceiver.GetRemoteSpeakerPosition();
        //     sr_r = _networkReceiver.GetRemoteSpeakerRotation();
        // }

        // UpdateLocalUserPosition();

        // // 2. 執行論文中的矩陣縮放與座標轉換公式
        // if (remotePlacement != null && localSpeaker != null)
        // {
        //     ApplyHologramTransformation();
        // }

        // if (sendData && _networkSender != null)
        // {
        //     sendTimer += Time.deltaTime;
        //     if (sendTimer >= SEND_INTERVAL)
        //     {
        //         sendTimer = 0f;

        //         // remote speaker position
        //         // Vector3 P_local = pointCloudInstance != null ? pointCloudInstance.transform.position : Vector3.zero;
        //         // Vector3 P_local = remotePlacement.position;
        //         // remote speaker rotation
        //         // Quaternion P_rot = pointCloudInstance != null ? pointCloudInstance.transform.rotation : Quaternion.identity;
        //         // Quaternion P_rot = remotePlacement.rotation;

        //         // send data
        //         _networkSender.UpdateLatestMetadata(sp_l, sr_l);
        //     }
        // }
    }
}
