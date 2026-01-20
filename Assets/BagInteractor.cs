using UnityEngine;
using Unity.Netcode;

public class BagInteractor : NetworkBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private float interactDist = 3.0f;
    [SerializeField] private LayerMask bagMask;

    [SerializeField] private NetworkGolfBag bag;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (!IsOwner) return;

        //lookedAt = null;

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
            bagMask,
            QueryTriggerInteraction.Ignore
        );

        //if (hitClub)
            //lookedAt = hit.collider.GetComponentInParent<WorldClub>();

        //Debug.DrawRay(
            //cam.transform.position,
            //cam.transform.forward * interactDist,
            //lookedAt ? Color.green : Color.red
        //);
    }
}
