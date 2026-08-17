using System;
using UnityEngine;
using TMPro;

public class UI_PC : MonoBehaviour
{
    public TextMeshProUGUI statusText;
    private RemoteHologram _remoteHologram;
    private GazeAlignment _gazeAlignment;

    void Start()
    {
        _remoteHologram = FindFirstObjectByType<RemoteHologram>();
        _gazeAlignment = FindFirstObjectByType<GazeAlignment>();
    }

    private void Update()
    {
        // statusText.text = "Time: " + Time.time.ToString("F2");
        UpdateUI();
    }

    void UpdateUI()
    {
        if (statusText == null) return;

        string runtime = $"Runtime: {Time.time:F1}s";
        // string clock = $"Time: {DateTime.Now:HH:mm:ss}";

        if (_gazeAlignment != null && _remoteHologram != null && _remoteHologram.enableGazeAlignment)
        {
            statusText.text =
                $"{runtime}\n" +
                $"Gaze Alignment: <color=green>ON</color>\n" +
                $"Scenario: {_gazeAlignment.currentScenario}\n" +
                $"Scale: {_gazeAlignment.calculatedFinalScale:F2}";
        }
        else
        {
            statusText.text =
                $"{runtime}\n" +
                "Gaze Alignment: <color=red>OFF</color>";
        }
    }
}
