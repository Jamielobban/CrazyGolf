using Unity.Netcode;
using UnityEngine;

public class NetworkBallTeeSocket : NetworkBehaviour
{
    [SerializeField] private Transform socket;
    public Transform Socket => socket ? socket : transform;

    public NetworkVariable<ulong> OccupiedBallNetId =
        new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsOccupied => OccupiedBallNetId.Value != ulong.MaxValue;
}
