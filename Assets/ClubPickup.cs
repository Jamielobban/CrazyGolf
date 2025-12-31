using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class ClubPickup : NetworkBehaviour
{
    [SerializeField] private GolfClub golfClub; // on same prefab root
    public GolfClub GolfClub => golfClub;

    void Awake()
    {
        if (!golfClub) golfClub = GetComponent<GolfClub>();
    }
}
