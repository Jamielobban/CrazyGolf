using UnityEngine;

[CreateAssetMenu(menuName = "Golf/Club Data")]
public class ClubData : ScriptableObject
{
    [Header("Identity")]
    public string clubName = "Iron";

    [Header("Swing Feel (client)")]
    public float yawSpeed = 4f;
    public float pitchSpeed = 4f;
    public float pitchMin = -80f;
    public float pitchMax = 80f;
    public float rotFollow = 14f;
    public bool invertY = true;

    [Header("Catch-up (client)")]
    public bool enableCatchup = true;
    public float catchupBoost = 3.5f;
    public float catchupTime = 0.09f;
    public float reversalThreshold = 40f;

    [Header("Anti-Parking (client)")]
    public bool enableAntiParking = true;
    public float yawClamp = 60f;
    public float yawDecay = 7f;
    public float pitchDecay = 2f;
    public AnimationCurve yawGainByPitch = AnimationCurve.EaseInOut(-80f, 0.5f, 80f, 1.4f);

    [Header("Grip / Setup (optional)")]
    public Vector3 gripLocalPosOffset;
    public Vector3 gripLocalEulerOffset;

    [Header("Shot (shared inputs)")]
    public float loftDeg = 25f;
    public float minLaunchDeg = 1f;
    public float maxLaunchDeg = 65f;

    [Header("Power mapping (client)")]
    public float speedForFullPower = 14f;
    public AnimationCurve powerCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Impulse mapping (server)")]
    public float minImpulse = 0.2f;
    public float maxImpulse = 20f;

    [Header("Curve (server)")]
    public float fpForFullCurve = 12f;
    public float curveAccel = 18f;
    public float minCurveFlatSpeed = 2.0f;
    public float curveMaxSeconds = 1.2f;
}
