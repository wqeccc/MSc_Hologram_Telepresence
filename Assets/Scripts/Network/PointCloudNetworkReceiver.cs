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
    private int _playerIndexByteSize;
    private bool _configReceived = false;

    public bool hideNonSkeletonPixels = true;
    public int LocalListeningPort = 8080;

    // TODO comment & change params name

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

        _networkThread = new Thread(networkLoop);
        _networkThread.IsBackground = true;
        _networkThread.Start();
    }

    private void networkLoop()
    {
        try
        {
            // 初始化 UDP 監聽
            _udpClient = new UdpClient(LocalListeningPort);
            // 允許接收來自任何 IP 的封包
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, LocalListeningPort);
            Debug.Log($"UDP Receiver started. Listening on port {LocalListeningPort}...");

            while (_running)
            {
                // 阻塞等待接收原始 UDP 封包
                byte[] rawPacket = _udpClient.Receive(ref remoteEP);

                if (rawPacket.Length < 12) continue; // 格式錯誤的防呆

                // 解析 12 位元組的自訂標頭 (Header)
                int frameID = BitConverter.ToInt32(rawPacket, 0);
                int packetID = BitConverter.ToInt32(rawPacket, 4);
                int totalPackets = BitConverter.ToInt32(rawPacket, 8);

                // 情況 A：收到的是「初始化配置封包」(我們約定 packetID = -1 作為設定檔暗號)
                if (packetID == -1)
                {
                    if (!_configReceived)
                    {
                        ParseConfiguration(rawPacket);
                    }
                    continue;
                }

                // 如果還沒收到配置資訊，先不處理任何點雲數據封包
                if (!_configReceived) continue;

                // 情況 B：處理正常的點雲數據碎片
                // 如果收到更新的一影格，放棄過去沒湊齊的舊碎片，開啟新影格組裝
                if (frameID > _currentAssemblingFrame)
                {
                    _currentAssemblingFrame = frameID;
                    _framePacketsBuffer.Clear();
                }

                if (frameID == _currentAssemblingFrame)
                {
                    // 提取真正的點雲數據碎片 (Payload)
                    byte[] payload = new byte[rawPacket.Length - 12];
                    Buffer.BlockCopy(rawPacket, 12, payload, 0, payload.Length);

                    if (!_framePacketsBuffer.ContainsKey(packetID))
                    {
                        _framePacketsBuffer.Add(packetID, payload);
                    }

                    // 檢查碎片是否全部收集齊全了！
                    if (_framePacketsBuffer.Count == totalPackets)
                    {
                        // 拼回這一影格完整的點雲大蛋糕 (Color + Depth + BodyIndex)
                        byte[] fullFrameData = AssembleFrame(totalPackets);
                        
                        // 將完整大數據拆解分發至對應的渲染佇列中
                        DistributeFrameData(fullFrameData);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("UDP 網路迴圈發生錯誤: " + e.Message);
        }
    }

    // 解析初始化設定資訊 (解析方式與原本 TCP 類似，但資料來源是單個 UDP 封包)
    private void ParseConfiguration(byte[] configPacket)
    {
        int offset = 12; // 跳過 Header
        
        _depthHeight = BitConverter.ToInt32(configPacket, offset); offset += 4;
        _depthWidth = BitConverter.ToInt32(configPacket, offset); offset += 4;
        int calibrationSize = BitConverter.ToInt32(configPacket, offset); offset += 4;

        _calibrationTable = new float[calibrationSize];
        for (int i = 0; i < calibrationSize; i++)
        {
            _calibrationTable[i] = BitConverter.ToSingle(configPacket, offset);
            offset += 4;
        }

        Debug.Log("UDP 成功接收設定資訊: " + _depthHeight + "x" + _depthWidth);

        // 計算每一影格各資料類型所需的精準 Byte 大小
        _colorByteSize = _depthWidth * _depthHeight * 4;
        _depthByteSize = _depthWidth * _depthHeight * 2;
        _playerIndexByteSize = _depthWidth * _depthHeight;

        // 初始化緩衝緩存池
        for (int i = 0; i < _nOfBufferFrames; i++)
        {
            _colorFramesEmpty.Push(new byte[_colorByteSize]);
            _depthFramesEmpty.Push(new byte[_depthByteSize]);
            _bodyIndexFramesEmpty.Push(new byte[_playerIndexByteSize]);
        }

        _configReceived = true;
        _networkInitialized = true; // 觸發主執行緒的 PostKinectInit
    }

    // 將所有零散的 UDP 碎片拼接成完整的一影格總陣列
    private byte[] AssembleFrame(int totalPackets)
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
        int expectedPointCloudSize = _colorByteSize + _depthByteSize + _playerIndexByteSize;
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
        Buffer.BlockCopy(fullFrameData, offset, bodyIndexBuffer, 0, _playerIndexByteSize);

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

    private void initializePointCloudData()
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
                    a.transform.parent = this.gameObject.transform;
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
        afinal.transform.parent = this.gameObject.transform;
        afinal.transform.localPosition = Vector3.zero;
        afinal.transform.localRotation = Quaternion.identity;
        afinal.transform.localScale = Vector3.one;
        _cloudGameObjs.Add(afinal);
    }

    void PostKinectInit()
    {
        _depthTexture = new Texture2D(_depthWidth, _depthHeight, TextureFormat.RG16, false);
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
        
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer mr = renderers[i];
            mr.material.SetInt("_RemoveBackground", hideNonSkeletonPixels ? 1 : 0);
            mr.material.SetTexture("_ColorTex", _colorTexture);
            mr.material.SetTexture("_DepthTex", _depthTexture);
            mr.material.SetTexture("_BodyIndexTex", _bodyIndexTexture);
        }
    }

    private void OnApplicationQuit()
    {
        _running = false;
        if (_udpClient != null) _udpClient.Close();
        if (_networkThread != null && _networkThread.IsAlive) _networkThread.Join();
    }
}
