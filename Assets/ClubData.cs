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

    
    [Header("Curve Intent (client sends curve01)")]
    [Tooltip("Hard cap on curve01 for this club. Putter=0, wedges small, driver bigger.")]
    [Range(0f, 1f)] public float curveMaxAbs = 0.35f;

    [Tooltip("How much this club responds to curve intent. (0=none, 1=full).")]
    [Range(0f, 1f)] public float curveEffectiveness = 0.85f;

    [Header("Launch Intent (client adds bias before clamping)")]
    [Tooltip("Max degrees the player is allowed to bias launch up/down for this club.")]
    [Range(0f, 30f)] public float launchBiasMaxAbsDeg = 10f;
}
