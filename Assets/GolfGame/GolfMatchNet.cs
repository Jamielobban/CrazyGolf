using Unity.Netcode;
using UnityEngine;

public class GolfMatchNet : NetworkBehaviour
{
    [SerializeField] private GolfGameManager game;

    private void Awake()
    {
        if (!game) game = FindFirstObjectByType<GolfGameManager>();
    }

    // Call this from UI button / host menu / client ready screen etc
    public void RequestStartHole()
    {
        // anyone can press it, but server validates
        StartHoleServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void StartHoleServerRpc(RpcParams rpcParams = default)
    {
        // OPTIONAL: restrict who can start (host only)
        ulong sender = rpcParams.Receive.SenderClientId;
        if (sender != NetworkManager.ServerClientId) // host only
        {
            Debug.LogWarning($"[GolfMatchNet] Client {sender} tried to start hole (denied).");
            return;
        }

        if (!game) game = FindFirstObjectByType<GolfGameManager>();
        if (!game)
        {
            Debug.LogError("[GolfMatchNet] No GolfGameManager found.");
            return;
        }

        game.StartHoleServer(); // server-only logic
    }
}
