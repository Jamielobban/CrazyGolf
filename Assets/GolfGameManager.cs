using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GolfGameManager : MonoBehaviour
{
    [SerializeField] private NetworkObject golfBallPrefab;
    [SerializeField] private NetworkObject handRigPrefab;

    [Header("Spawn Points (optional)")]
    [SerializeField] private Transform[] teeSpawns;

    private readonly Dictionary<ulong, NetworkObject> ballByClient = new();
    private readonly Dictionary<ulong, NetworkObject> rigByClient  = new();

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
            {
                ulong clientId = kv.Key;

                EnsureBallForClient(clientId);
                EnsureRigForClient(clientId);

                // equip default for already-connected clients too
                var client = kv.Value;
                if (client.PlayerObject)
                {
                    var player = client.PlayerObject.GetComponent<NetworkGolferPlayer>();
                    if (player) player.EquippedClubId.Value = 0;
                }
            }
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
        EnsureRigForClient(clientId);

        if (nm.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject)
        {
            var player = client.PlayerObject.GetComponent<NetworkGolferPlayer>();
            if (player)
                player.EquippedClubId.Value = 0;
        }
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

        if (rigByClient.TryGetValue(clientId, out var rigNO) && rigNO != null)
        {
            if (rigNO.IsSpawned)
                rigNO.Despawn(true);

            rigByClient.Remove(clientId);
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

        //Debug.Log($"[GolfGameManager] Spawned ball for client {clientId} at {pos}");
    }

     private void EnsureRigForClient(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        if (handRigPrefab == null)
        {
            Debug.LogError("[GolfGameManager] handRigPrefab not set.");
            return;
        }

        if (rigByClient.TryGetValue(clientId, out var existing) && existing != null && existing.IsSpawned)
            return;

        var rigNO = Instantiate(handRigPrefab);
        rigNO.Spawn();

        var rig = rigNO.GetComponent<NetworkHandRig>();
        if (rig == null)
        {
            Debug.LogError("[GolfGameManager] handRigPrefab is missing NetworkHandRig component.");
            rigNO.Despawn(true);
            return;
        }

        rig.LogicalOwnerClientId.Value = clientId;
        rigByClient[clientId] = rigNO;
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
            return t.position + t.forward * 5f;
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
