/**
    udp point cloud receiver
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class PointCloudNetworkReceiver : MonoBehaviour
{
    Material _renderMaterial;
    List<GameObject> _cloudGameObjs;
    Texture2D _depthTexture;
    Texture2D _colorTexture;
    Texture2D _bodyIndexTexture;
    bool _texturesInitialized;
    bool _networkInitialized;

    int _depthWidth;
    int _depthHeight;
    float[] _calibrationTable;

    // buffer pool
    const int _nOfBufferFrames = 5;
    Stack<byte[]> _colorFramesEmpty;
    Stack<byte[]> _depthFramesEmpty;
    Stack<byte[]> _bodyIndexFramesEmpty;
    Queue<byte[]> _colorFrames;
    Queue<byte[]> _depthFrames;
    Queue<byte[]> _bodyIndexFrames;

    object _framesLock;

    UdpClient _udpClient;
    Thread _networkThread;
    bool _running;
    
    private Dictionary<int, byte[]> _framePacketsBuffer = new Dictionary<int, byte[]>();
    private int _currentAssemblingFrame = -1;
    private int _colorByteSize;
    private int _depthByteSize;
    private int _bodyIndexByteSize;
    private bool _configReceived = false;

    public bool hideNonSkeletonPixels = true;
    public int listeningPort = 8080;

    private GameObject _remoteSpeakerObj;
    public Transform GetRemoteSpeakerTransform => _remoteSpeakerObj != null ? _remoteSpeakerObj.transform : null;

    void Start()
    {
        _texturesInitialized = false;
        _cloudGameObjs = new List<GameObject>();
        _networkInitialized = false;
        _colorFramesEmpty = new Stack<byte[]>();
        _depthFramesEmpty = new Stack<byte[]>();
        _bodyIndexFramesEmpty = new Stack<byte[]>();
        _colorFrames = new Queue<byte[]>();
        _depthFrames = new Queue<byte[]>();
        _bodyIndexFrames = new Queue<byte[]>();
        _framesLock = new object();
        _running = true;

        _remoteSpeakerObj = new GameObject("RemoteSpeaker");
        _remoteSpeakerObj.transform.parent = this.transform;
        _remoteSpeakerObj.transform.localPosition = Vector3.zero;
        _remoteSpeakerObj.transform.localRotation = Quaternion.identity;
        _remoteSpeakerObj.transform.localScale = Vector3.one;

        BoxCollider boxCollider = _remoteSpeakerObj.AddComponent<BoxCollider>();
        boxCollider.center = new Vector3(0, 0, 4.5f);
        boxCollider.size = new Vector3(5, 5, 5);

        _networkThread = new Thread(networkLoop);
        _networkThread.IsBackground = true;
        _networkThread.Start();
    }

    private void networkLoop()
    {
        try
        {
            _udpClient = new UdpClient(listeningPort);
            // receive packets from any ip on listeningPort
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, listeningPort);
            Debug.Log($"UDP Receiver started. Listening on port {listeningPort}");

            while (_running)
            {
                byte[] rawPacket = _udpClient.Receive(ref remoteEP);
                if (rawPacket.Length < 12) continue; // header (12 bytes)

                // parse header
                int frameID = BitConverter.ToInt32(rawPacket, 0);
                int packetID = BitConverter.ToInt32(rawPacket, 4);
                int totalPackets = BitConverter.ToInt32(rawPacket, 8);

                // kinect configuration
                if (packetID == -1)
                {
                    if (!_configReceived)
                    {
                        ParseKinectConfig(rawPacket);
                        Debug.Log("Received kinect config");
                    }

                    byte[] ackData = new byte[] { 0x99 };
                    _udpClient.Send(ackData, ackData.Length, remoteEP);

                    continue;
                }

                // only process point cloud data when kinect configuration is received
                if (!_configReceived) continue;

                // handle point cloud data
                // the data in one frame would be cut into many packets since the maximum size of a UDP packet is 64 kb
                if (frameID > _currentAssemblingFrame)
                {
                    // start collecting next frame data
                    _currentAssemblingFrame = frameID;
                    _framePacketsBuffer.Clear();
                }

                // combine packets
                if (frameID == _currentAssemblingFrame)
                {
                    byte[] payload = new byte[rawPacket.Length - 12];
                    Buffer.BlockCopy(rawPacket, 12, payload, 0, payload.Length);

                    if (!_framePacketsBuffer.ContainsKey(packetID))
                    {
                        _framePacketsBuffer.Add(packetID, payload);
                    }

                    // check if all the packets have been collected
                    if (_framePacketsBuffer.Count == totalPackets)
                    {
                        // Color + Depth + BodyIndex
                        byte[] fullFrameData = AssembleFrameData(totalPackets);

                        DistributeFrameData(fullFrameData);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("UDP error: " + e.Message);
        }
    }

    private void ParseKinectConfig(byte[] configPacket)
    {
        int offset = 12; // skip header
        
        _depthHeight = BitConverter.ToInt32(configPacket, offset);
        offset += 4;
        _depthWidth = BitConverter.ToInt32(configPacket, offset);
        offset += 4;
        int calibrationSize = BitConverter.ToInt32(configPacket, offset);
        offset += 4;

        _calibrationTable = new float[calibrationSize];
        for (int i = 0; i < calibrationSize; i++)
        {
            _calibrationTable[i] = BitConverter.ToSingle(configPacket, offset);
            offset += 4;
        }

        _colorByteSize = _depthWidth * _depthHeight * 4;
        _depthByteSize = _depthWidth * _depthHeight * 2;
        _bodyIndexByteSize = _depthWidth * _depthHeight;

        for (int i = 0; i < _nOfBufferFrames; i++)
        {
            _colorFramesEmpty.Push(new byte[_colorByteSize]);
            _depthFramesEmpty.Push(new byte[_depthByteSize]);
            _bodyIndexFramesEmpty.Push(new byte[_bodyIndexByteSize]);
        }

        _configReceived = true;
        _networkInitialized = true;
    }

    private byte[] AssembleFrameData(int totalPackets)
    {
        int totalSize = 0;
        for (int i = 0; i < totalPackets; i++)
        {
            totalSize += _framePacketsBuffer[i].Length;
        }

        byte[] fullFrame = new byte[totalSize];
        int currentOffset = 0;

        for (int i = 0; i < totalPackets; i++)
        {
            Buffer.BlockCopy(_framePacketsBuffer[i], 0, fullFrame, currentOffset, _framePacketsBuffer[i].Length);
            currentOffset += _framePacketsBuffer[i].Length;
        }
        return fullFrame;
    }

    private void DistributeFrameData(byte[] fullFrameData)
    {
        int expectedPointCloudSize = _colorByteSize + _depthByteSize + _bodyIndexByteSize;
        if (fullFrameData.Length < expectedPointCloudSize) return;

        byte[] colorBuffer;
        byte[] depthBuffer;
        byte[] bodyIndexBuffer;

        lock (_framesLock)
        {
            if (_colorFramesEmpty.Count == 0)
                refillEmptyStack();

            colorBuffer = _colorFramesEmpty.Pop();
            depthBuffer = _depthFramesEmpty.Pop();
            bodyIndexBuffer = _bodyIndexFramesEmpty.Pop();
        }

        int offset = 0;
        Buffer.BlockCopy(fullFrameData, offset, colorBuffer, 0, _colorByteSize);
        offset += _colorByteSize;
        Buffer.BlockCopy(fullFrameData, offset, depthBuffer, 0, _depthByteSize);
        offset += _depthByteSize;
        Buffer.BlockCopy(fullFrameData, offset, bodyIndexBuffer, 0, _bodyIndexByteSize);

        lock (_framesLock)
        {
            _colorFrames.Enqueue(colorBuffer);
            _depthFrames.Enqueue(depthBuffer);
            _bodyIndexFrames.Enqueue(bodyIndexBuffer);
        }
    }

    void refillEmptyStack()
    {
        for (int i = 0; i < _colorFrames.Count - 1; i++)
        {
            _colorFramesEmpty.Push(_colorFrames.Dequeue());
            _depthFramesEmpty.Push(_depthFrames.Dequeue());
            _bodyIndexFramesEmpty.Push(_bodyIndexFrames.Dequeue());
        }
    }

    private void initializePointCloudData_old()
    {
        _renderMaterial = Resources.Load("Materials/cloudmatDepth") as Material;
        List<Vector3> points = new List<Vector3>();
        List<int> ind = new List<int>();
        int n = 0; int i = 0;

        for (float w = 0; w < _depthWidth; w++)
        {
            for (float h = 0; h < _depthHeight; h++)
            {
                Vector3 p = new Vector3(w / _depthWidth, h / _depthHeight, 0);
                points.Add(p); ind.Add(n); n++;

                if (n == 65000)
                {
                    GameObject a = new GameObject("cloud" + i);
                    a.AddComponent<MeshFilter>().mesh = new Mesh { vertices = points.ToArray() };
                    a.GetComponent<MeshFilter>().mesh.SetIndices(ind.ToArray(), MeshTopology.Points, 0);
                    a.GetComponent<MeshFilter>().mesh.bounds = new Bounds(new Vector3(0, 0, 4.5f), new Vector3(5, 5, 5));
                    a.AddComponent<MeshRenderer>().material = _renderMaterial;

                    a.transform.parent = _remoteSpeakerObj.transform;
                    a.transform.localPosition = Vector3.zero;
                    a.transform.localRotation = Quaternion.identity;
                    a.transform.localScale = Vector3.one;
                    n = 0; i++; _cloudGameObjs.Add(a);
                    points = new List<Vector3>(); ind = new List<int>();
                }
            }
        }
        GameObject afinal = new GameObject("cloud" + i);
        afinal.AddComponent<MeshFilter>().mesh = new Mesh { vertices = points.ToArray() };
        afinal.GetComponent<MeshFilter>().mesh.SetIndices(ind.ToArray(), MeshTopology.Points, 0);
        afinal.AddComponent<MeshRenderer>().material = _renderMaterial;
        
        afinal.transform.parent = _remoteSpeakerObj.transform;
        afinal.transform.localPosition = Vector3.zero;
        afinal.transform.localRotation = Quaternion.identity;
        afinal.transform.localScale = Vector3.one;
        _cloudGameObjs.Add(afinal);
    }

    private void initializePointCloudData()
    {
        _renderMaterial = Resources.Load("Materials/hologramMat") as Material;
        
        List<Vector3> points = new List<Vector3>();
        List<Vector2> uv0s = new List<Vector2>(); // Kinect uv
        List<Vector2> uv1s = new List<Vector2>(); // quad
        List<int> ind = new List<int>(); // mesh triangle index
        
        // n: vertex index, i: gameObject index
        int n = 0, i = 0;

        // pixels
        for (float w = 0; w < _depthWidth; w++)
        {
            for (float h = 0; h < _depthHeight; h++)
            {
                // normalized coordinates
                float u = w / _depthWidth;
                float v = h / _depthHeight;
                Vector2 kinectUV = new Vector2(u, v);

                // 4 vertices of a quad
                points.Add(Vector3.zero); uv0s.Add(kinectUV);
                points.Add(Vector3.zero); uv0s.Add(kinectUV);
                points.Add(Vector3.zero); uv0s.Add(kinectUV);
                points.Add(Vector3.zero); uv0s.Add(kinectUV);

                uv1s.Add(new Vector2(0, 0)); // bottom-left
                uv1s.Add(new Vector2(1, 0)); // bottom-right
                uv1s.Add(new Vector2(0, 1)); // top-left
                uv1s.Add(new Vector2(1, 1)); // top-right

                /**
                 * clockwise
                 *   2 __ 3
                 *   |    |
                 *   0 __ 1 
                 */
                ind.Add(n + 0); ind.Add(n + 2); ind.Add(n + 1);
                ind.Add(n + 2); ind.Add(n + 3); ind.Add(n + 1);
                
                // next quad
                n += 4;

                if (n >= 65000)
                {
                    CreateCloudGameObject(points, uv0s, uv1s, ind, i);
                    n = 0; i++;
                    points = new List<Vector3>();
                    uv0s = new List<Vector2>();
                    uv1s = new List<Vector2>();
                    ind = new List<int>();
                }
            }
        }

        // handles rest points
        if (points.Count > 0)
        {
            CreateCloudGameObject(points, uv0s, uv1s, ind, i);
        }
    }

    private void CreateCloudGameObject(List<Vector3> points, List<Vector2> uv0s, List<Vector2> uv1s, List<int> ind, int index)
    {
        GameObject a = new GameObject("cloud" + index);
        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        
        mesh.vertices = points.ToArray();
        mesh.SetUVs(0, uv0s); // Shader TEXCOORD0
        mesh.SetUVs(1, uv1s); // Shader TEXCOORD1
        
        // MeshTopology.Points -> MeshTopology.Triangles
        mesh.SetIndices(ind.ToArray(), MeshTopology.Triangles, 0); 
        mesh.bounds = new Bounds(new Vector3(0, 0, 4.5f), new Vector3(5, 5, 5));
        mesh.UploadMeshData(true); 

        a.AddComponent<MeshFilter>().mesh = mesh;
        a.AddComponent<MeshRenderer>().material = _renderMaterial;

        a.transform.parent = _remoteSpeakerObj.transform;
        a.transform.localPosition = Vector3.zero;
        a.transform.localRotation = Quaternion.identity;
        a.transform.localScale = Vector3.one;
        
        _cloudGameObjs.Add(a);
    }

    void PostKinectInit()
    {
        _depthTexture = new Texture2D(_depthWidth, _depthHeight, TextureFormat.RG16, false, true);
        _colorTexture = new Texture2D(_depthWidth, _depthHeight, TextureFormat.BGRA32, false);
        _bodyIndexTexture = new Texture2D(_depthWidth, _depthHeight, TextureFormat.Alpha8, false);
        _depthTexture.filterMode = FilterMode.Point;
        _colorTexture.filterMode = FilterMode.Point;
        
        initializePointCloudData();

        _renderMaterial.SetFloatArray("camera_calibration", _calibrationTable);
        _renderMaterial.SetFloat("camera_width", _depthWidth);
        _renderMaterial.SetFloat("camera_height", _depthHeight);
        _texturesInitialized = true;
    }

    // render point cloud
    void Update()
    {
        if (!_networkInitialized) return;
        if (_networkInitialized && !_texturesInitialized) PostKinectInit();

        lock (_framesLock)
        {
            refillEmptyStack();
            if (_colorFrames.Count > 0)
            {
                byte[] buffer = _colorFrames.Dequeue();
                byte[] dbuffer = _depthFrames.Dequeue();
                byte[] pbuffer = _bodyIndexFrames.Dequeue();
                _colorTexture.LoadRawTextureData(buffer);
                _depthTexture.LoadRawTextureData(dbuffer);
                _bodyIndexTexture.LoadRawTextureData(pbuffer);
                _colorFramesEmpty.Push(buffer);
                _depthFramesEmpty.Push(dbuffer);
                _bodyIndexFramesEmpty.Push(pbuffer);
            }
        }

        _colorTexture.Apply();
        _depthTexture.Apply();
        _bodyIndexTexture.Apply();
        
        if (_remoteSpeakerObj != null)
        {
            MeshRenderer[] renderers = _remoteSpeakerObj.GetComponentsInChildren<MeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer mr = renderers[i];
                mr.material.SetInt("_RemoveBackground", hideNonSkeletonPixels ? 1 : 0);
                mr.material.SetTexture("_ColorTex", _colorTexture);
                mr.material.SetTexture("_DepthTex", _depthTexture);
                mr.material.SetTexture("_BodyIndexTex", _bodyIndexTexture);
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
