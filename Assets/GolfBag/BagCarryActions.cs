using Unity.Netcode;
using UnityEngine;

public class BagCarryActions : NetworkBehaviour
{
    [SerializeField] private Camera cam;

    [Header("Drop toss")]
    [SerializeField] private float dropForwardSpeed = 2.0f;
    [SerializeField] private float dropUpSpeed = 0.5f;
    [SerializeField] private float dropSpinYaw = 3.0f;

    private NetworkGolfBagCarry heldBag; // cached

    private void Update()
    {
        if (!IsOwner) return;
        if (!cam) cam = Camera.main;
    }

    // Call from input (G) when looking at a bag (we'll set heldBag from the interactable)
    public void RequestHold(NetworkGolfBagCarry bag)
    {
        if (!IsOwner) return;
        if (bag == null) return;

        bag.RequestHoldServerRpc();
        heldBag = bag;
    }

    // Call from input (H)
    public void RequestDrop()
    {
        if (!IsOwner) return;
        if (!cam) return;

        var b = GetHeldBagOrFindOne();
        if (b == null)
        {
            Debug.LogWarning("Drop failed: no held bag found for me.");
            return;
        }

        Vector3 fwd = cam.transform.forward;
        Vector3 vel = fwd * dropForwardSpeed + Vector3.up * dropUpSpeed;
        Vector3 angVel = new Vector3(0f, dropSpinYaw, 0f);

        b.RequestDropServerRpc(vel, angVel);
    }

    private NetworkGolfBagCarry GetHeldBagOrFindOne()
    {
        // Use cached if still held by me
        if (heldBag != null &&
            heldBag.IsSpawned &&
            heldBag.IsHeld.Value &&
            heldBag.HeldByClientId.Value == OwnerClientId)
        {
            return heldBag;
        }

        heldBag = null;

        // Fallback: find any bag held by me
        var all = Object.FindObjectsByType<NetworkGolfBagCarry>(FindObjectsSortMode.None);
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
