using Unity.Netcode;
using UnityEngine;

public class LocalPlayerUIBootstrap : NetworkBehaviour
{
    [SerializeField] private GameObject radialUIPrefab;

    private GameObject uiInstance;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;          // ONLY local player
        if (uiInstance != null) return;

        uiInstance = Instantiate(radialUIPrefab);

        // Optional but recommended if you load scenes
        DontDestroyOnLoad(uiInstance);
    }

    public override void OnNetworkDespawn()
    {
        if (uiInstance != null)
        {
            Destroy(uiInstance);
            uiInstance = null;
        }
    }
}
