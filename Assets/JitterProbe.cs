using UnityEngine;
using Unity.Netcode;

public class JitterProbe : NetworkBehaviour
{
    public Transform rawAnchor; // BodyAnchorPivot
    public Transform grip;      // GripPivot (detached)

    public float logEverySeconds = 0.25f;

    Vector3 rawPrev, gripPrev;
    float tAccum;
    float rawStepSum, gripStepSum;
    float rawStepMax, gripStepMax;
    int rawZeroishCount;
    int frames;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) { enabled = false; return; }
        if (rawAnchor) rawPrev = rawAnchor.position;
        if (grip) gripPrev = grip.position;
    }

    void LateUpdate()
    {
        if (!rawAnchor || !grip) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 rawNow = rawAnchor.position;
        Vector3 gripNow = grip.position;

        float rawStep = (rawNow - rawPrev).magnitude;
        float gripStep = (gripNow - gripPrev).magnitude;

        rawStepSum += rawStep;
        gripStepSum += gripStep;
        rawStepMax = Mathf.Max(rawStepMax, rawStep);
        gripStepMax = Mathf.Max(gripStepMax, gripStep);

        if (rawStep < 0.0002f) rawZeroishCount++;

        rawPrev = rawNow;
        gripPrev = gripNow;

        frames++;
        tAccum += dt;

        if (tAccum >= logEverySeconds)
        {
            float rawAvg = rawStepSum / Mathf.Max(1, frames);
            float gripAvg = gripStepSum / Mathf.Max(1, frames);

            Debug.Log(
                $"[JitterProbe] frames={frames} " +
                $"RAW avgStep={rawAvg:F5} maxStep={rawStepMax:F5} zeroish={rawZeroishCount} | " +
                $"GRIP avgStep={gripAvg:F5} maxStep={gripStepMax:F5}"
            );

            tAccum = 0f;
            frames = 0;
            rawStepSum = gripStepSum = 0f;
            rawStepMax = gripStepMax = 0f;
            rawZeroishCount = 0;
        }
    }
}
