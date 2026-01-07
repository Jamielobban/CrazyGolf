using UnityEngine;

public class GolferContextLink : MonoBehaviour
{
   [Header("Runtime references")]
    public SwingPivotMouseRotate swing;
    public ClubFaceRollDriver face;
    public NetworkGolferPlayer golfer;

    [Header("Equipped (runtime)")]
    [SerializeField] public GolfClub equippedClub; 
    [SerializeField] private ClubData equippedData;

     public GolfClub EquippedClub => equippedClub;
     public ClubData Data => equippedData;

      void Awake()
    {
        if (!golfer) golfer = GetComponent<NetworkGolferPlayer>();
        if (!swing)  swing  = GetComponent<SwingPivotMouseRotate>();
        //if (!face)   face   = GetComponentInChildren<ClubFaceRollDriver>(true);
    }

     public void SetEquippedClub(GolfClub club)
    {
        equippedClub = club;
        equippedData = club ? club.data : null;

        // Debug so you KNOW it’s being set
        //Debug.Log($"[GolferContextLink] EquippedClub={(equippedClub ? equippedClub.name : "NULL")} Data={(equippedData ? equippedData.clubName : "NULL")}");
    }

    public void SetEquippedData(ClubData data)
    {
        equippedClub = null;
        equippedData = data;

        //Debug.Log($"[GolferContextLink] EquippedData={(equippedData ? equippedData.clubName : "NULL")}");
    }

    
}