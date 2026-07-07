using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class PointCloudNetworkSender : MonoBehaviour
{
    public KinectController _kinectController;

    [Header("Network Settings")]
    public string targetIP = "129.11.145.130"; // TODO ml2 ip address: 192.168.42.51, ml2 pc: 129.11.145.130
    public int targetPort = 8080; // TODO 8080-pointcloud 8081-metadata

    private UdpClient _udpClient;
    private Thread _sendThread;
    private bool _running;

    private int frameCounter = 0;
    private bool kinectConfigACK = false;
    private const int MAX_UDP_PACKET_SIZE = 60000; // maximum length of a single packet (less than 64 KB)

    void Start()
    {
        _kinectController = FindObjectOfType<KinectController>();

        _udpClient = new UdpClient();
        _udpClient.Client.ReceiveTimeout = 1; // 0.001s (1ms)
        _running = true;

        _sendThread = new Thread(SendLoop);
        _sendThread.IsBackground = true;
        _sendThread.Start();
    }

    private void SendLoop()
    {
        while (_running)
        {
            IPEndPoint anyEP = new IPEndPoint(IPAddress.Any, 0);

            if (!kinectConfigACK)
            {
                try
                {
                    while (_udpClient.Available > 0)
                    {
                        byte[] ackPacket = _udpClient.Receive(ref anyEP);
                        if (ackPacket.Length > 0 && ackPacket[0] == 0x99) 
                        {
                            kinectConfigACK = true;
                            Debug.Log("Receiver confirmed. Starting point cloud stream");
                            break;
                        }
                    }
                }
                catch (SocketException)
                {
                    // Debug.Log("timeout");
                }

                if (!kinectConfigACK)
                {
                    // network configuration (packetID = -1)
                    SendKinectConfig();
                    Thread.Sleep(300); // 0.3s
                    continue;
                }
            }

            // point cloud data
            byte[] localColor = null;
            byte[] localDepth = null;
            byte[] localBodyIndex = null;

            lock (_kinectController.m_bufferLock)
            {
                if (_kinectController.m_colorImage != null && _kinectController.m_colorImage.Length > 0)
                {
                    localColor = (byte[])_kinectController.m_colorImage.Clone();
                    localDepth = (byte[])_kinectController.m_depthImage.Clone();
                    localBodyIndex = (byte[])_kinectController.m_bodyIndexMap.Clone();
                }
            }

            if (localColor != null && localDepth != null && localBodyIndex != null)
            {
                // combine data
                byte[] pointCloudData = CombineFrameData(localColor, localDepth, localBodyIndex);
                SendLargeFrame(pointCloudData);
            }

            // control sending frequency, CameraFPS is set to 30 fps in KinectController
            Thread.Sleep(33);
        }
    }

    private void SendKinectConfig()
    {
        if (_kinectController?.calibrationTable == null)
        {
            return; 
        }

        Debug.Log("Sending config: " + targetIP + " " + targetPort);

        int height = _kinectController.depthHeight;
        int width = _kinectController.depthWidth;
        float[] calibTable = _kinectController.calibrationTable;
        int calibSize = calibTable.Length;

        // total size: 12 Byte Header + 4 Byte Height + 4 Byte Width + 4 Byte Size + (calibSize * 4) Byte Table
        int totalSize = 12 + 4 + 4 + 4 + (calibSize * 4);
        byte[] configPacket = new byte[totalSize];

        // write header
        Buffer.BlockCopy(BitConverter.GetBytes(0), 0, configPacket, 0, 4); // frame index: 0
        Buffer.BlockCopy(BitConverter.GetBytes(-1), 0, configPacket, 4, 4); // packetId: -1
        Buffer.BlockCopy(BitConverter.GetBytes(1), 0, configPacket, 8, 4); // total packet: 1

        // write kinect config data
        int offset = 12; // 12 - header size
        Buffer.BlockCopy(BitConverter.GetBytes(height), 0, configPacket, offset, 4); offset += 4;
        Buffer.BlockCopy(BitConverter.GetBytes(width), 0, configPacket, offset, 4); offset += 4;
        Buffer.BlockCopy(BitConverter.GetBytes(calibSize), 0, configPacket, offset, 4); offset += 4;
        Buffer.BlockCopy(calibTable, 0, configPacket, offset, calibSize * 4);
        // offset += calibSize * 4;

        _udpClient.Send(configPacket, configPacket.Length, targetIP, targetPort);
    }

    private byte[] CombineFrameData(byte[] color, byte[] depth, byte[] bodyIndex)
    {
        byte[] combined = new byte[color.Length + depth.Length + bodyIndex.Length];
        
        int offset = 0;
        Buffer.BlockCopy(color, 0, combined, offset, color.Length); offset += color.Length;
        Buffer.BlockCopy(depth, 0, combined, offset, depth.Length); offset += depth.Length;
        Buffer.BlockCopy(bodyIndex, 0, combined, offset, bodyIndex.Length);

        return combined;
    }

    private void SendLargeFrame(byte[] fullFrameData)
    {
        frameCounter++;
        int totalBytes = fullFrameData.Length;
        // calculate how many packets needs to be send in this frame
        int totalPackets = Mathf.CeilToInt((float)totalBytes / MAX_UDP_PACKET_SIZE);

        for (int i = 0; i < totalPackets; i++)
        {
            int currentOffset = i * MAX_UDP_PACKET_SIZE;
            int sizeToSend = Mathf.Min(MAX_UDP_PACKET_SIZE, totalBytes - currentOffset);

            // 12 Byte Header + Payload
            byte[] packet = new byte[12 + sizeToSend];
            // write header
            Buffer.BlockCopy(BitConverter.GetBytes(frameCounter), 0, packet, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(i), 0, packet, 4, 4);         
            Buffer.BlockCopy(BitConverter.GetBytes(totalPackets), 0, packet, 8, 4); 
            Buffer.BlockCopy(fullFrameData, currentOffset, packet, 12, sizeToSend);

            try
            {
                _udpClient.Send(packet, packet.Length, targetIP, targetPort); 
            }
            catch (Exception e)
            {
                Debug.LogWarning("UDP error: " + e.Message);
            }
        }
    }

    private void OnApplicationQuit()
    {
        _running = false;
        if (_udpClient != null) _udpClient.Close();
        if (_sendThread != null && _sendThread.IsAlive) _sendThread.Join();
    }
}
