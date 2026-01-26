using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class GolfCupTrigger : NetworkBehaviour
{
    [Header("Validation")]
    [SerializeField] private float maxEntrySpeed = 0.25f; // ball must be basically stopped
    [SerializeField] private Transform cupCenter;         // optional: snap/FX position

    private void Awake()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        var ball = other.GetComponentInParent<NetworkGolfBall>();
        if (!ball) return;

        // server-side stop check (uses your ball logic)
        if (!ball.IsStoppedServer())
            return;

        ulong ownerId = ball.LogicalOwnerClientId;

        Vector3 cupPos = cupCenter ? cupCenter.position : transform.position;

        var holeMgr = FindFirstObjectByType<GolfHoleManager>();
        if (holeMgr != null)
            holeMgr.OnBallHoledServer(ownerId, ball, cupPos);
    }

    private void OnTriggerStay(Collider other)
    {
        // Optional: lets fast balls settle then count as holed while inside
        if (!IsServer) return;

        var ball = other.GetComponentInParent<NetworkGolfBall>();
        if (!ball) return;

        if (!ball.IsStoppedServer())
            return;

        ulong ownerId = ball.LogicalOwnerClientId;
        Vector3 cupPos = cupCenter ? cupCenter.position : transform.position;

        var holeMgr = FindFirstObjectByType<GolfHoleManager>();
        if (holeMgr != null)
            holeMgr.OnBallHoledServer(ownerId, ball, cupPos);
    }
}
