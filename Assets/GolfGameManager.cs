using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GolfGameManager : MonoBehaviour
{
    [SerializeField] private NetworkObject golfBallPrefab;

    [Header("Spawn Points (optional)")]
    [SerializeField] private Transform[] teeSpawns;

    private readonly Dictionary<ulong, NetworkObject> ballByClient = new();

    private void OnEnable()
    {
        StartCoroutine(HookWhenReady());
    }

    private IEnumerator HookWhenReady()
    {
        // Wait until NetworkManager exists
        while (NetworkManager.Singleton == null)
            yield return null;

        var nm = NetworkManager.Singleton;

        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;

        // If host/server already started, spawn for already-connected clients
        if (nm.IsServer)
        {
            foreach (var kv in nm.ConnectedClients)
                EnsureBallForClient(kv.Key);
        }
    }

    private void OnDisable()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        EnsureBallForClient(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        if (ballByClient.TryGetValue(clientId, out var ballNO) && ballNO != null)
        {
            if (ballNO.IsSpawned)
                ballNO.Despawn(true);

            ballByClient.Remove(clientId);
        }

        // clear player's reference
        if (nm.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null)
        {
            var player = client.PlayerObject.GetComponent<NetworkGolferPlayer>();
            if (player != null) player.SetMyBallIdServer(0);
        }
    }

    private void EnsureBallForClient(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        if (golfBallPrefab == null)
        {
            Debug.LogError("[GolfGameManager] golfBallPrefab not set.");
            return;
        }

        if (ballByClient.TryGetValue(clientId, out var existing) && existing != null && existing.IsSpawned)
        {
            AssignBallIdToPlayer(clientId, existing.NetworkObjectId);
            return;
        }

        Vector3 pos = GetSpawnPosForClient(clientId);
        Quaternion rot = Quaternion.identity;

        var ballNO = Instantiate(golfBallPrefab, pos, rot);
        ballNO.Spawn();

        var ball = ballNO.GetComponent<NetworkGolfBall>();
        if (ball != null) ball.LogicalOwnerClientId = clientId;

        ballByClient[clientId] = ballNO;

        AssignBallIdToPlayer(clientId, ballNO.NetworkObjectId);

        Debug.Log($"[GolfGameManager] Spawned ball for client {clientId} at {pos}");
    }

    private Vector3 GetSpawnPosForClient(ulong clientId)
    {
        // 1) teeSpawns if provided
        if (teeSpawns != null && teeSpawns.Length > 0)
        {
            int idx = (int)(clientId % (ulong)teeSpawns.Length);
            if (teeSpawns[idx] != null)
                return teeSpawns[idx].position;
        }

        // 2) fallback: near player
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null)
        {
            var t = client.PlayerObject.transform;
            return t.position + t.forward * 2f;
        }

        // 3) last resort: origin
        return Vector3.zero;
    }

    private void AssignBallIdToPlayer(ulong clientId, ulong ballId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        if (!nm.ConnectedClients.TryGetValue(clientId, out var client)) return;
        if (client.PlayerObject == null) return;

        var player = client.PlayerObject.GetComponent<NetworkGolferPlayer>();
        if (player != null)
            player.SetMyBallIdServer(ballId);
    }
}
