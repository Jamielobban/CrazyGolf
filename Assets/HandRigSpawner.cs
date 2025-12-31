using Unity.Netcode;
using UnityEngine;

public class HandRigSpawner : NetworkBehaviour
{
    [SerializeField] private NetworkObject handRigPrefab;

    // Tag of the body anchor on the PLAYER
    [SerializeField] private string bodyAnchorTag = "BodyAnchorPivot";

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        SpawnForOwner();
    }

    void SpawnForOwner()
    {
        if (!handRigPrefab)
        {
            Debug.LogError("[HandRigSpawner] Missing handRigPrefab.");
            return;
        }

        // Find the owner's body anchor
        Transform bodyAnchor = FindBodyAnchorForOwner();
        if (!bodyAnchor)
        {
            Debug.LogError("[HandRigSpawner] Could not find BodyAnchorPivot for player.");
            return;
        }

        // Instantiate
        var rig = Instantiate(handRigPrefab);

        // IMPORTANT: set position BEFORE spawning
        rig.transform.position = bodyAnchor.position;
        rig.transform.rotation = bodyAnchor.rotation;

        rig.SpawnWithOwnership(OwnerClientId, true);
    }

    Transform FindBodyAnchorForOwner()
    {
        // Since this script is on the PLAYER NetworkObject,
        // just search within our own hierarchy first
        var anchors = GetComponentsInChildren<Transform>(true);
        foreach (var t in anchors)
        {
            if (t.CompareTag(bodyAnchorTag))
                return t;
        }

        return null;
    }
}
