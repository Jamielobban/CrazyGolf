using Unity.Netcode;
using UnityEngine;

public class BagHoldInteractor : NetworkBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private float interactDist = 3.0f;
    [SerializeField] private LayerMask bagMask;

    [Header("Drop Toss")]
    [SerializeField] private float dropForwardSpeed = 2.0f;
    [SerializeField] private float dropUpSpeed = 0.5f;
    [SerializeField] private float dropSpin = 2.0f;

    private void Update()
    {
        if (!IsOwner) return;

        if (!cam) cam = Camera.main;
        if (!cam) return;

        // Look for bag
        NetworkGolfBagCarry bag = null;
        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, interactDist, bagMask, QueryTriggerInteraction.Ignore))
            bag = hit.collider.GetComponentInParent<NetworkGolfBagCarry>();

        // Hold
        if (bag != null && Input.GetKeyDown(KeyCode.G))
        {
            bag.RequestHoldServerRpc();
        }

        // Drop (drop the bag you are currently holding, even if not looking at it)
        if (Input.GetKeyDown(KeyCode.H))
        {
            // find any bag in scene held by me (simple approach for now)
            var all = FindObjectsOfType<NetworkGolfBagCarry>();
            foreach (var b in all)
            {
                if (b != null && b.IsSpawned && b.IsHeld.Value && b.HeldByClientId.Value == OwnerClientId)
                {
                    Vector3 fwd = cam.transform.forward;
                    Vector3 v = fwd * dropForwardSpeed + Vector3.up * dropUpSpeed;
                    Vector3 av = new Vector3(0f, dropSpin, 0f);
                    b.RequestDropServerRpc(v, av);
                    break;
                }
            }
        }
    }
}
