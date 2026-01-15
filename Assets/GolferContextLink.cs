using UnityEngine;

public class GolferContextLink : MonoBehaviour
{
   [Header("Runtime references")]
    public SwingPivotMouseRotate swing;
    public NetworkGolferPlayer golfer;

    [Header("Equipped (runtime)")]
    [SerializeField] public GolfClub equippedClub; 
    [SerializeField] private ClubData equippedData;
    [SerializeField] private Transform clubHead;

     public GolfClub EquippedClub => equippedClub;
     public ClubData Data => equippedData;
     public Transform ClubHead => clubHead;

      void Awake()
    {
        if (!golfer) golfer = GetComponent<NetworkGolferPlayer>();
        if (!swing)  swing  = GetComponent<SwingPivotMouseRotate>();

    }

    public void SetEquippedClub(GolfClub club)
    {
        equippedClub = club;
        equippedData = club ? club.data : null;

        clubHead = null;
        if (!equippedClub) return;

        var binder = equippedClub.GetComponentInChildren<ClubContextBinder>(true);
        if (!binder || !binder.clubhead)
        {
            Debug.LogError($"[{name}] ClubContextBinder/clubhead missing on {equippedClub.name}");
            return;
        }

        clubHead = binder.clubhead;
    }
    public void SetEquippedData(ClubData data)
    {
        equippedClub = null;
        equippedData = data;
    }

    
}