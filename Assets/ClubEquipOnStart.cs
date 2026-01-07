using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ClubEquipOnStart : MonoBehaviour
{
    [SerializeField] private GameObject clubPrefab;
    [SerializeField] private GolferContextLink link;

    private void Start()
    {
        if (!link) link = GetComponentInParent<GolferContextLink>();

        StartCoroutine(WaitForLocalHandRigThenEquip());
    }

    private IEnumerator WaitForLocalHandRigThenEquip()
    {
        // Wait for Netcode
        while (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
            yield return null;

        var nm = NetworkManager.Singleton;
        ulong localId = nm.LocalClientId;

        NetworkHandRig myRig = null;

        // Wait until my rig is spawned + owner id has replicated
        while (myRig == null)
        {
            foreach (var no in nm.SpawnManager.SpawnedObjectsList)
            {
                var rig = no.GetComponent<NetworkHandRig>();
                if (!rig) continue;

                if (rig.LogicalOwnerClientId.Value == localId)
                {
                    myRig = rig;
                    break;
                }
            }

            yield return null;
        }

        // Get the follower on that rig
        var follower = myRig.GetComponent<GripInertiaFollower>();
        if (!follower)
        {
            Debug.LogError("[ClubEquipOnStart] Found NetworkHandRig, but missing GripInertiaFollower.");
            yield break;
        }

        // You need a pivot to parent to. Best is to expose it explicitly on the rig/follower.
        // For now: use the follower's transform as the pivot (or replace with follower.GripPivot if you add it)
        Transform gripPivot = follower.transform;

        Equip(gripPivot);
    }

    private void Equip(Transform gripPivot)
    {
        if (!clubPrefab)
        {
            Debug.LogError("[ClubEquip] clubPrefab is null.");
            return;
        }

        GameObject club = Instantiate(clubPrefab);

        var gc = club.GetComponent<GolfClub>();
        if (gc && link) link.SetEquippedClub(gc);

        // Parent first, keep world pose for now
        club.transform.SetParent(gripPivot, true);

        Transform gripPoint = club.transform.Find("GripPoint");
        if (!gripPoint)
        {
            Debug.LogError("[ClubEquip] Club prefab missing GripPoint.");
            club.transform.localPosition = Vector3.zero;
            club.transform.localRotation = Quaternion.identity;
            return;
        }

        // Rotate club so GripPoint matches GripPivot
        Quaternion rotDelta = gripPivot.rotation * Quaternion.Inverse(gripPoint.rotation);
        club.transform.rotation = rotDelta * club.transform.rotation;

        // Move club so GripPoint sits on GripPivot
        Vector3 posDelta = gripPivot.position - gripPoint.position;
        club.transform.position += posDelta;
    }
}
