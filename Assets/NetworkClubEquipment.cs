using Unity.Netcode;
using UnityEngine;

public class NetworkClubEquipment : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private GolferContextLink link;   // on player root (stable)
    [SerializeField] private ClubVisualBinder binder;

    [Header("Pickup validation")]
    [SerializeField] private float pickupRange = 3.0f;

    // Networked equipped club object id (0 = none)
    public readonly NetworkVariable<int> equippedClubNetId =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private NetworkObject equippedClubNO;

    public override void OnNetworkSpawn()
    {
        if (!link) link = GetComponent<GolferContextLink>();

        equippedClubNetId.OnValueChanged += OnEquippedChanged;

        // resolve if already set
        if (equippedClubNetId.Value != 0)
            ResolveEquipped(equippedClubNetId.Value);
    }

    public override void OnNetworkDespawn()
    {
        equippedClubNetId.OnValueChanged -= OnEquippedChanged;
    }

    // Called by local interactor (owner)
    public void TryPickup(ClubPickup pickup)
    {
        if (!IsOwner || !pickup) return;

        var no = pickup.GetComponent<NetworkObject>();
        if (!no) return;

        RequestPickupServerRpc((int)no.NetworkObjectId);
    }

    [ServerRpc]
    private void RequestPickupServerRpc(int clubNetId, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue((ulong)clubNetId, out var clubNO) || !clubNO)
            return;

        // distance validation
        var playerNO = NetworkManager.ConnectedClients[sender].PlayerObject;
        if (!playerNO) return;

        float dist = Vector3.Distance(playerNO.transform.position, clubNO.transform.position);
        if (dist > pickupRange) return;

        // (optional) deny if someone else owns it, or transfer:
        clubNO.ChangeOwnership(sender);

        // Set equipped id
        equippedClubNetId.Value = clubNetId;
    }

    public void OnEquippedChanged(int oldId, int newId)
    {
        ResolveEquipped(newId);
    }

   private void ResolveEquipped(int id)
    {
        if (id == 0)
        {
            if (link) link.equippedClub = null;
            return;
        }

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue((ulong)id, out var no))
            return;

        var gc = no.GetComponent<GolfClub>();
        if (link) link.equippedClub = gc;

        binder = GetComponent<ClubVisualBinder>();
        binder.OnClubChanged(0, 1);
    }
}
