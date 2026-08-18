using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GazeAlignment : MonoBehaviour
{
    // scenarios
    [System.Serializable]
    public enum ScalingCase
    {
        Case1_SimplestCase,            // local downscale, remote downscale -> done
        Case2_ViewingVectorCorrection, // local downscale, remote upscale -> eye-to-eye vector correction
        Case3_ForcedUpscale            // local can not downscale -> done
    }

    public float smoothSpeed = 5.0f;

    [Header("Algorithm Status")]
    public ScalingCase currentScenario;
    public float calculatedFinalScale = 1.0f;

    [Header("Floor Offsets")]
    public float localFloorOffset = 0.75f; // ml2 0.75m, kinect1 = 1.4m, kinect2 1.4m
    public float remoteFloorOffset = 1.4f;

    /**
    * assume a is local hologram space, b is remote hologram space
    * S_a: local speaker
    * S_b: remote speaker
    * P_b: remote speaker hologram at a (S_b->a)
    * P_a: local speaker hologram at b (S_a->b)
    * TODO: F_a, F_b (Capture Boundary)
    */
    public void ExecuteAlgorithm(Pose S_a, Pose S_b, Transform P_b, Pose P_a)
    {
        if (S_a == null || S_b == null || P_b == null || P_a == null) return;

        // Eb = Sr - Pr (gaze direction of remote speaker Sr looking at Pr)
        // Eh = Pl - Sl (desired gaze direction of local hologram Pl looking at Sl)
        // Vector3 Eb = S_b.position - P_a.position;
        // Vector3 Eh = P_b.position - S_a.position;

        Vector3 saPos = S_a.position;
        Vector3 sbPos = S_b.position;
        Vector3 pbPos = P_b.position;
        Vector3 paPos = P_a.position;

        float floorSaY = saPos.y + localFloorOffset;
        float floorSbY = sbPos.y + remoteFloorOffset;
        float floorPbY = pbPos.y + remoteFloorOffset;
        float floorPaY = paPos.y + localFloorOffset;

        // Viewing Vectors
        Vector3 Eb = new Vector3(
            sbPos.x - paPos.x,
            floorSbY - floorPaY,
            sbPos.z - paPos.z
        );
        Vector3 Eh = new Vector3(
            pbPos.x - saPos.x,
            floorPbY - floorSaY,
            pbPos.z - saPos.z
        );

        // only manipulating in XZ plane
        Vector3 Eb_xz = new Vector3(Eb.x, 0f, Eb.z);
        Vector3 Eh_xz = new Vector3(Eh.x, 0f, Eh.z);

        float t = 1.0f - Mathf.Exp(-smoothSpeed * Time.deltaTime);

        // avoid division by zero (1e-3f as safety threshold)
        if (Eb_xz.sqrMagnitude > 1e-3f && Eh_xz.sqrMagnitude > 1e-3f) 
        {
            // guarantee face-to-face conditions (Eb should align with -Eh)
            // the rotation difference between Eb and the inverse of Eh.
            Quaternion targetRotation = Quaternion.FromToRotation(Eb_xz.normalized, Eh_xz.normalized);
            // apply the rotation difference to the hologram (y axis)
            Quaternion finalRot = Quaternion.Euler(0f, targetRotation.eulerAngles.y, 0f);

            // lerp smoothing
            P_b.localRotation = Quaternion.Slerp(P_b.localRotation, finalRot, t);
        }
        
        // calculate vertical height differences
        float localDeltaY = Mathf.Abs(Eh.y);
        float remoteDeltaY = Mathf.Abs(Eb.y); 

        if (Mathf.Abs(remoteDeltaY) < 1e-3f) remoteDeltaY = 1e-3f;
        if (Mathf.Abs(localDeltaY) < 1e-3f) localDeltaY = 1e-3f;

        // scaling factors for height alignment
        float localScaleFactor = localDeltaY / remoteDeltaY;
        float remoteScaleFactor = remoteDeltaY / localDeltaY;
        float finalScale = 1.0f;

        // 1: eye-to-eye downscale
        if (localScaleFactor <= 1.0f) 
        {
            // 2: can my partner also downscale?
            if (remoteScaleFactor <= 1.0f)
            {
                // [Case 1: Yes] -> end the algorithm
                currentScenario = ScalingCase.Case1_SimplestCase;
                finalScale = localScaleFactor; 
            }
            else
            {
                // [Case 2: No] -> Correct local perspective using eye-to-eye vector ratio
                currentScenario = ScalingCase.Case2_ViewingVectorCorrection;
                // use the viewing vector-based correction to calculate a local scale that will not force the remote participant to upscale
                // use the reciprocal of the remote requirement to prevent forcing the partner to upscale
                finalScale = 1.0f / remoteScaleFactor; 
            }
        }
        else 
        {
            // 3: Can't downscale (Forced to upscale locally)
            // [Case 3] -> use current scale (1.0f), let the remote side downscale instead
            currentScenario = ScalingCase.Case3_ForcedUpscale;
            finalScale = 1.0f;
        }

        calculatedFinalScale = finalScale;

        // apply scaling smoothly while maintaining realistic body proportions
        Vector3 scaleVec = new Vector3(finalScale, finalScale, finalScale);
        P_b.localScale = Vector3.Lerp(P_b.localScale, scaleVec, t);
    }
}
