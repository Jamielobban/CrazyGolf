using Unity.Netcode;
using UnityEngine;

public class PlayerHeldController : NetworkBehaviour
{
    [SerializeField] private Camera cam;

    private NetworkClubEquipment clubEquip;
    private NetworkGolfBagCarry cachedBag;

    private void Awake()
    {
        clubEquip = GetComponent<NetworkClubEquipment>();
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;
        if (!cam) cam = Camera.main;
    }

    // ---------- DROP (tap Q) ----------
    public void Drop()
    {
        if (!IsOwner) return;

        // 1) Drop bag if held
        if (NetworkGolfBagCarry.TryGetHeldBag(OwnerClientId, out var bag))
        {
            Vector3 vel = Vector3.zero;
            Vector3 ang = Vector3.zero;
            bag.RequestDropServerRpc(vel, ang);
            return;
        }

        // 2) Drop equipped club
        if (clubEquip != null && clubEquip.equippedClubId.Value != 0)
        {
            clubEquip.DropEquipped();
        }
    }

    // ---------- THROW (hold Q release) ----------
    public void Throw(float charge01)
    {
        if (!IsOwner) return;

        if (!cam) return;

        // 1) Throw bag if held
        if (NetworkGolfBagCarry.TryGetHeldBag(OwnerClientId, out var bag))
        {
            Vector3 fwd = cam.transform.forward;
            Vector3 vel = fwd * Mathf.Lerp(2f, 8f, charge01) + Vector3.up * 1.5f;
            Vector3 ang = new Vector3(0f, 6f, 0f);
            bag.RequestDropServerRpc(vel, ang);
            return;
        }

        // 2) Throw equipped club
        if (clubEquip != null && clubEquip.equippedClubId.Value != 0)
        {
            clubEquip.ThrowEquippedCharged(charge01);
        }
    }
    public bool TryTakeFromLook(RaycastHit hit)
    {
        if (!IsOwner) return false;
        if (hit.collider == null) return false;

        var bagCarry = hit.collider.GetComponent<NetworkGolfBagCarry>();
        if (bagCarry == null) return false;

        bagCarry.RequestHoldServerRpc();
        cachedBag = bagCarry;
        return true;
    }
}
