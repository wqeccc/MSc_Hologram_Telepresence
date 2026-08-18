using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using TMPro;

public class UI_ML2 : MonoBehaviour
{
    public TextMeshProUGUI statusText;
    private RemoteHologram _remoteHologram;
    private GazeAlignment _gazeAlignment;

    // controller
    private InputActionAsset inputActionsAsset;
    private InputAction triggerAction;

    void Start()
    {
        _remoteHologram = FindFirstObjectByType<RemoteHologram>();
        _gazeAlignment = FindFirstObjectByType<GazeAlignment>();

        InitController();
    }

    void InitController()
    {
        var manager = UnityEngine.Object.FindObjectOfType<InputActionManager>();
        if (manager == null)
            throw new NullReferenceException("Could not find an InputActionManager to initialize a MagicLeapController from");

        inputActionsAsset = manager.actionAssets[0];
        if (inputActionsAsset == null)
            throw new NullReferenceException("Could not find an InputActionAsset");

        inputActionsAsset.Enable();

        var inputMap = inputActionsAsset.FindActionMap("Controller");

        if (inputMap != null)
        {
            triggerAction = inputMap.FindAction("Trigger");
            triggerAction.performed += HandleTriggerToggle; // subscribe
        }
    }

    void HandleTriggerToggle(InputAction.CallbackContext context)
    {
        Debug.Log("Trigger Toggle");
        if (_remoteHologram != null)
        {
            _remoteHologram.enableGazeAlignment = !_remoteHologram.enableGazeAlignment;

            Debug.Log($"[ML2Layer] Trigger Pressed. Gaze Alignment enabled: {_remoteHologram.enableGazeAlignment}");
        }
    }

    void Update()
    {
        UpdateUI();
    }

    void UpdateUI()
    {
        if (statusText == null) return;

        string runtime = $"Runtime: {Time.time:F1}s";
        string height = "\n";

        if (_gazeAlignment != null && _remoteHologram != null && _remoteHologram.remoteSpeakerHologram != null)
        {
            string sa = $"local speaker height: {_remoteHologram.localSpeaker.position.y + _gazeAlignment.localFloorOffset}";
            string sb = $"remote speaker height: {_remoteHologram.remoteSpeaker.position.y + _gazeAlignment.remoteFloorOffset}";
            string pb = $"remote speaker hologram height: {_remoteHologram.remoteSpeakerHologram.position.y + _gazeAlignment.remoteFloorOffset}";
            string pa = $"local speaker at remote height: {_remoteHologram.localHologramAtRemote.position.y + _gazeAlignment.localFloorOffset}";

            height += $"{sa}\n" + $"{sb}\n" + $"{pb}\n" + $"{pa}\n";
        }

        if (_gazeAlignment != null && _remoteHologram != null && _remoteHologram.enableGazeAlignment)
        {
            statusText.text =
                $"{runtime}\n" +
                $"Gaze Alignment: <color=green>ON</color>\n" +
                $"Scenario: {_gazeAlignment.currentScenario}\n" +
                $"Scale: {_gazeAlignment.calculatedFinalScale:F2}\n" +
                $"{height}";
        }
        else
        {
            statusText.text =
                $"{runtime}\n" +
                "Gaze Alignment: <color=red>OFF</color>\n" +
                $"{height}";
        }
    }

    void OnDestroy()
    {
        if (triggerAction != null)
        {
            triggerAction.performed -= HandleTriggerToggle; // unsubscribe
        }

        if (inputActionsAsset != null) 
        {
            inputActionsAsset.Disable();
        }
    }
}
