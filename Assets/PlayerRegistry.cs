using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerRegistry : MonoBehaviour
{
    public static PlayerRegistry Instance { get; private set; }

    private readonly Dictionary<ulong, GolferContextLink> ctxByClient = new();

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Register(GolferContextLink ctx)
    {
        if (!ctx || !ctx.golfer) return;
        ctxByClient[ctx.golfer.OwnerClientId] = ctx;
        Debug.Log($"[PlayerRegistry] Registered client {ctx.golfer.OwnerClientId} -> {ctx.name}");
    }

    public void Unregister(GolferContextLink ctx)
    {
        if (!ctx || !ctx.golfer) return;

        ulong id = ctx.golfer.OwnerClientId;
        if (ctxByClient.TryGetValue(id, out var existing) && existing == ctx)
            ctxByClient.Remove(id);

        Debug.Log($"[PlayerRegistry] Unregistered client {id} -> {ctx.name}");
    }

    public bool TryGetContext(ulong clientId, out GolferContextLink ctx)
        => ctxByClient.TryGetValue(clientId, out ctx);
}
