using UnityEngine;

public class GolferContextLink : MonoBehaviour
{
    public NetworkGolferPlayer golfer;
    public ClubFaceRollDriver face;
    public GolfClub equippedClub;

    public ClubData Data => equippedClub ? equippedClub.data : null;
}