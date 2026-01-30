using Unity.Netcode;
using UnityEngine;

public class BagUseable : MonoBehaviour, IUseable
{
    [SerializeField] private NetworkGolfBag bagInv;

    public int Priority => 5;
    public string UsePrompt => "Open bag";

    private void Awake()
    {
        if (!bagInv) bagInv = GetComponent<NetworkGolfBag>();
    }

    // TAP E
    public bool Use(Interactor who)
    {
        if (!bagInv || !bagInv.IsSpawned) return false;

        // Later: BagUI.Instance.Open(bagInv);
        Debug.Log("[BAG] Open UI");
        return true;
    }

    // DEBUG: J
    public void DebugDeposit(Interactor who)
    {
        if (!bagInv || !bagInv.IsSpawned) return;

        var playerNO = who.GetComponent<NetworkObject>();
        if (!playerNO) return;

        bagInv.DepositEquippedToBagServerRpc(playerNO.NetworkObjectId);
        Debug.Log("[BAG] Deposit equipped");
    }

    // DEBUG: K
    public void DebugEquipIndex(Interactor who, int index)
    {
        if (!bagInv || !bagInv.IsSpawned) return;

        var playerNO = who.GetComponent<NetworkObject>();
        if (!playerNO) return;

        bagInv.EquipFromBagServerRpc(playerNO.NetworkObjectId, index);
        Debug.Log($"[BAG] Equip index {index}");
    }
}
