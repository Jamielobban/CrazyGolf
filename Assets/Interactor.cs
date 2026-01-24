using Unity.Netcode;
using UnityEngine;

public class Interactor : NetworkBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private float interactDist = 3f;
    [SerializeField] private LayerMask interactMask;

    public RaycastHit CurrentHit { get; private set; }
    public IUseable CurrentUseable { get; private set; }

    private void Update()
    {
        if (!IsOwner) return;

        if (!cam) cam = Camera.main;
        if (!cam) return;

        CurrentUseable = null;
        CurrentHit = default;

        if (Physics.Raycast(cam.transform.position, cam.transform.forward,
            out var hit, interactDist, interactMask, QueryTriggerInteraction.Ignore))
        {
            CurrentHit = hit;
            CurrentUseable = FindBestUseable(hit.collider);
        }

        Debug.DrawRay(cam.transform.position, cam.transform.forward * interactDist,
            CurrentUseable != null ? Color.green : Color.red);

        if (CurrentUseable != null)
            Debug.Log($"[LOOK] {((MonoBehaviour)CurrentUseable).name} - {CurrentUseable.UsePrompt} (tap E)");
    }

    public void TryTapUse()
    {
        if (!IsOwner) return;

        if (CurrentUseable == null)
        {
            Debug.Log("[E TAP] nothing useable");
            return;
        }

        bool ok = CurrentUseable.Use(this);
        if (!ok)
            Debug.Log($"[E TAP] Use not handled by {((MonoBehaviour)CurrentUseable).name}");
    }

    private static IUseable FindBestUseable(Collider c)
    {
        if (!c) return null;

        var list = c.GetComponentsInParent<MonoBehaviour>(true);
        IUseable best = null;
        int bestPrio = int.MinValue;

        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] is not IUseable u) continue;
            if (u.Priority > bestPrio)
            {
                bestPrio = u.Priority;
                best = u;
            }
        }
        return best;
    }

    public void TryHoldInteract()
    {
        if (!IsOwner) return;
        if (CurrentHit.collider == null) return;
        
        // 1) Bag physical hold (if you’re doing that here)
        var bagCarry = CurrentHit.collider.GetComponent<NetworkGolfBagCarry>();
        if (bagCarry != null)
        {
            var hands = GetComponent<PlayerHeldController>(); // or BagCarryActions
            if (hands != null && hands.TryTakeFromLook(CurrentHit))
                return;
        }

        // 2) Take inventory item source (clubs etc.)
        var src = FindBestItemSource(CurrentHit.collider);
        if (src != null)
        {
            bool ok = src.Take(this);
            if (!ok) Debug.Log("[E HOLD] item source didn't take");
            return;
        }

        Debug.Log("[E HOLD] nothing holdable/takeable");
    }

    private static IInventoryItemSource FindBestItemSource(Collider c)
    {
        var list = c.GetComponentsInParent<MonoBehaviour>(true);
        IInventoryItemSource best = null;
        int bestPrio = int.MinValue;

        foreach (var mb in list)
        {
            if (mb is not IInventoryItemSource s) continue;
            if (s.Priority > bestPrio)
            {
                bestPrio = s.Priority;
                best = s;
            }
        }
        return best;
    }
}
