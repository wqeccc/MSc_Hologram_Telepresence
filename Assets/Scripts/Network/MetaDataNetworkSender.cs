using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class MetaDataNetworkSender : MonoBehaviour
{
    [HideInInspector]
    public Transform localTransform;     // S_a
    [HideInInspector]
    public Transform hologramTransform;  // P_b (S_b->a)

    [Header("Network Settings")]
    public string targetIP = "192.168.42.51"; // TODO ml2 ip address: 192.168.42.51, ml2 pc: 129.11.145.130
    public int targetPort = 8081;

    private UdpClient _udpClient;
    private Thread _sendThread;
    private bool _running;

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
            if (localTransform != null && hologramTransform != null)
            {
                // S_a
                Vector3 saPos = localTransform.position;
                Quaternion saRot = localTransform.rotation;
                // P_b
                Vector3 pbPos = hologramTransform.position;
                Quaternion pbRot = hologramTransform.rotation;

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
