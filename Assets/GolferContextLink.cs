using UnityEngine;

public class GolferContextLink : MonoBehaviour
{
   [Header("Runtime references")]
    public SwingPivotMouseRotate swing;
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

    }

     public void SetEquippedClub(GolfClub club)
    {
        equippedClub = club;
        equippedData = club ? club.data : null;
    }

    public void SetEquippedData(ClubData data)
    {
        equippedClub = null;
        equippedData = data;
    }

    
}