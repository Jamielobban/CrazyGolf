using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ClubVisualBinder : NetworkBehaviour
{
    [System.Serializable]
    public struct ClubEntry
    {
        public int id;
        public GameObject visualPrefab; // NON-networked visual prefab
    }

    [SerializeField] private ClubEntry[] clubs;
    [SerializeField] private GolferContextLink link;

    private NetworkGolferPlayer player;
    private GameObject currentClub;
    private int currentId = -999;

    private Coroutine attachRoutine;

    public override void OnNetworkSpawn()
    {
        if (!link) link = GetComponent<GolferContextLink>();

        player = GetComponent<NetworkGolferPlayer>();
        if (!player)
        {
            Debug.LogError("[ClubVisualBinder] Missing NetworkGolferPlayer on player object.");
            return;
        }

        player.EquippedClubId.OnValueChanged += OnClubChanged;

        // apply initial state (important for late join / already-set values)
        OnClubChanged(-999, player.EquippedClubId.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (player != null)
            player.EquippedClubId.OnValueChanged -= OnClubChanged;

        if (currentClub)
            Destroy(currentClub);
    }

    private void OnClubChanged(int oldId, int newId)
    {
        if (currentId == newId) return;
        currentId = newId;

        // clear previous visual + data ref
        if (currentClub)
        {
            Destroy(currentClub);
            currentClub = null;
        }

        if (link) link.SetEquippedClub(null);

        if (newId < 0) return;

        var prefab = GetVisualPrefab(newId);
        if (!prefab)
        {
            Debug.LogError($"[ClubVisualBinder] No visual prefab mapped for clubId={newId}");
            return;
        }

        currentClub = Instantiate(prefab);
        
        var binder = currentClub.GetComponent<ClubContextBinder>();
        if (binder) binder.Bind(link);
        else Debug.LogWarning("[ClubVisualBinder] Club prefab missing ClubContextBinder.");

        if (attachRoutine != null)
        {
            StopCoroutine(attachRoutine);
            attachRoutine = null;
        }
        attachRoutine = StartCoroutine(AttachWhenRigReady(currentClub));
    }

    private GameObject GetVisualPrefab(int id)
    {
        foreach (var c in clubs)
            if (c.id == id) return c.visualPrefab;
        return null;
    }

    private IEnumerator AttachWhenRigReady(GameObject clubInstance)
    {
        // Safety: club might be destroyed before rig exists
        if (!clubInstance) yield break;

        Transform gripPivot = null;

        while (gripPivot == null)
        {
            // If club changed while waiting, abort
            if (!clubInstance || clubInstance != currentClub)
                yield break;

            gripPivot = FindGripPivotForPlayer(OwnerClientId);
            yield return null;
        }

        AttachToPivot(clubInstance.transform, gripPivot);

        // Set equipped data ONCE
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
            return follower ? follower.transform : null; // replace with explicit pivot later
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