using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEngine;

public class Profiler : MonoBehaviour
{
    [Header("Logging Settings")]
    public bool isLogging = false;
    public float systemLogInterval = 0.5f; // record data every 0.5 seconds
    public string customFileNamePrefix = "MSc_Evaluation";

    // Current Runtime Metrics
    private int currentPointCloudVertices = 0;
    private float currentNetworkLatencyMs = 0f;
    private float currentBandwidthMbps = 0f;
    private float currentPacketSizeBytes = 0f;

    private float deltaTimeAccumulator = 0.0f;
    private int frameCountAccumulator = 0;
    private float systemTimer = 0.0f;

    // GPU / CPU Frame Timing
    private FrameTiming[] _frameTimings = new FrameTiming[1];
    private float cpuTimeAccumulator = 0.0f;
    private float gpuTimeAccumulator = 0.0f;

    private string systemFilePath;
    private string networkFilePath;
    private string gazeFilePath;

    private ConcurrentQueue<(string filePath, string line)> logQueue = new ConcurrentQueue<(string, string)>();
    private bool isWritingLoopRunning = false;

    void Start()
    {
        if (!isLogging) return;

        // C:\Users\<YourUsername>\AppData\LocalLow\<CompanyName>\<ProductName>\
        // Android/data/<Your.Package.Name>/files/
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string folderPath = Application.persistentDataPath;

        systemFilePath = Path.Combine(folderPath, $"{customFileNamePrefix}_System_{timestamp}.csv");
        networkFilePath = Path.Combine(folderPath, $"{customFileNamePrefix}_Network_{timestamp}.csv");
        gazeFilePath = Path.Combine(folderPath, $"{customFileNamePrefix}_GazeAlignment_{timestamp}.csv");

        // headers
        string systemHeader = "Timestamp_s,FPS,FrameTime_ms,CPU_Time_ms,GPU_Time_ms,ActiveVerticesCount\n";
        string networkHeader = "Timestamp_s,Latency_ms,Bandwidth_Mbps,PacketSize_KB,ActiveVerticesCount\n";
        string gazeHeader = "Timestamp_s,CurrentCase,FinalScale,LocalDeltaY,RemoteDeltaY\n";
        File.WriteAllText(systemFilePath, systemHeader);
        File.WriteAllText(networkFilePath, networkHeader);
        File.WriteAllText(gazeFilePath, gazeHeader);

        Debug.Log($"[Profiler] System Log created at: {systemFilePath}");
        Debug.Log($"[Profiler] Network Log created at: {networkFilePath}");

        isWritingLoopRunning = true;
        Task.Run(ProcessLogQueueAsync);
    }

    void Update()
    {
        if (!isLogging) return;

        // CPU / GPU times
        FrameTimingManager.CaptureFrameTimings();
        uint numTimings = FrameTimingManager.GetLatestTimings(1, _frameTimings);

        if (numTimings > 0)
        {
            cpuTimeAccumulator += (float)_frameTimings[0].cpuFrameTime * 1000f;
            gpuTimeAccumulator += (float)_frameTimings[0].gpuFrameTime * 1000f;
        }

        // Frame Time & FPS
        deltaTimeAccumulator += Time.unscaledDeltaTime;
        frameCountAccumulator++;
        systemTimer += Time.unscaledDeltaTime;

        // System Evaluation
        if (systemTimer >= systemLogInterval)
        {
            float averageFPS = frameCountAccumulator / deltaTimeAccumulator;
            float averageFrameTimeMs = (deltaTimeAccumulator / frameCountAccumulator) * 1000f;
            
            float averageCpuMs = frameCountAccumulator > 0 ? (cpuTimeAccumulator / frameCountAccumulator) : 0f;
            float averageGpuMs = frameCountAccumulator > 0 ? (gpuTimeAccumulator / frameCountAccumulator) : 0f;

            float currentTime = Time.timeSinceLevelLoad;

            string systemLogLine = $"{currentTime:F3}," +
                                  $"{averageFPS:F2}," +
                                  $"{averageFrameTimeMs:F2}," +
                                  $"{averageCpuMs:F2}," +
                                  $"{averageGpuMs:F2}," +
                                  $"{currentPointCloudVertices}";

            logQueue.Enqueue((systemFilePath, systemLogLine));

            // reset accumulators
            systemTimer = 0.0f;
            deltaTimeAccumulator = 0.0f;
            frameCountAccumulator = 0;
            cpuTimeAccumulator = 0.0f;
            gpuTimeAccumulator = 0.0f;
        }
    }

    // Network Evaluation
    public void UpdateStreamingMetrics(int vertexCount, float latencyMs, float bandwidthMbps, float packetSizeBytes)
    {
        currentPointCloudVertices = vertexCount;
        currentNetworkLatencyMs = latencyMs;
        currentBandwidthMbps = bandwidthMbps;
        currentPacketSizeBytes = packetSizeBytes;

        if (!isLogging) return;

        float currentTime = Time.timeSinceLevelLoad;
        float packetSizeKB = packetSizeBytes / 1024f;

        string networkLogLine = $"{currentTime:F3}," +
                               $"{latencyMs:F2}," +
                               $"{bandwidthMbps:F2}," +
                               $"{packetSizeKB:F2}," +
                               $"{vertexCount}";

        logQueue.Enqueue((networkFilePath, networkLogLine));
    }

    // gaze algorithm validation
    public void RecordGazeMetrics(int scenarioCase, float finalScale, float localDeltaY, float remoteDeltaY)
    {
        float currentTime = Time.timeSinceLevelLoad;
        string gazeLogLine = $"{currentTime:F3}," +
                            $"{scenarioCase}," +
                            $"{finalScale:F3}," +
                            $"{localDeltaY:F3}," +
                            $"{remoteDeltaY:F3}";

        logQueue.Enqueue((gazeFilePath, gazeLogLine));
    }

    // write data loop
    private async Task ProcessLogQueueAsync()
    {
        while (isWritingLoopRunning || !logQueue.IsEmpty)
        {
            if (!logQueue.IsEmpty)
            {
                var batchLines = new ConcurrentDictionary<string, System.Text.StringBuilder>();

                while (logQueue.TryDequeue(out var logEntry))
                {
                    if (!batchLines.ContainsKey(logEntry.filePath))
                    {
                        batchLines[logEntry.filePath] = new System.Text.StringBuilder();
                    }
                    batchLines[logEntry.filePath].AppendLine(logEntry.line);
                }

                foreach (var kvp in batchLines)
                {
                    try
                    {
                        using (StreamWriter writer = new StreamWriter(kvp.Key, true))
                        {
                            await writer.WriteAsync(kvp.Value.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Profiler] File write error: {ex.Message}");
                    }
                }
            }

            // wirte every 100ms
            await Task.Delay(100);
        }
    }

    private void OnDestroy()
    {
        isWritingLoopRunning = false;
    }
}
