using Unity.Netcode;
using UnityEngine;

public class PlayerHeldController : NetworkBehaviour
{
    [SerializeField] private Camera cam;

    [Header("Bag throw")]
    [SerializeField] private float bagDropForward = 1.0f;
    [SerializeField] private float bagDropUp = 0.5f;

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

        // 1) Drop bag if I'm holding it
        //if (NetworkGolfBagCarry.TryGetHeldBag(OwnerClientId, out var bag))
        //{
        //    if (!cam) return;

        //    Vector3 vel = cam.transform.forward * bagDropForward + Vector3.up * bagDropUp;
        //    Vector3 ang = Vector3.zero;
        //    bag.RequestDropServerRpc(vel, ang);
        //    return;
        //}

        // 2) Otherwise drop equipped club (spawns world pickup)
        if (clubEquip != null && clubEquip.equippedClubId.Value != 0)
            clubEquip.DropEquipped();
    }

    // ---------- THROW (hold Q release) ----------
    public void Throw(float charge01)
    {
        if (!IsOwner) return;
        if (!cam) return;

        charge01 = Mathf.Clamp01(charge01);

        // 1) Throw bag if held
        //if (NetworkGolfBagCarry.TryGetHeldBag(OwnerClientId, out var bag))
        //{
        //    float fwd = Mathf.Lerp(bagThrowForwardMin, bagThrowForwardMax, charge01);
        //    float up  = Mathf.Lerp(bagThrowUpMin, bagThrowUpMax, charge01);
        //    float spin= Mathf.Lerp(bagSpinMin, bagSpinMax, charge01);

        //    Vector3 vel = cam.transform.forward * fwd + Vector3.up * up;
        //    Vector3 ang = new Vector3(0f, spin, 0f);

        //    bag.RequestDropServerRpc(vel, ang);
        //    return;
        //}

        // 2) Otherwise throw equipped club (your charged RPC)
        if (clubEquip != null && clubEquip.equippedClubId.Value != 0)
            clubEquip.ThrowEquippedCharged(charge01);
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

    public void DropBagOnly()
    {
        if (!IsOwner) return;
        if (!cam) cam = Camera.main;
        if (!cam) return;

        Debug.Log("Drop bag only");
        // Only affects bag. If no bag, do nothing.
        if (!NetworkGolfBagCarry.TryGetHeldBag(OwnerClientId, out var bag))
            return;

        Debug.Log(bag.gameObject.name);
        Vector3 vel = cam.transform.forward * bagDropForward + Vector3.up * bagDropUp;
        Vector3 ang = Vector3.zero;
        bag.RequestDropServerRpc(vel, ang);
    }
}
