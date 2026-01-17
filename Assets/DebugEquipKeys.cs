using Unity.Netcode;
using UnityEngine;

public class DebugEquipKeys : NetworkBehaviour
{
    [SerializeField] private NetworkClubEquipment equip;

    private void Awake()
    {
        if (!equip) equip = GetComponent<NetworkClubEquipment>();
    }

    private void Update()
    {
        if (!IsOwner || equip == null) return;

        if (Input.GetKeyDown(KeyCode.Alpha9)) equip.DebugEquip(1);
        if (Input.GetKeyDown(KeyCode.Alpha8)) equip.DebugEquip(2);

    }
}
