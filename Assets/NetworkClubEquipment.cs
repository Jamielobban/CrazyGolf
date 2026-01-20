using Unity.Netcode;
using UnityEngine;

public class NetworkClubEquipment : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private GolferContextLink link;
    [SerializeField] private ClubVisualBinder binder;

    [Header("Database")]
    [SerializeField] private ClubDatabase clubDb;

    [Header("Pickup Validation")]
    [SerializeField] private float pickupRange = 3.0f;

    [Header("Drop / Throw Spawn Offset (from camera)")]
    [SerializeField] private float dropForward = 0.6f;
    [SerializeField] private float dropUp = 0.1f;

    [Header("Throw (simple)")]
    [SerializeField] private float throwSpeed = 10f;
    [SerializeField] private float throwUpBoost = 1.5f;
    [SerializeField] private float throwSpin = 25f;

    [Header("Charged Throw")]
    [SerializeField] private float throwSpeedMin = 4f;
    [SerializeField] private float throwSpeedMax = 14f;
    [SerializeField] private float throwUpBoostMin = 0.8f;
    [SerializeField] private float throwUpBoostMax = 2.0f;
    [SerializeField] private float throwSpinMin = 10f;
    [SerializeField] private float throwSpinMax = 35f;

    // CLUB ID (0 = none)
    public readonly NetworkVariable<int> equippedClubId =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (!link) link = GetComponent<GolferContextLink>();
        if (!binder) binder = GetComponent<ClubVisualBinder>();

        equippedClubId.OnValueChanged += OnEquippedChanged;

        // apply initial
        OnEquippedChanged(-999, equippedClubId.Value);
    }

    public override void OnNetworkDespawn()
    {
        equippedClubId.OnValueChanged -= OnEquippedChanged;
    }

    private void OnEquippedChanged(int oldId, int newId)
    {
        if (!binder) binder = GetComponent<ClubVisualBinder>();
        if (binder) binder.OnClubChanged(oldId, newId);
    }

    // -------------------------
    // Pickup (owner calls)
    // -------------------------
    public void TryPickupWorldClub(WorldClub club)
    {
        if (!IsOwner || club == null) return;

        var no = club.GetComponent<NetworkObject>();
        if (!no || !no.IsSpawned) return;

        RequestPickupWorldClubServerRpc(no.NetworkObjectId);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestPickupWorldClubServerRpc(ulong clubNetObjectId, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(clubNetObjectId, out var clubNO) || clubNO == null)
            return;

        var worldClub = clubNO.GetComponent<WorldClub>();
        if (worldClub == null) return;

        // distance validation
        float dist = Vector3.Distance(transform.position, clubNO.transform.position);
        if (dist > pickupRange) return;

        // Equip by ID
        equippedClubId.Value = worldClub.ClubId;

        // Remove from world
        clubNO.Despawn(true);
    }

    // -------------------------
    // Drop / Throw (owner calls)
    // -------------------------
    public void DropEquipped()
    {
        if (!IsOwner) return;

        var cam = Camera.main;
        if (!cam) return;

        Vector3 pos = cam.transform.position + cam.transform.forward * dropForward + Vector3.up * dropUp;
        Vector3 fwd = cam.transform.forward;

        DropOrThrowServerRpc(pos, fwd, false);
    }

    public void ThrowEquipped()
    {
        if (!IsOwner) return;

        var cam = Camera.main;
        if (!cam) return;

        Vector3 pos = cam.transform.position + cam.transform.forward * dropForward + Vector3.up * dropUp;
        Vector3 fwd = cam.transform.forward;

        DropOrThrowServerRpc(pos, fwd, true);
    }

    public void ThrowEquippedCharged(float charge01)
    {
        if (!IsOwner) return;

        var cam = Camera.main;
        if (!cam) return;

        Vector3 pos = cam.transform.position + cam.transform.forward * dropForward + Vector3.up * dropUp;
        Vector3 fwd = cam.transform.forward;

        charge01 = Mathf.Clamp01(charge01);
        DropOrThrowChargedServerRpc(pos, fwd, charge01);
    }

    // -------------------------
    // Server: spawn correct world prefab by equippedClubId
    // -------------------------
    [ServerRpc(RequireOwnership = true)]
    private void DropOrThrowServerRpc(Vector3 spawnPos, Vector3 forward, bool doThrow, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

        int clubId = equippedClubId.Value;
        if (clubId <= 0) return;

        if (!clubDb)
        {
            Debug.LogError("[NetworkClubEquipment] clubDb is null.");
            return;
        }

        ClubData cd = clubDb.Get(clubId);
        if (cd == null || cd.worldPrefab == null)
        {
            Debug.LogError($"[NetworkClubEquipment] Missing ClubData/worldPrefab for clubId={clubId}");
            return;
        }

        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        GameObject go = Instantiate(cd.worldPrefab, spawnPos, Quaternion.LookRotation(forward, Vector3.up));
        NetworkObject no = go.GetComponent<NetworkObject>();

        if (!no)
        {
            Debug.LogError("[NetworkClubEquipment] World prefab is missing NetworkObject!");
            Destroy(go);
            return;
        }
        no.Spawn(true);

        // If your world prefab already "knows" its id, you can delete this
        var wc = no.GetComponent<WorldClub>();
        if (wc != null) wc.SetClubIdServer(clubId);

        var rb = no.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.WakeUp();
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            if (doThrow)
            {
                Vector3 v = forward * throwSpeed + Vector3.up * throwUpBoost;
                rb.linearVelocity = v;

                Vector3 spinAxis = Vector3.Cross(Vector3.up, forward);
                if (spinAxis.sqrMagnitude < 0.0001f) spinAxis = Vector3.right;
                spinAxis.Normalize();

                rb.angularVelocity = spinAxis * throwSpin;
            }
        }

        // Unequip after spawning
        equippedClubId.Value = 0;
    }

    [ServerRpc(RequireOwnership = true)]
    private void DropOrThrowChargedServerRpc(Vector3 spawnPos, Vector3 forward, float charge01, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

        int clubId = equippedClubId.Value;
        if (clubId <= 0) return;

        if (!clubDb)
        {
            Debug.LogError("[NetworkClubEquipment] clubDb is null.");
            return;
        }

        ClubData cd = clubDb.Get(clubId);
        if (cd == null || cd.worldPrefab == null)
        {
            Debug.LogError($"[NetworkClubEquipment] Missing ClubData/worldPrefab for clubId={clubId}");
            return;
        }

        charge01 = Mathf.Clamp01(charge01);

        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        GameObject go = Instantiate(cd.worldPrefab, spawnPos, Quaternion.LookRotation(forward, Vector3.up));
        NetworkObject no = go.GetComponent<NetworkObject>();

        if (!no)
        {
            Debug.LogError("[NetworkClubEquipment] World prefab is missing NetworkObject!");
            Destroy(go);
            return;
        }
        no.Spawn(true);

        // If your world prefab already "knows" its id, you can delete this
        var wc = no.GetComponent<WorldClub>();
        if (wc != null) wc.SetClubIdServer(clubId);

        var rb = no.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.WakeUp();
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            float spd  = Mathf.Lerp(throwSpeedMin, throwSpeedMax, charge01);
            float up   = Mathf.Lerp(throwUpBoostMin, throwUpBoostMax, charge01);
            float spin = Mathf.Lerp(throwSpinMin, throwSpinMax, charge01);

            rb.linearVelocity = forward * spd + Vector3.up * up;

            Vector3 spinAxis = Vector3.Cross(Vector3.up, forward);
            if (spinAxis.sqrMagnitude < 0.0001f) spinAxis = Vector3.right;
            spinAxis.Normalize();

            rb.angularVelocity = spinAxis * spin;
        }

        equippedClubId.Value = 0;
    }

    // -------------------------
    // Optional debug equip
    // -------------------------
    public void DebugEquip(int clubId)
    {
        if (!IsOwner) return;
        DebugEquipServerRpc(clubId);
    }

    [ServerRpc(RequireOwnership = true)]
    private void DebugEquipServerRpc(int clubId, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        equippedClubId.Value = clubId;
    }
}
