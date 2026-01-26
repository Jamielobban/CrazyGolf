using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GolfHoleManager : NetworkBehaviour
{
    private readonly Dictionary<ulong, int> strokesByClient = new();

    [System.Serializable]
    public struct StrokeDebugRow
    {
        public ulong clientId;
        public int strokes;
    }

    [Header("DEBUG (Inspector Only)")]
    [SerializeField] private List<StrokeDebugRow> debugStrokes = new();

    // Server: call this when a validated hit occurs (your RequestHitBallByIdServerRpc)
    public void AddStrokeServer(ulong clientId)
    {
        if (!IsServer) return;

        int strokes = 0;
        strokesByClient.TryGetValue(clientId, out strokes);
        strokes++;
        strokesByClient[clientId] = strokes;

        Debug.Log($"[GOLF] Client {clientId} stroke {strokes}");

        SyncDebug();

        // Notify everyone for UI/SFX etc.
        StrokeCountChangedClientRpc(clientId, strokes);
    }

    // Server: called by cup trigger
    public void OnBallHoledServer(ulong clientId, NetworkGolfBall ball, Vector3 cupPos)
    {
        if (!IsServer) return;

        int strokes = strokesByClient.TryGetValue(clientId, out var s) ? s : 0;
        Debug.Log($"[GOLF] Client {clientId} HOLED OUT in {strokes} strokes");

        // Freeze ball in cup (server truth)
        var rb = ball.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // Notify everyone for VFX/SFX/UI
        BallHoledClientRpc(clientId, strokes, cupPos);

        // TODO later:
        // - reset ball
        // - advance to next hole
    }

    // -------------------------
    // Client RPCs: run on all clients, raise local signals
    // -------------------------

    [ClientRpc]
    private void StrokeCountChangedClientRpc(ulong clientId, int strokes)
    {
        GameSignals.RaiseStrokeCountChanged(clientId, strokes);
    }

    [ClientRpc]
    private void BallHoledClientRpc(ulong clientId, int strokes, Vector3 cupPos)
    {
        GameSignals.RaiseBallHoled(clientId, strokes, cupPos);
    }

    private void SyncDebug()
    {
        debugStrokes.Clear();
        foreach (var kv in strokesByClient)
        {
            debugStrokes.Add(new StrokeDebugRow
            {
                clientId = kv.Key,
                strokes = kv.Value
            });
        }
    }
}
