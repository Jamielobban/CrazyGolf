using UnityEngine;
using Unity.Netcode;

public class ClubHeadVelocity : NetworkBehaviour
{
    public Vector3 VelocityWorld { get; private set; }
    public float Speed => VelocityWorld.magnitude;

    Vector3 prevPos;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) { enabled = false; return; }
        prevPos = transform.position;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 pos = transform.position;
        VelocityWorld = (pos - prevPos) / dt;
        prevPos = pos;
    }
}