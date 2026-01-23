using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class RadialClubUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform center;      // the visual center (RadialRoot)
    [SerializeField] private RectTransform slotsParent; // where slot buttons are parented

    [Header("Prefabs")]
    [SerializeField] private RadialSlotButton slotPrefab;

    [Header("Data")]
    [SerializeField] private ClubDatabase clubDb;

    [Header("Layout")]
    [SerializeField] private float radius = 220f;
    [SerializeField] private float deadzone = 60f;

    [Header("Input")]
    [SerializeField] private KeyCode holdKey = KeyCode.Tab;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private bool debugHighlightSpam = false; // logs every highlight change
    [SerializeField] private bool debugDrawRays = true;

    private NetworkGolfBag bag;
    private NetworkObject localPlayerNO;

    private readonly List<RadialSlotButton> slots = new();
    private int highlighted = -1;

    private void Awake()
    {
        if (!group) group = GetComponent<CanvasGroup>();
        HideImmediate();
        Log($"Awake. group={(group ? "OK" : "NULL")} center={(center ? "OK" : "NULL")} slotsParent={(slotsParent ? "OK" : "NULL")}");
    }

    private void OnEnable()
    {
        Log("OnEnable()");
        TryResolveLocalPlayerAndBag();
    }

    private void OnDisable()
    {
        if (bag != null)
        {
            bag.Clubs.OnListChanged -= OnBagListChanged;
            Log("OnDisable(): Unhooked bag list changed.");
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(holdKey))
        {
            Log($"HoldKey DOWN ({holdKey}) -> Open()");
            Open();
        }

        if (Input.GetKeyUp(holdKey))
        {
            Log($"HoldKey UP ({holdKey}) -> ConfirmSelection() + Close()");
            ConfirmSelection();
            Close();
        }

        if (group != null && group.alpha > 0.9f)
        {
            UpdateHighlightFromMouse();
            if (debugDrawRays) DebugDrawMouseDir();
        }
    }

    public void Open()
    {
        if (!TryResolveLocalPlayerAndBag())
        {
            Log("Open(): FAILED to resolve local player/bag (not ready yet?)");
            return;
        }

        BuildFromBag();
        ShowImmediate();
        Log($"Open(): bagCount={bag.Clubs.Count} localPlayerNetId={localPlayerNO.NetworkObjectId}");
    }

    public void Close()
    {
        HideImmediate();
        highlighted = -1;
        SetAllHighlights(-1);
        Log("Close(): UI hidden, highlight reset.");
    }

    private bool TryResolveLocalPlayerAndBag()
    {
        if (NetworkManager.Singleton == null)
        {
            Log("TryResolve: NetworkManager.Singleton is NULL");
            return false;
        }

        // Local player
        if (localPlayerNO == null)
        {
            var lp = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
            if (lp == null)
            {
                Log("TryResolve: LocalPlayerObject is NULL (player not spawned yet)");
                return false;
            }

            localPlayerNO = lp;
            Log($"TryResolve: Local player found. netId={localPlayerNO.NetworkObjectId} ownerClientId={localPlayerNO.OwnerClientId}");
        }

        // Bag
        if (bag == null)
        {
            ulong localId = NetworkManager.Singleton.LocalClientId;
            var allBags = FindObjectsOfType<NetworkGolfBag>();

            Log($"TryResolve: Searching bags... found={allBags.Length} localClientId={localId}");

            foreach (var b in allBags)
            {
                if (b == null) continue;

                Log($"  Bag candidate '{b.name}': logicalOwner={b.LogicalOwnerClientId.Value} netOwner={b.OwnerClientId} bagNetId={(b.TryGetComponent<NetworkObject>(out var no) ? no.NetworkObjectId : 0)}");

                if (b.LogicalOwnerClientId.Value == localId)
                {
                    bag = b;
                    Log($"TryResolve: ✅ Selected bag '{bag.name}' for local client {localId}");
                    break;
                }
            }

            if (bag == null)
            {
                Log("TryResolve: ❌ No matching bag found (LogicalOwnerClientId didn’t match local client).");
                return false;
            }

            // Hook list changes
            bag.Clubs.OnListChanged -= OnBagListChanged; // safety
            bag.Clubs.OnListChanged += OnBagListChanged;
            Log("TryResolve: Hooked bag.Clubs.OnListChanged");
        }

        return true;
    }

    private void OnBagListChanged(NetworkListEvent<int> evt)
    {
        Log($"OnBagListChanged: type={evt.Type} index={evt.Index} value={evt.Value} bagCountNow={bag.Clubs.Count}");

        // Only rebuild live if UI is open
        if (group != null && group.alpha > 0.9f)
        {
            Log("OnBagListChanged: UI open -> rebuilding");
            BuildFromBag();
        }
    }

    private void BuildFromBag()
    {
        if (bag == null)
        {
            Log("BuildFromBag(): bag is NULL");
            return;
        }

        int count = bag.Clubs.Count;
        Log($"BuildFromBag(): building slots for count={count}");

        if (!slotPrefab)
        {
            Log("BuildFromBag(): slotPrefab is NULL (assign it in inspector)");
            return;
        }

        // Ensure enough slot instances
        while (slots.Count < count)
        {
            var s = Instantiate(slotPrefab, slotsParent);
            slots.Add(s);
            Log($"BuildFromBag(): Instantiated slot #{slots.Count - 1}");
        }

        // Enable/disable extras
        for (int i = 0; i < slots.Count; i++)
            slots[i].gameObject.SetActive(i < count);

        if (count <= 0)
        {
            highlighted = -1;
            SetAllHighlights(-1);
            Log("BuildFromBag(): bag empty -> nothing to show");
            return;
        }

        float step = 360f / count;

        for (int i = 0; i < count; i++)
        {
            float ang = (90f - i * step) * Mathf.Deg2Rad; // start at top
            Vector2 pos = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;

            slots[i].Rect.anchoredPosition = pos;

            int clubId = bag.Clubs[i];
            var data = clubDb ? clubDb.Get(clubId) : null;

            var icon = data ? data.icon : null;
            var label = data ? data.shortName : clubId.ToString();

            slots[i].Set(icon, label);

            Log($"  Slot[{i}] clubId={clubId} label='{label}' pos={pos}");
        }

        if (highlighted >= count) highlighted = -1;
        SetAllHighlights(highlighted);
    }

    private void UpdateHighlightFromMouse()
    {
        if (bag == null || bag.Clubs.Count == 0) return;

        Vector2 centerScreen = RectTransformUtility.WorldToScreenPoint(null, center.position);
        Vector2 mouse = Input.mousePosition;
        Vector2 dir = mouse - centerScreen;

        float dist = dir.magnitude;
        if (dist < deadzone)
        {
            if (highlighted != -1)
            {
                highlighted = -1;
                SetAllHighlights(-1);
                if (debugHighlightSpam) Log("Highlight: deadzone -> none");
            }
            return;
        }

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg; // -180..180
        angle = (angle + 360f + 90f) % 360f; // 0 at up

        int count = bag.Clubs.Count;
        float step = 360f / count;
        int idx = Mathf.FloorToInt(angle / step);
        idx = Mathf.Clamp(idx, 0, count - 1);

        if (idx != highlighted)
        {
            highlighted = idx;
            SetAllHighlights(idx);
            if (debugHighlightSpam) Log($"Highlight: idx={idx} angle={angle:F1} count={count}");
        }
    }

    private void ConfirmSelection()
    {
        if (bag == null)
        {
            Log("ConfirmSelection(): bag NULL");
            return;
        }

        if (localPlayerNO == null)
        {
            Log("ConfirmSelection(): localPlayerNO NULL");
            return;
        }

        if (highlighted < 0 || highlighted >= bag.Clubs.Count)
        {
            Log($"ConfirmSelection(): no selection (highlighted={highlighted}, bagCount={bag.Clubs.Count})");
            return;
        }

        int clubId = bag.Clubs[highlighted];

        Log($"ConfirmSelection(): EQUIP request idx={highlighted} clubId={clubId} playerNetId={localPlayerNO.NetworkObjectId}");

        bag.EquipFromBagServerRpc(localPlayerNO.NetworkObjectId, highlighted);
    }

    private void SetAllHighlights(int selected)
    {
        for (int i = 0; i < slots.Count; i++)
            if (slots[i].gameObject.activeSelf)
                slots[i].SetHighlight(i == selected);
    }

    private void ShowImmediate()
    {
        if (!group) return;

        group.alpha = 1;
        group.blocksRaycasts = true;
        group.interactable = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        DisablePlayerLook(true);
    }

    private void HideImmediate()
    {
        if (!group) return;

        group.alpha = 0;
        group.blocksRaycasts = false;
        group.interactable = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        DisablePlayerLook(false);
    }

    private void DebugDrawMouseDir()
    {
        if (!center) return;

        Vector2 centerScreen = RectTransformUtility.WorldToScreenPoint(null, center.position);
        Vector2 mouse = Input.mousePosition;
        Vector2 dir = (mouse - centerScreen);

        // Draw a little ray in screen-space-ish using Debug.DrawRay in world (approx):
        // We'll just log distance/angle instead of true screen ray.
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (debugHighlightSpam) Log($"MouseDir: dist={dir.magnitude:F0} angle={angle:F1}");
    }

    private void Log(string msg)
    {
        if (!debugLogs) return;
        Debug.Log($"[RadialClubUI] {msg}", this);
    }
    
    private void DisablePlayerLook(bool uiOpen)
    {
        if (NetworkManager.Singleton == null) return;

        var playerNO = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (!playerNO) return;

        var player = playerNO.GetComponent<NetworkRigidbodyPlayer>();
        if (!player) return;

        // Stop yaw → body rotation + server yaw
        player.SetYawEnabled(!uiOpen);

        // Stop camera input
        if (player.LocalRig != null)
            player.LocalRig.SetLookEnabled(!uiOpen);
    }
}
