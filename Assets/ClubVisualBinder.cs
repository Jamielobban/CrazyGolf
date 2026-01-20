using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ClubVisualBinder : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private GolferContextLink link;
    [SerializeField] private ClubDatabase clubDb;

    private NetworkClubEquipment equipment;
    private GameObject currentClub;
    private int currentId = -999;

    private Coroutine attachRoutine;

    public override void OnNetworkSpawn()
    {
        if (!link) link = GetComponent<GolferContextLink>();
        if (!equipment) equipment = GetComponent<NetworkClubEquipment>();

        if (!equipment)
        {
            Debug.LogError("[ClubVisualBinder] Missing NetworkClubEquipment.");
            return;
        }

        equipment.equippedClubId.OnValueChanged += OnClubChanged;

        // apply initial
        OnClubChanged(-999, equipment.equippedClubId.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (equipment != null)
            equipment.equippedClubId.OnValueChanged -= OnClubChanged;

        if (attachRoutine != null)
            StopCoroutine(attachRoutine);

        if (currentClub)
            Destroy(currentClub);
    }

    public void OnClubChanged(int oldId, int newId)
    {
        if (currentId == newId) return;
        currentId = newId;

        if (attachRoutine != null)
        {
            StopCoroutine(attachRoutine);
            attachRoutine = null;
        }

        if (currentClub)
        {
            Destroy(currentClub);
            currentClub = null;
        }

        // Clear runtime refs (not data)
        if (link != null)
        {
           link.SetClubHead(null);
        }

        if (newId <= 0) return; // 0 = none

        if (!clubDb)
        {
            Debug.LogError("[ClubVisualBinder] clubDb is not assigned.");
            return;
        }

        ClubData cd = clubDb.Get(newId);
        if (cd == null)
        {
            Debug.LogError($"[ClubVisualBinder] No ClubData found for clubId={newId}");
            return;
        }

        // If you go with “2 prefabs per club”, use heldPrefab here
        if (cd.heldPrefab == null)
        {
            Debug.LogError($"[ClubVisualBinder] ClubData for id={newId} missing heldPrefab");
            return;
        }

        currentClub = Instantiate(cd.heldPrefab);

        // Bind runtime refs like clubhead into GolferContextLink
        var ctxBinder = currentClub.GetComponent<ClubContextBinder>();
        if (ctxBinder) ctxBinder.Bind(link);
        else Debug.LogWarning("[ClubVisualBinder] Held prefab missing ClubContextBinder.");

        // Attach under the correct hand rig pivot
        attachRoutine = StartCoroutine(AttachWhenRigReady(currentClub, equipment.OwnerClientId));
    }

    private IEnumerator AttachWhenRigReady(GameObject clubInstance, ulong playerOwnerClientId)
    {
        if (!clubInstance) yield break;

        Transform gripPivot = null;

        while (gripPivot == null)
        {
            if (!clubInstance || clubInstance != currentClub)
                yield break;

            gripPivot = FindGripPivotForPlayer(playerOwnerClientId);
            yield return null;
        }

        AttachToPivot(clubInstance.transform, gripPivot);

        attachRoutine = null;
    }

    private Transform FindGripPivotForPlayer(ulong playerOwnerClientId)
    {
        var nm = NetworkManager.Singleton;
        if (!nm) return null;

        foreach (var no in nm.SpawnManager.SpawnedObjectsList)
        {
            var rig = no.GetComponent<NetworkHandRig>();
            if (!rig) continue;

            if (rig.LogicalOwnerClientId.Value != playerOwnerClientId)
                continue;

            var follower = rig.GetComponent<GripInertiaFollower>();
            return follower ? follower.transform : null;
        }

        return null;
    }

    private void AttachToPivot(Transform club, Transform gripPivot)
    {
        club.SetParent(gripPivot, true);

        Transform gripPoint = club.Find("GripPoint");
        if (!gripPoint)
        {
            Debug.LogError("[ClubVisualBinder] Held prefab missing GripPoint.");
            club.localPosition = Vector3.zero;
            club.localRotation = Quaternion.identity;
            return;
        }

        Quaternion rotDelta = gripPivot.rotation * Quaternion.Inverse(gripPoint.rotation);
        club.rotation = rotDelta * club.rotation;

        Vector3 posDelta = gripPivot.position - gripPoint.position;
        club.position += posDelta;
    }
}
