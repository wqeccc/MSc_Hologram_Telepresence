using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class MetaDataNetworkSender : MonoBehaviour
{
    readonly object _dataLock = new object();
    Vector3 _localTransform_pos;
    Quaternion _localTransform_rot;
    Vector3 _hologramTransform_pos;
    Quaternion _hologramTransform_rot;

    // S_a
    [HideInInspector]
    public Vector3 localTransform_pos
    {
        get { lock (_dataLock) return _localTransform_pos; }
        set { lock (_dataLock) _localTransform_pos = value; }
    }
    [HideInInspector]
    public Quaternion localTransform_rot
    {
        get { lock (_dataLock) return _localTransform_rot; }
        set { lock (_dataLock) _localTransform_rot = value; }
    }

    // P_b (S_b->a)
    [HideInInspector]
    public Vector3 hologramTransform_pos
    {
        get { lock (_dataLock) return _hologramTransform_pos; }
        set { lock (_dataLock) _hologramTransform_pos = value; }
    }
    [HideInInspector]
    public Quaternion hologramTransform_rot
    {
        get { lock (_dataLock) return _hologramTransform_rot; }
        set { lock (_dataLock) _hologramTransform_rot = value; }
    }

    [Header("Network Settings")]
    public string targetIP = "129.11.145.107"; //  ml2 ip address:192.168.137.172, ml2 pc: 129.11.145.130, pc: 192.168.137.105
    public int targetPort = 50052; // 50051-pointcloud 50052-metadata

    private UdpClient _udpClient;
    private Thread _sendThread;
    private bool _running;

    // TODO data lock

    void Start()
    {
        _udpClient = new UdpClient();
        _running = true;

        _sendThread = new Thread(SendLoop);
        _sendThread.IsBackground = true;
        _sendThread.Start();
    }

    private void SendLoop()
    {
        // 14 float * 4 byte = 56 byte
        byte[] metaPacket = new byte[56];

        while (_running)
        {
            // S_a
            Vector3 saPos = localTransform_pos;
            Quaternion saRot = localTransform_rot;
            // P_b
            Vector3 pbPos = hologramTransform_pos;
            Quaternion pbRot = hologramTransform_rot;

            int offset = 0;

            // Position (X, Y, Z) - 12 Byte
            Buffer.BlockCopy(BitConverter.GetBytes(saPos.x), 0, metaPacket, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(saPos.y), 0, metaPacket, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(saPos.z), 0, metaPacket, offset, 4); offset += 4;

            // Rotation (X, Y, Z, W) - 16 Byte
            Buffer.BlockCopy(BitConverter.GetBytes(saRot.x), 0, metaPacket, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(saRot.y), 0, metaPacket, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(saRot.z), 0, metaPacket, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(saRot.w), 0, metaPacket, offset, 4); offset += 4;

            // Position (X, Y, Z) - 12 Byte
            Buffer.BlockCopy(BitConverter.GetBytes(pbPos.x), 0, metaPacket, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(pbPos.y), 0, metaPacket, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(pbPos.z), 0, metaPacket, offset, 4); offset += 4;

            // Rotation (X, Y, Z, W) - 16 Byte
            Buffer.BlockCopy(BitConverter.GetBytes(pbRot.x), 0, metaPacket, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(pbRot.y), 0, metaPacket, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(pbRot.z), 0, metaPacket, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(pbRot.w), 0, metaPacket, offset, 4);

            try
            {
                _udpClient.Send(metaPacket, metaPacket.Length, targetIP, targetPort);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Metadata UDP sender error: " + e.Message);
            }

            // 30 FPS
            Thread.Sleep(33);
        }
    }

    private void OnApplicationQuit()
    {
        _running = false;
        if (_udpClient != null) _udpClient.Close();
        if (_sendThread != null && _sendThread.IsAlive) _sendThread.Join();
    }
}
