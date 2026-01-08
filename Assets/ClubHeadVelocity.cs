using UnityEngine;
using Unity.Netcode;

public class ClubHeadVelocity : MonoBehaviour
{
    public Vector3 VelocityWorld { get; private set; }
    public float Speed => VelocityWorld.magnitude;

    private Vector3 prevPos;
    private NetworkBehaviour owner;   // we only need IsOwner

    public void BindOwner(NetworkBehaviour ownerBehaviour)
    {
        owner = ownerBehaviour;
        prevPos = transform.position;
    }

    private void FixedUpdate()
    {
        // If we know an owner, only compute on the owning client
        if (owner != null && !owner.IsOwner)
            return;

        float dt = Time.fixedDeltaTime;
        if (dt <= 0.0001f) return;

        Vector3 pos = transform.position;
        VelocityWorld = (pos - prevPos) / dt;
        prevPos = pos;
    }
}