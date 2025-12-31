using Unity.Netcode;
using UnityEngine;

public class HandRigIdentity : NetworkBehaviour
{
    [Header("Assign in prefab")]
    public Transform gripPivot;

    public ulong OwnerId => NetworkObject ? NetworkObject.OwnerClientId : 0;

    // Helper: get the local player's hand rig
    public static HandRigIdentity FindLocal()
    {
        var localId = NetworkManager.Singleton.LocalClientId;
        var rigs = GameObject.FindGameObjectsWithTag("HandRig");
        for (int i = 0; i < rigs.Length; i++)
        {
            var id = rigs[i].GetComponent<HandRigIdentity>();
            if (id && id.OwnerId == localId)
                return id;
        }
        return null;
    }
}
