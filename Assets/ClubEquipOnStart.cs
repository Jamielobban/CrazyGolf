using UnityEngine;

public class ClubEquipOnStart : MonoBehaviour
{
    [SerializeField] private GameObject clubPrefab; 
    [SerializeField] private GolferContextLink link;
 
    void Start()
    {
        var rigId = HandRigIdentity.FindLocal();
        if (!rigId || !rigId.gripPivot)
        {
            Debug.LogError("[ClubEquipOnStart] No local HandRig/gripPivot.");
            return;
        }
        if (!link) link = GetComponentInParent<GolferContextLink>();
        Equip(rigId.gripPivot);
    }

    void Equip(Transform gripPivot)
    {
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
