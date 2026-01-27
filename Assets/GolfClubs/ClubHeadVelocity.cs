// ClubHeadVelocity.cs
using UnityEngine;
using Unity.Netcode;

public class ClubHeadVelocity : MonoBehaviour
{
    public Vector3 VelocityWorld { get; private set; }      // smoothed
    public float Speed => VelocityWorld.magnitude;

    public Vector3 RawVelocityWorld { get; private set; }   // unsmoothed
    public float RawSpeed => RawVelocityWorld.magnitude;

    [Header("Smoothing")]
    [Tooltip("0 = no smoothing. 0.5 is a good start.")]
    [Range(0f, 0.95f)]
    [SerializeField] private float smoothing = 0.5f;

    private Vector3 prevPos;
    private NetworkBehaviour owner; // injected; only used for IsOwner gating
    private bool hasOwner;

    public void BindOwner(NetworkBehaviour ownerBehaviour)
    {
        owner = ownerBehaviour;
        hasOwner = owner != null;
        prevPos = transform.position;
        VelocityWorld = Vector3.zero;
        RawVelocityWorld = Vector3.zero;
    }

    private void OnEnable()
    {
        prevPos = transform.position;
        VelocityWorld = Vector3.zero;
        RawVelocityWorld = Vector3.zero;
    }

    private void FixedUpdate()
    {
        // If we have an owner reference, only compute on that owner's client
        if (hasOwner && !owner.IsOwner)
            return;

        float dt = Time.fixedDeltaTime;
        if (dt <= 0.0001f) return;

        Vector3 pos = transform.position;
        Vector3 raw = (pos - prevPos) / dt;
        prevPos = pos;

        RawVelocityWorld = raw;

        // Exponential smoothing
        if (smoothing > 0f)
            VelocityWorld = Vector3.Lerp(VelocityWorld, raw, 1f - smoothing);
        else
            VelocityWorld = raw;
    }
}
