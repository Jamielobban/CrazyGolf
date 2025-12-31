using UnityEngine;

public class ClubInteractor : MonoBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private float interactDist = 3.0f;
    [SerializeField] private LayerMask clubMask;

    [SerializeField] private NetworkClubEquipment equipment;

    ClubPickup lookedAt;

    void Awake()
    {
        if (!equipment) equipment = GetComponent<NetworkClubEquipment>();
    }

    void Update()
    {
        lookedAt = null;

        if (!cam) {
            cam = Camera.main;
            if (!cam) return;
        }
        Debug.DrawRay(
            cam.transform.position,
            cam.transform.forward * interactDist,
            lookedAt ? Color.green : Color.red
        );

        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, interactDist, clubMask,
                QueryTriggerInteraction.Ignore))
        {
            lookedAt = hit.collider.GetComponent<ClubPickup>();
        }

        if (lookedAt != null)
        {
            // replace with UI later
            Debug.Log($"[LOOK] {lookedAt.name} (press E)");

            if (Input.GetKeyDown(KeyCode.E))
                equipment.TryPickup(lookedAt);
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.E))
                Debug.Log($"[PRESSED E]");
        }
    }
}
