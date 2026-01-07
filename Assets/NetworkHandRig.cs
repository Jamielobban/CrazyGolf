using Unity.Netcode;
using UnityEngine;

public class NetworkHandRig : NetworkBehaviour
{
    public NetworkVariable<ulong> LogicalOwnerClientId =
        new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [SerializeField] private GripInertiaFollower follower;

    private bool bound;

    public override void OnNetworkSpawn()
    {
        if (!follower) follower = GetComponent<GripInertiaFollower>();

        // When the owner id arrives/changes, rebind the anchor.
        LogicalOwnerClientId.OnValueChanged += OnOwnerChanged;

        // Try immediately too (covers server + already-initialized cases).
        TryBind();
    }

    public override void OnNetworkDespawn()
    {
        LogicalOwnerClientId.OnValueChanged -= OnOwnerChanged;
    }

    private void OnOwnerChanged(ulong oldValue, ulong newValue)
    {
        TryBind();
    }

    private void TryBind()
    {
        if (bound) return;
        if (!follower) return;
        if (LogicalOwnerClientId.Value == ulong.MaxValue) return;

        follower.BindToPlayer(LogicalOwnerClientId.Value);
        bound = true;
    }
}
