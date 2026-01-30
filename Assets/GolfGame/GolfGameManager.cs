using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GolfGameManager : MonoBehaviour
{
    [SerializeField] private NetworkObject golfBallPrefab;
    [SerializeField] private NetworkObject handRigPrefab;
    [SerializeField] private NetworkObject golfBagPrefab;

    [Header("Spawn Points (optional)")]
    [SerializeField] private Transform teeRoot;

    private static readonly Vector3[] teeOffsets =
    {
        new Vector3(0f, 0f, 0f),
        new Vector3(2.5f, 0f, 0f),
        new Vector3(-2.5f, 0f, 0f),
        new Vector3(0f, 0f, -3f),
    };

    private readonly Dictionary<ulong, NetworkObject> ballByClient = new();
    private readonly Dictionary<ulong, NetworkObject> rigByClient = new();
    private readonly Dictionary<ulong, NetworkObject> bagByClient = new();

    private bool hooked;

    private void OnEnable()
    {
        StartCoroutine(HookWhenServerReady());
    }

    private IEnumerator HookWhenServerReady()
    {
        // 1) Wait for NetworkManager to exist
        while (NetworkManager.Singleton == null)
            yield return null;

        var nm = NetworkManager.Singleton;

        // 2) Wait until we are actually the server/host
        while (!nm.IsServer)
            yield return null;

        // 3) Hook only once
        if (hooked) yield break;
        hooked = true;

        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;

        // 4) Spawn for anyone already connected (host counts too)
        foreach (var kv in nm.ConnectedClients)
        {
            ulong clientId = kv.Key;
            EnsureRigForClient(clientId);
            EnsureBagForClient(clientId);
            EnsureBallForClient(clientId);
        }
    }

    private void OnDisable()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;

        hooked = false;
    }

    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        EnsureRigForClient(clientId);
        EnsureBagForClient(clientId);
        EnsureBallForClient(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        DespawnForClient(clientId, ballByClient);
        DespawnForClient(clientId, rigByClient);
        DespawnForClient(clientId, bagByClient);

        // clear player's references (if player object still around)
        if (nm.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null)
        {
            var p = client.PlayerObject.GetComponent<NetworkGolferPlayer>();
            if (p != null)
            {
                p.SetMyBallIdServer(0);
                p.SetMyBagIdServer(0);
            }
        }
    }

    private static void DespawnForClient(ulong clientId, Dictionary<ulong, NetworkObject> dict)
    {
        if (!dict.TryGetValue(clientId, out var no) || no == null) return;

        if (no.IsSpawned)
            no.Despawn(true);

        dict.Remove(clientId);
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
        var ballNO = Instantiate(golfBallPrefab, pos, Quaternion.identity);
        ballNO.Spawn();

        // NEW: set ownership on BallState (not old NetworkGolfBall.LogicalOwnerClientId)
        var st = ballNO.GetComponent<NetworkGolfBallState>();
        if (st != null)
        {
            st.Mode.Value = NetworkGolfBallState.BallMode.Round;
            st.LogicalOwnerClientId.Value = clientId;
        }

        ballByClient[clientId] = ballNO;
        AssignBallIdToPlayer(clientId, ballNO.NetworkObjectId);
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

    private void EnsureBagForClient(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        if (golfBagPrefab == null)
        {
            Debug.LogError("[GolfGameManager] golfBagPrefab not set.");
            return;
        }

        if (bagByClient.TryGetValue(clientId, out var existing) && existing != null && existing.IsSpawned)
        {
            AssignBagIdToPlayer(clientId, existing.NetworkObjectId);
            return;
        }

        Vector3 pos = GetSpawnPosForClient(clientId) + Vector3.right * 3.0f + Vector3.up * 2.0f;
        var bagNO = Instantiate(golfBagPrefab, pos, Quaternion.identity);
        bagNO.Spawn(); // server-owned (no ChangeOwnership)

        var bag = bagNO.GetComponent<NetworkGolfBag>();
        if (bag != null)
            bag.LogicalOwnerClientId.Value = clientId;

        bagByClient[clientId] = bagNO;
        AssignBagIdToPlayer(clientId, bagNO.NetworkObjectId);
    }

    private Vector3 GetSpawnPosForClient(ulong clientId)
    {
        if (teeRoot != null)
        {
            int idx = (int)(clientId % (ulong)teeOffsets.Length);
            Vector3 offsetWorld = teeRoot.rotation * teeOffsets[idx];
            return teeRoot.position + offsetWorld;
        }

        var nm = NetworkManager.Singleton;
        if (nm != null && nm.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null)
        {
            var t = client.PlayerObject.transform;
            return t.position + t.forward * 5f;
        }

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

    private void AssignBagIdToPlayer(ulong clientId, ulong bagId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        if (!nm.ConnectedClients.TryGetValue(clientId, out var client)) return;
        if (client.PlayerObject == null) return;

        var player = client.PlayerObject.GetComponent<NetworkGolferPlayer>();
        if (player != null)
            player.SetMyBagIdServer(bagId);
    }

    public void StartHoleServer()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        TeleportPlayersToTees();
    }

    private void TeleportPlayersToTees()
    {
        var nm = NetworkManager.Singleton;
        if (!nm || !nm.IsServer) return;
        if (!teeRoot) return;

        foreach (var kv in nm.ConnectedClients)
        {
            ulong clientId = kv.Key;
            var playerObj = kv.Value.PlayerObject;

            if (!playerObj)
            {
                Debug.LogWarning($"[GolfGameManager] No PlayerObject yet for client {clientId}, skipping teleport.");
                continue;
            }

            int idx = (int)(clientId % (ulong)teeOffsets.Length);
            Vector3 pos = teeRoot.position + (teeRoot.rotation * teeOffsets[idx]);

            playerObj.transform.SetPositionAndRotation(pos, teeRoot.rotation);

            var rb = playerObj.GetComponent<Rigidbody>();
            if (rb)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
}
