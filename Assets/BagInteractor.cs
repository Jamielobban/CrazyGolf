using Unity.Netcode;
using UnityEngine;

public class BagInteractor : NetworkBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private float interactDist = 3.0f;
    [SerializeField] private LayerMask bagMask;

    private void Update()
    {
        if (!IsOwner) return;

        if (!cam) cam = Camera.main;
        if (!cam) return;

        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, interactDist, bagMask,
                QueryTriggerInteraction.Ignore))
            return;

        var bag = hit.collider.GetComponentInParent<NetworkGolfBag>();
        if (!bag) return;

        // E = deposit currently equipped into bag
        if (Input.GetKeyDown(KeyCode.E))
        {
            bag.DepositEquippedToBagServerRpc(NetworkObjectId);
        }

        // F = equip first club from bag
        if (Input.GetKeyDown(KeyCode.F))
        {
            bag.EquipFromBagServerRpc(NetworkObjectId, 0);
        }
    }
}
