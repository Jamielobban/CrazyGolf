using Unity.Netcode;
using UnityEngine;

public class ClubInteractor : NetworkBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private float interactDist = 3.0f;
    [SerializeField] private LayerMask clubMask;

    [SerializeField] private NetworkClubEquipment equipment;

    private WorldClub lookedAt;

    private void Awake()
    {
        if (!equipment) equipment = GetComponent<NetworkClubEquipment>();
    }

    private void Update()
    {
        if (!IsOwner) return;

        lookedAt = null;

        if (!cam)
        {
            cam = Camera.main;
            if (!cam) return;
        }

        bool hitClub = Physics.Raycast(
            cam.transform.position,
            cam.transform.forward,
            out var hit,
            interactDist,
            clubMask,
            QueryTriggerInteraction.Ignore
        );

        if (hitClub)
            lookedAt = hit.collider.GetComponentInParent<WorldClub>();

        Debug.DrawRay(
            cam.transform.position,
            cam.transform.forward * interactDist,
            lookedAt ? Color.green : Color.red
        );

        if (lookedAt != null)
        {
            Debug.Log($"[LOOK] {lookedAt.name} clubId={lookedAt.ClubId} (press E)");

            if (Input.GetKeyDown(KeyCode.E))
                equipment.TryPickupWorldClub(lookedAt);
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.E))
                Debug.Log("[PRESSED E] (no club)");
        }
    }
}
