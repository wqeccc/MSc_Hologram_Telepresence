using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class MetaDataNetworkReceiver : MonoBehaviour
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

    // --- UDP 專用變數 ---
    UdpClient _udpClient;
    Thread _networkThread;
    bool _running;
    
    // 臨時組裝倉庫
    private Dictionary<int, byte[]> _framePacketsBuffer = new Dictionary<int, byte[]>();
    private int _currentAssemblingFrame = -1;
    private int _colorByteSize;
    private int _depthByteSize;
    private int _playerIndexByteSize;
    private bool _configReceived = false;

    // --- 【新增：接收遠端動態座標的變數與安全鎖】 ---
    private Vector3 _remoteSpeakerPos;
    private Quaternion _remoteSpeakerRot;
    private Vector3 _remotePointCloudPos;
    private Quaternion _remotePointCloudRot;
    private object _metaLock = new object();

    public bool remesh = false;
    public bool hideNonSkeletonPixels = true;
    public int LocalListeningPort = 12345; // 監聽本機的這個 Port，PC 端要射到這個 Port

    // 提供公開屬性或函式，方便其他腳本（如 RemoteHologram）讀取遠端使用者的位置
    public Vector3 RemoteSpeakerPos { get { lock(_metaLock) return _remoteSpeakerPos; } }
    public Quaternion RemoteSpeakerRot { get { lock(_metaLock) return _remoteSpeakerRot; } }

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

        // 啟動背景網路執行緒
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

    // 【核心修改】解開大蛋糕：前 56 Byte 是元數據座標，後面才是點雲 （// 把拼好的總影格大陣列，重新依長度切開，塞入原本的渲染雙佇列系統中）
    private void DistributeFrameData(byte[] fullFrameData)
    {
        // 預期大小現在必須包含 56 密碼位元組 （// 安全檢查，確保拼出來的總長度符合預期）
        int expectedPointCloudSize = _colorByteSize + _depthByteSize + _playerIndexByteSize;
        int totalExpectedSize = 56 + expectedPointCloudSize;
        if (fullFrameData.Length < totalExpectedSize) return;

        // 1. 先解鎖並提取最前方的 56 位元組動態空間座標
        lock (_metaLock)
        {
            _remoteSpeakerPos.x = BitConverter.ToSingle(fullFrameData, 0);
            _remoteSpeakerPos.y = BitConverter.ToSingle(fullFrameData, 4);
            _remoteSpeakerPos.z = BitConverter.ToSingle(fullFrameData, 8);

            _remoteSpeakerRot.x = BitConverter.ToSingle(fullFrameData, 12);
            _remoteSpeakerRot.y = BitConverter.ToSingle(fullFrameData, 16);
            _remoteSpeakerRot.z = BitConverter.ToSingle(fullFrameData, 20);
            _remoteSpeakerRot.w = BitConverter.ToSingle(fullFrameData, 24);

            _remotePointCloudPos.x = BitConverter.ToSingle(fullFrameData, 28);
            _remotePointCloudPos.y = BitConverter.ToSingle(fullFrameData, 32);
            _remotePointCloudPos.z = BitConverter.ToSingle(fullFrameData, 36);

            _remotePointCloudRot.x = BitConverter.ToSingle(fullFrameData, 40);
            _remotePointCloudRot.y = BitConverter.ToSingle(fullFrameData, 44);
            _remotePointCloudRot.z = BitConverter.ToSingle(fullFrameData, 48);
            _remotePointCloudRot.w = BitConverter.ToSingle(fullFrameData, 52);
        }

        // 2. 處理剩下的點雲影像
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

        // 起始偏移量跳過 56 （// 依序從小切口複製出各自的資料段）
        int srcOffset = 56;
        Buffer.BlockCopy(fullFrameData, srcOffset, colorBuffer, 0, _colorByteSize); srcOffset += _colorByteSize;
        Buffer.BlockCopy(fullFrameData, srcOffset, depthBuffer, 0, _depthByteSize); srcOffset += _depthByteSize;
        Buffer.BlockCopy(fullFrameData, srcOffset, bodyIndexBuffer, 0, _playerIndexByteSize);

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

    // ======= 以下程式碼完全保留原本 TCP 版本的 GPU/Mesh 渲染邏輯，原封不動 =======
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

    void initializeMeshData() { /* 保留你原本的 initializeMeshData, createSubmesh, createStitchingMesh 程式碼 */ }

    void PostKinectInit()
    {
        _depthTexture = new Texture2D(_depthWidth, _depthHeight, TextureFormat.RG16, false);
        _colorTexture = new Texture2D(_depthWidth, _depthHeight, TextureFormat.BGRA32, false);
        _bodyIndexTexture = new Texture2D(_depthWidth, _depthHeight, TextureFormat.Alpha8, false);
        _depthTexture.filterMode = FilterMode.Point;
        _colorTexture.filterMode = FilterMode.Point;
        
        if (remesh) initializeMeshData();
        else initializePointCloudData();

        _renderMaterial.SetFloatArray("camera_calibration", _calibrationTable);
        _renderMaterial.SetFloat("camera_width", _depthWidth);
        _renderMaterial.SetFloat("camera_height", _depthHeight);
        _texturesInitialized = true;
    }

    void Update()
    {
        if (!_networkInitialized) return;
        if (_networkInitialized && !_texturesInitialized) PostKinectInit();

        // 🌟 移除：不再強制覆蓋更新 transform 的 position 和 rotation，允許在 Unity 中自由擺放 Receiver 物件
        // --- 【新增：在主執行緒將遠端點雲同步套用到實體世界中】 ---
        Vector3 targetPCloudPos;
        Quaternion targetPCloudRot;
        lock (_metaLock)
        {
            targetPCloudPos = _remotePointCloudPos;
            targetPCloudRot = _remotePointCloudRot;
        }
        // 讓掛載此 Receiver 的點雲物件，依照遠端調整的 position/rotation 進行即時位移
        this.transform.position = targetPCloudPos;
        this.transform.rotation = targetPCloudRot;


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
