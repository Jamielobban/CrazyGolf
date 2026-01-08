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
        while (NetworkManager.Singleton == null)
            yield return null;

        var nm = NetworkManager.Singleton;

        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;

        if (nm.SceneManager != null)
            nm.SceneManager.OnSceneEvent += OnSceneEvent;

        // Host/server already running: spawn for everyone already connected.
        // IMPORTANT: if Scene Management is enabled, don't spawn immediately here.
        if (nm.IsServer)
        {
            if (!nm.NetworkConfig.EnableSceneManagement)
            {
                foreach (var kv in nm.ConnectedClients)
                    SpawnAllForClient(kv.Key);
            }
            else
            {
                // With scene management, we wait for SynchronizeComplete per client.
                // Host (server local client) is effectively "ready", so you *can* spawn for host now,
                // but it's also fine to let it come through the scene event depending on NGO version.
                SpawnAllForClient(nm.LocalClientId);
            }
        }
    }

    private void OnDisable()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;

        if (nm.SceneManager != null)
            nm.SceneManager.OnSceneEvent -= OnSceneEvent;
    }

    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        // Host should spawn immediately (host's PlayerObject exists very quickly)
        if (clientId == nm.LocalClientId)
        {
            Debug.Log($"[GolfGameManager] Host connected ({clientId}) -> spawn now");
            StartCoroutine(SpawnAllWhenPlayerReady(clientId));
            return;
        }

        // Remote clients: wait until their PlayerObject exists (safe point)
        Debug.Log($"[GolfGameManager] Client {clientId} connected -> wait for PlayerObject then spawn");
        StartCoroutine(SpawnAllWhenPlayerReady(clientId));
    }

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (sceneEvent.SceneEventType == SceneEventType.SynchronizeComplete)
        {
            StartCoroutine(SpawnAllWhenPlayerReady(sceneEvent.ClientId));
        }
    }

    private void SpawnAllForClient(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        // Wait until PlayerObject exists (important even after sync complete sometimes)
        if (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
        {
            StartCoroutine(SpawnAllWhenPlayerReady(clientId));
            return;
        }

        EnsureBallForClient(clientId);
        EnsureRigForClient(clientId);

        var player = client.PlayerObject.GetComponent<NetworkGolferPlayer>();
        if (player)
            player.EquippedClubId.Value = 0;
    }

   private IEnumerator SpawnAllWhenPlayerReady(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) yield break;

        while (nm.IsServer &&
            (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null))
        {
            yield return null;
        }

        // one extra frame helps avoid edge timing
        yield return null;

        Debug.Log($"[GolfGameManager] PlayerObject ready for {clientId} -> spawning ball/rig");
        SpawnAllForClient(clientId);
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
        rigNO.SpawnWithOwnership(clientId);

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
        if (teeSpawns != null && teeSpawns.Length > 0)
        {
            int idx = (int)(clientId % (ulong)teeSpawns.Length);
            if (teeSpawns[idx] != null)
                return teeSpawns[idx].position;
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
}
