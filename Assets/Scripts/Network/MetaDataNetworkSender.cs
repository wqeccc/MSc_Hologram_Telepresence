using System;
// using System.Collections;
// using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class MetaDataNetworkSender : MonoBehaviour
{
    // 需要關聯的 KinectController 元件
    public KinectController kinectController;

    [Header("Network Settings")]
    public string TargetIP = "192.168.1.100"; // 接收端（例如 Magic Leap 2 或 另一台 PC）的 IP 位址
    public int TargetPort = 12345;            // 必須跟接收端的 LocalListeningPort 一致

    private UdpClient _udpClient;
    private Thread _sendThread;
    private bool _running;

    private int _frameCounter = 0;
    private bool _hasSentConfig = false;
    private const int MAX_UDP_PACKET_SIZE = 60000; // 安全範圍內的單包最大長度 (小於 64KB)

    // --- 【新增：動態座標共享倉庫】 ---
    private Vector3 _latestSpeakerPos;
    private Quaternion _latestSpeakerRot;
    private Vector3 _latestPointCloudPos;
    private Quaternion _latestPointCloudRot;
    private object _metaLock = new object(); // 確保多執行緒讀寫安全

    void Start()
    {
        if (kinectController == null)
        {
            kinectController = GetComponent<KinectController>();
        }

        _udpClient = new UdpClient();
        _running = true;

        // 啟動一個專門負責從緩衝區抓資料並發送的背景執行緒
        // 【保留】啟動專職發送的背景執行緒
        _sendThread = new Thread(SendLoop);
        _sendThread.IsBackground = true;
        _sendThread.Start();
    }

    // 讓 LocalHologram 來呼叫這個公開函式，它只負責更新「座標紙條」，完全不耗時！
    public void UpdateLatestMetadata(Vector3 speakerPos, Quaternion speakerRot, Vector3 pointCloudPos, Quaternion pointCloudRot)
    {
        lock (_metaLock)
        {
            _latestSpeakerPos = speakerPos;
            _latestSpeakerRot = speakerRot;
            _latestPointCloudPos = pointCloudPos;
            _latestPointCloudRot = pointCloudRot;
        }
    }

    private void SendLoop()
    {
        while (_running)
        {
            // 1. 等待 Kinect 初始完成
            if (kinectController == null || !kinectController.kinectInitialized)
            {
                Thread.Sleep(100); // 避免空迴圈空耗 CPU
                continue;
            }

            // 2. 第一時間發送初始化配置資訊 (只送一次， packetID = -1)
            if (!_hasSentConfig)
            {
                SendKinectConfig();
                _hasSentConfig = true;
                continue;
            }

            // 3. 抓取點雲影格數據
            byte[] localColor = null;
            byte[] localDepth = null;
            byte[] localBodyIndex = null;

            // 鎖定 KinectController 的緩衝區，安全地複製出來
            lock (kinectController.m_bufferLock)
            {
                if (kinectController.m_colorImage != null && kinectController.m_colorImage.Length > 0)
                {
                    // 抓取點雲
                    localColor = (byte[])kinectController.m_colorImage.Clone();
                    localDepth = (byte[])kinectController.m_depthImage.Clone();
                    localBodyIndex = (byte[])kinectController.m_bodyIndexMap.Clone();
                }
            }

            if (localColor != null && localDepth != null && localBodyIndex != null)
            {
                // 1. 拼接點雲 （// 將三種資料拼裝成一整個大陣列影格）
                byte[] pointCloudData = CombineFrameData(localColor, localDepth, localBodyIndex);

                // 2. 抓取 LocalHologram 留在倉庫裡的最新座標
                Vector3 sPos; Quaternion sRot; Vector3 pPos; Quaternion pRot;
                lock (_metaLock)
                {
                    sPos = _latestSpeakerPos;
                    sRot = _latestSpeakerRot;
                    pPos = _latestPointCloudPos;
                    pRot = _latestPointCloudRot;
                }

                // 3. 把點雲跟動態座標元數據合併 （// 4. 【關鍵改進】把點雲跟你的「動態座標元數據」合併！）
                byte[] fullFrameData = AppendMetadata(pointCloudData, sPos, sRot, pPos, pRot);

                // 4. 在背景執行緒切片發射，完全不卡主畫面 （// 開始切片並透過 UDP 射出去）
                SendLargeFrame(fullFrameData);
            }

            // 控制發送頻率。Kinect 是 30 FPS，所以大約每 33 毫秒發送一影格即可
            Thread.Sleep(33); // 穩定的 30 FPS 傳輸
        }
    }

    // 發送初始化設定資訊 (與接收端的 ParseConfiguration 完全對齊)
    private void SendKinectConfig()
    {
        Debug.Log("正在發送 Kinect 初始化設定資訊到: " + TargetIP);

        int height = kinectController.depthHeight;
        int width = kinectController.depthWidth;
        float[] calibTable = kinectController.calibrationTable;
        int calibSize = calibTable.Length;

        // 計算所需總長度：12 Byte Header + 4 Byte Height + 4 Byte Width + 4 Byte Size + (calibSize * 4) Byte Table
        int totalSize = 12 + 4 + 4 + 4 + (calibSize * 4);
        byte[] configPacket = new byte[totalSize];

        // 寫入 12 位元組自訂 Header：[ 幀號: 0, 序號(packetID): -1 (設定暗號), 總片數: 1 ]
        Buffer.BlockCopy(BitConverter.GetBytes(0), 0, configPacket, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(-1), 0, configPacket, 4, 4); 
        Buffer.BlockCopy(BitConverter.GetBytes(1), 0, configPacket, 8, 4);

        // 寫入實體配置參數
        int offset = 12;
        Buffer.BlockCopy(BitConverter.GetBytes(height), 0, configPacket, offset, 4); offset += 4;
        Buffer.BlockCopy(BitConverter.GetBytes(width), 0, configPacket, offset, 4); offset += 4;
        Buffer.BlockCopy(BitConverter.GetBytes(calibSize), 0, configPacket, offset, 4); offset += 4;

        // 寫入 Float 內參矩陣
        for (int i = 0; i < calibSize; i++)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(calibTable[i]), 0, configPacket, offset, 4);
            offset += 4;
        }

        // 射出設定檔封包
        _udpClient.Send(configPacket, configPacket.Length, TargetIP, TargetPort);
    }

    // 將 Color、Depth、BodyIndex 拼接成超大Byte陣列
    private byte[] CombineFrameData(byte[] color, byte[] depth, byte[] bodyIndex)
    {
        byte[] combined = new byte[color.Length + depth.Length + bodyIndex.Length];
        
        int offset = 0;
        Buffer.BlockCopy(color, 0, combined, offset, color.Length); offset += color.Length;
        Buffer.BlockCopy(depth, 0, combined, offset, depth.Length); offset += depth.Length;
        Buffer.BlockCopy(bodyIndex, 0, combined, offset, bodyIndex.Length);

        return combined;
    }

    // 將 4 個 3D 空間參數（48 位元組）墊在大矩陣的最前端或最後端，方便接收端拆解
    private byte[] AppendMetadata(byte[] cloudData, Vector3 sPos, Quaternion sRot, Vector3 pPos, Quaternion pRot)
    {
        // 3個float是Vector3(12byte), 4個float是Quaternion(16byte) -> 總共 12+16+12+16 = 56 Byte
        byte[] metaBytes = new byte[56];

        Buffer.BlockCopy(BitConverter.GetBytes(sPos.x), 0, metaBytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(sPos.y), 0, metaBytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(sPos.z), 0, metaBytes, 8, 4);
        
        Buffer.BlockCopy(BitConverter.GetBytes(sRot.x), 0, metaBytes, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(sRot.y), 0, metaBytes, 16, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(sRot.z), 0, metaBytes, 20, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(sRot.w), 0, metaBytes, 24, 4);

        Buffer.BlockCopy(BitConverter.GetBytes(pPos.x), 0, metaBytes, 28, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(pPos.y), 0, metaBytes, 32, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(pPos.z), 0, metaBytes, 36, 4);
        
        Buffer.BlockCopy(BitConverter.GetBytes(pRot.x), 0, metaBytes, 40, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(pRot.y), 0, metaBytes, 44, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(pRot.z), 0, metaBytes, 48, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(pRot.w), 0, metaBytes, 52, 4);

        // 把 56 Byte 元數據拼在點雲大資料最前面
        byte[] finalPayload = new byte[metaBytes.Length + cloudData.Length];
        Buffer.BlockCopy(metaBytes, 0, finalPayload, 0, metaBytes.Length);
        Buffer.BlockCopy(cloudData, 0, finalPayload, metaBytes.Length, cloudData.Length);

        return finalPayload;
    }

    // 負責將大蛋糕切成 60KB 以內的小切片，並加上 Header 發射
    private void SendLargeFrame(byte[] fullFrameData)
    {
        _frameCounter++;
        int totalBytes = fullFrameData.Length;
        // 算出這影格總共需要切成幾片
        int totalPackets = Mathf.CeilToInt((float)totalBytes / MAX_UDP_PACKET_SIZE);

        for (int i = 0; i < totalPackets; i++)
        {
            int currentOffset = i * MAX_UDP_PACKET_SIZE;
            int sizeToSend = Mathf.Min(MAX_UDP_PACKET_SIZE, totalBytes - currentOffset);

            // 建立切片包：12 Byte Header + 數據荷載 (Payload)
            byte[] packet = new byte[12 + sizeToSend];
            // 填寫 12 位元組自訂 Header 暗號
            Buffer.BlockCopy(BitConverter.GetBytes(_frameCounter), 0, packet, 0, 4);  // 0~3 Byte: 幀號 (FrameID)
            Buffer.BlockCopy(BitConverter.GetBytes(i), 0, packet, 4, 4);  // 4~7 Byte: 切片序號 (PacketID)           
            Buffer.BlockCopy(BitConverter.GetBytes(totalPackets), 0, packet, 8, 4);   // 8~11 Byte: 總切片數 (TotalPackets)

            // 填寫實體數據
            Buffer.BlockCopy(fullFrameData, currentOffset, packet, 12, sizeToSend);

            // 通過 UDP 發射
            try
            {
                _udpClient.Send(packet, packet.Length, TargetIP, TargetPort); 
            }
            catch (Exception e)
            {
                Debug.LogWarning("UDP 發送切片失敗: " + e.Message);
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
