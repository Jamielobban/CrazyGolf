using Unity.Netcode;
using UnityEngine;

public class BagInteractor : NetworkBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private float interactDist = 3.0f;
    [SerializeField] private LayerMask bagMask;

    [Header("Drop toss")]
    [SerializeField] private float dropForwardSpeed = 2.0f;
    [SerializeField] private float dropUpSpeed = 0.5f;
    [SerializeField] private float dropSpinYaw = 3.0f;

    // Local cached reference to “the bag I’m holding”
    private NetworkGolfBagCarry heldBag;

    private void Update()
    {
        if (!IsOwner) return;

        if (!cam) cam = Camera.main;
        if (!cam) return;

        // 1) Raycast to find a bag we're looking at
        NetworkGolfBag bagInv = null;
        NetworkGolfBagCarry bagCarry = null;

        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, interactDist, bagMask, QueryTriggerInteraction.Ignore))
        {
            bagInv = hit.collider.GetComponentInParent<NetworkGolfBag>();
            bagCarry = hit.collider.GetComponentInParent<NetworkGolfBagCarry>();
        }

        // 2) Inventory actions (only when looking at bag)
        if (bagInv != null)
        {
            if (Input.GetKeyDown(KeyCode.E))
                bagInv.DepositEquippedToBagServerRpc(NetworkObjectId);

            if (Input.GetKeyDown(KeyCode.F))
                bagInv.EquipFromBagServerRpc(NetworkObjectId, 0);
        }

        // 3) HOLD (when looking at a bag)
        if (bagCarry != null && Input.GetKeyDown(KeyCode.G))
        {
            bagCarry.RequestHoldServerRpc();
            heldBag = bagCarry; // cache it so drop works even if raycast can’t see it
        }

        // 4) DROP (works anywhere, no raycast needed)
       if (Input.GetKeyDown(KeyCode.H))
        {
            var b = GetHeldBagOrFindOne();   // <- use your helper
            if (b == null)
            {
                Debug.LogWarning("Drop failed: no held bag found for me.");
                return;
            }

            Debug.Log("Bag dropped");

            Vector3 fwd = cam.transform.forward;
            Vector3 vel = fwd * dropForwardSpeed + Vector3.up * dropUpSpeed;
            Vector3 angVel = new Vector3(0f, dropSpinYaw, 0f);

            b.RequestDropServerRpc(vel, angVel);
        }
    }

    private NetworkGolfBagCarry GetHeldBagOrFindOne()
    {
        // Use cached if still actually held by me
        if (heldBag != null &&
            heldBag.IsSpawned &&
            heldBag.IsHeld.Value &&
            heldBag.HeldByClientId.Value == OwnerClientId)
        {
            return heldBag;
        }

        heldBag = null;

        // Fallback: find any bag held by me
        var all = Object.FindObjectsByType<NetworkGolfBagCarry>(
            FindObjectsSortMode.None
        );
        foreach (var b in all)
        {
            if (b != null && b.IsSpawned && b.IsHeld.Value && b.HeldByClientId.Value == OwnerClientId)
            {
                heldBag = b;
                return heldBag;
            }
        }

        return null;
    }
}
