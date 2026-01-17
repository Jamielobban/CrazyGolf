using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ClubVisualBinder : NetworkBehaviour
{
    [System.Serializable]
    public struct ClubEntry
    {
        public int id;
        public GameObject visualPrefab;
    }

    [SerializeField] private ClubEntry[] clubs;
    [SerializeField] private GolferContextLink link;

    private NetworkClubEquipment equipment;
    private GameObject currentClub;
    private int currentId = -999;

    private Coroutine attachRoutine;

    public override void OnNetworkSpawn()
    {
        if (!link) link = GetComponent<GolferContextLink>();

        equipment = GetComponent<NetworkClubEquipment>();
        if (!equipment)
        {
            Debug.LogError("[ClubVisualBinder] Missing NetworkClubEquipment.");
            return;
        }

        // Listen to the SAME NV (server-written)
        equipment.equippedClubId.OnValueChanged += OnClubChanged;

        // apply initial
        OnClubChanged(-999, equipment.equippedClubId.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (equipment != null)
            equipment.equippedClubId.OnValueChanged -= OnClubChanged;

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

        if (link) link.SetEquippedClub(null);

        if (newId <= 0) return; // 0 = none

        var prefab = GetVisualPrefab(newId);
        if (!prefab)
        {
            Debug.LogError($"[ClubVisualBinder] No visual prefab mapped for clubId={newId}");
            return;
        }

        currentClub = Instantiate(prefab);

        var ctxBinder = currentClub.GetComponent<ClubContextBinder>();
        if (ctxBinder) ctxBinder.Bind(link);
        else Debug.LogWarning("[ClubVisualBinder] Club prefab missing ClubContextBinder.");

        attachRoutine = StartCoroutine(AttachWhenRigReady(currentClub, equipment.OwnerClientId));
    }

    private GameObject GetVisualPrefab(int id)
    {
        foreach (var c in clubs)
            if (c.id == id) return c.visualPrefab;
        return null;
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

        var gc = clubInstance.GetComponent<GolfClub>();
        if (gc && link)
            link.SetEquippedClub(gc);

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
            Debug.LogError("[ClubVisualBinder] Club visual prefab missing GripPoint.");
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
