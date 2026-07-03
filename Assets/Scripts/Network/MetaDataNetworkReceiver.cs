using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class MetaDataNetworkReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    public int localListeningPort = 8081;

    private UdpClient _udpClient;
    private Thread _networkThread;
    private bool _running;

    // S_b
    private Vector3 _remoteSpeakerPos;
    private Quaternion _remoteSpeakerRot;
    // P_a
    private Vector3 _localHologramAtRemotePos;
    private Quaternion _localHologramAtRemoteRot;

    private readonly object _metaLock = new object();

    public Vector3 GetRemoteSpeakerPos { get { lock (_metaLock) return _remoteSpeakerPos; } }
    public Quaternion GetRemoteSpeakerRot { get { lock (_metaLock) return _remoteSpeakerRot; } }
    public Vector3 GetLocalHologramAtRemotePos { get { lock (_metaLock) return _localHologramAtRemotePos; } }
    public Quaternion GetLocalHologramAtRemoteRot { get { lock (_metaLock) return _localHologramAtRemoteRot; } }

    void Start()
    {
        _running = true;
        _networkThread = new Thread(NetworkLoop);
        _networkThread.IsBackground = true;
        _networkThread.Start();
    }

    private void NetworkLoop()
    {
        try
        {
            _udpClient = new UdpClient(localListeningPort);
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, localListeningPort);
            Debug.Log($"Metadata UDP Receiver started. Listening on port {localListeningPort}...");

            while (_running)
            {
                byte[] rawPacket = _udpClient.Receive(ref remoteEP);

                // 56 Byte
                if (rawPacket.Length < 56) continue; 

                lock (_metaLock)
                {
                    int offset = 0;

                    // S_b (0 - 27 byte)
                    _remoteSpeakerPos.x = BitConverter.ToSingle(rawPacket, offset); offset += 4;
                    _remoteSpeakerPos.y = BitConverter.ToSingle(rawPacket, offset); offset += 4;
                    _remoteSpeakerPos.z = BitConverter.ToSingle(rawPacket, offset); offset += 4;

                    _remoteSpeakerRot.x = BitConverter.ToSingle(rawPacket, offset); offset += 4;
                    _remoteSpeakerRot.y = BitConverter.ToSingle(rawPacket, offset); offset += 4;
                    _remoteSpeakerRot.z = BitConverter.ToSingle(rawPacket, offset); offset += 4;
                    _remoteSpeakerRot.w = BitConverter.ToSingle(rawPacket, offset); offset += 4;

                    // P_a (28 - 55 byte)
                    _localHologramAtRemotePos.x = BitConverter.ToSingle(rawPacket, offset); offset += 4;
                    _localHologramAtRemotePos.y = BitConverter.ToSingle(rawPacket, offset); offset += 4;
                    _localHologramAtRemotePos.z = BitConverter.ToSingle(rawPacket, offset); offset += 4;

                    _localHologramAtRemoteRot.x = BitConverter.ToSingle(rawPacket, offset); offset += 4;
                    _localHologramAtRemoteRot.y = BitConverter.ToSingle(rawPacket, offset); offset += 4;
                    _localHologramAtRemoteRot.z = BitConverter.ToSingle(rawPacket, offset); offset += 4;
                    _localHologramAtRemoteRot.w = BitConverter.ToSingle(rawPacket, offset);
                }
            }
        }
        catch (Exception e)
        {
            if (_running)
            {
                Debug.LogError("Metadata receiver error: " + e.Message);
            }
        }
    }

    private void OnApplicationQuit()
    {
        _running = false;
        if (_udpClient != null) _udpClient.Close();
        if (_networkThread != null && _networkThread.IsAlive) _networkThread.Join();
    }
}
