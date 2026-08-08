using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using TMPro;

public class New_ML2Layer : MonoBehaviour // TODO add unity tmPro
{
    public TextMeshPro statusText;
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
        UpdateUI();
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
        if (_remoteHologram != null)
        {
            _remoteHologram.enableGazeAlignment = !_remoteHologram.enableGazeAlignment;
            
            UpdateUI();
            Debug.Log($"[ML2Layer] Trigger Pressed. Gaze Alignment enabled: {_remoteHologram.enableGazeAlignment}");
        }
    }

    private void Update()
    {
        if (_gazeAlignment != null && _remoteHologram != null && _remoteHologram.enableGazeAlignment)
        {
            UpdateUI();
        }
    }

    void UpdateUI()
    {
        if (statusText == null || _remoteHologram == null) return;

        if (_remoteHologram.enableGazeAlignment)
        {
            statusText.text = $"Gaze Alignment: <color=green>ON</color>\n" +
                              $"Scenario: {_gazeAlignment.currentScenario}\n" +
                              $"Scale: {_gazeAlignment.calculatedFinalScale:F2}";
        }
        else
        {
            statusText.text = "Gaze Alignment: <color=red>OFF</color>";
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
