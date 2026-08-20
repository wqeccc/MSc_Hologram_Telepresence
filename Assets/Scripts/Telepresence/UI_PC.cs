// using System;
// using UnityEngine;
// using TMPro;

// public class UI_PC : MonoBehaviour
// {
//     public TextMeshProUGUI statusText;
//     private RemoteHologram _remoteHologram;
//     private GazeAlignment _gazeAlignment;

//     void Start()
//     {
//         _remoteHologram = FindFirstObjectByType<RemoteHologram>();
//         _gazeAlignment = FindFirstObjectByType<GazeAlignment>();
//     }

//     private void Update()
//     {
//         // statusText.text = "Time: " + Time.time.ToString("F2");
//         UpdateUI();
//     }

//     void UpdateUI()
//     {
//         if (statusText == null) return;

//         string runtime = $"Runtime: {Time.time:F1}s";
//         // string clock = $"Time: {DateTime.Now:HH:mm:ss}";
//         string height = "\n";

//         if (_gazeAlignment != null && _remoteHologram != null && _remoteHologram.remoteSpeakerHologram != null)
//         {
//             string sa = $"local speaker height: {_remoteHologram.localSpeaker.position.y + _gazeAlignment.localFloorOffset}";
//             string sb = $"remote speaker height: {_remoteHologram.remoteSpeaker.position.y + _gazeAlignment.remoteFloorOffset}";
//             string pb = $"remote speaker hologram height: {_remoteHologram.remoteSpeakerHologram.position.y + _gazeAlignment.remoteFloorOffset}";
//             string pa = $"local speaker at remote height: {_remoteHologram.localHologramAtRemote.position.y + _gazeAlignment.localFloorOffset}";

//             height += $"{sa}\n" + $"{sb}\n" + $"{pb}\n" + $"{pa}\n";
//         }

//         if (_gazeAlignment != null && _remoteHologram != null && _remoteHologram.enableGazeAlignment)
//         {
//             statusText.text =
//                 $"{runtime}\n" +
//                 $"Gaze Alignment: <color=green>ON</color>\n" +
//                 $"Scenario: {_gazeAlignment.currentScenario}\n" +
//                 $"Scale: {_gazeAlignment.calculatedFinalScale:F2}\n" +
//                 $"{height}";
//         }
//         else
//         {
//             statusText.text =
//                 $"{runtime}\n" +
//                 "Gaze Alignment: <color=red>OFF</color>\n" +
//                 $"{height}";
//         }
//     }
// }
