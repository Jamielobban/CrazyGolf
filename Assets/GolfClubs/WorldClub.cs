using Unity.Netcode;
using UnityEngine;

public class WorldClub : NetworkBehaviour
{
    [SerializeField] private int clubId = 1;
    public int ClubId => clubId;

    // optional helper so server can set it when spawning throws
    public void SetClubIdServer(int id)
    {
        if (!IsServer) return;
        clubId = id;
    }
}
