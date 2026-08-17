using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Profiler : MonoBehaviour
{
    [Header("Logging Settings")]
    public bool isLogging = false;
    public float logInterval = 0.5f; // record data every 0.5 seconds
    public string customFileName = "MSc_Evaluation_Log";

    // [Header("Dynamic Metrics")]
    private int currentPointCloudVertices = 0;
    private float currentNetworkLatencyMs = 0f;  // ms
    private float currentBandwidthMbps = 0f;     // Mbps
    private float currentPacketSizeBytes = 0f;   // Bytes

    private float deltaTimeAccumulator = 0.0f;
    private int frameCountAccumulator = 0;
    private float timer = 0.0f;

    private string filePath;
    private StringBuilder csvContent = new StringBuilder();

    void Start()
    {
        if (!isLogging) return;

        // C:\Users\<YourUsername>\AppData\LocalLow\<CompanyName>\<ProductName>\
        // Android/data/<Your.Package.Name>/files/
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        filePath = Path.Combine(Application.persistentDataPath, $"{customFileName}_{timestamp}.csv");

        // header
        csvContent.AppendLine("Timestamp,FPS,FrameTime_ms,VerticesCount,NetworkLatency_ms,PacketSize_KB,Bandwidth_Mbps");
        File.WriteAllText(filePath, csvContent.ToString());
        
        Debug.Log($"[Profiler] CSV Log created at: {filePath}");
    }

    void Update()
    {
        if (!isLogging) return;

        // compute Frame Time and FPS
        deltaTimeAccumulator += Time.unscaledDeltaTime;
        frameCountAccumulator++;
        timer += Time.unscaledDeltaTime;

        if (timer >= logInterval)
        {
            float averageFPS = frameCountAccumulator / deltaTimeAccumulator;
            float averageFrameTimeMs = (deltaTimeAccumulator / frameCountAccumulator) * 1000f;

            float packetSizeKB = currentPacketSizeBytes / 1024f;
            // // calculate approximate bandwidth Mbps = (Packet Size in bits * FPS) / 1,000,000
            // float bandwidthMbps = (currentPacketSizeBytes * 8f * averageFPS) / 1000000f;

            // format data
            string logLine = $"{Time.timeSinceLevelLoad:F2}," +
                             $"{averageFPS:F2}," +
                             $"{averageFrameTimeMs:F2}," +
                             $"{currentPointCloudVertices}," +
                             $"{currentNetworkLatencyMs:F2}," +
                             $"{packetSizeKB:F2}," +
                             $"{currentBandwidthMbps:F2}";

            using (StreamWriter writer = new StreamWriter(filePath, true))
            {
                writer.WriteLine(logLine);
            }

            // reset
            timer = 0.0f;
            deltaTimeAccumulator = 0.0f;
            frameCountAccumulator = 0;
        }
    }

    // Streaming data
    public void UpdateStreamingMetrics(int vertexCount, float latencyMs, float bandwidthMbps, float packetSizeBytes)
    {
        currentPointCloudVertices = vertexCount;
        currentNetworkLatencyMs = latencyMs;
        currentBandwidthMbps = bandwidthMbps;
        currentPacketSizeBytes = packetSizeBytes;
    }

    // TODO
    // unity profiler gpu & cpu frametime
    // gaze alignment case data (recording)
}
