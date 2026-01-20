using UnityEngine;

public class GolferContextLink : MonoBehaviour
{
    [Header("Runtime references")]
    public SwingPivotMouseRotate swing;
    public NetworkGolferPlayer golfer;

    [Header("Database")]
    [SerializeField] private ClubDatabase clubDb;

    [Header("Equipped (runtime refs only)")]
    [SerializeField] private Transform clubHead;

    public Transform ClubHead => clubHead;

    // Source of truth for id should be NetworkClubEquipment
    public int EquippedClubId
    {
        get
        {
            var eq = GetComponent<NetworkClubEquipment>();
            return eq != null ? eq.equippedClubId.Value : 0;
        }
    }

    public ClubData Data => clubDb != null ? clubDb.Get(EquippedClubId) : null;

    private void Awake()
    {
        if (!golfer) golfer = GetComponent<NetworkGolferPlayer>();
        if (!swing)  swing  = GetComponent<SwingPivotMouseRotate>();
    }

    // Called by your binder after it instantiates the held prefab
    public void SetClubHead(Transform head)
    {
        clubHead = head;
    }
}
