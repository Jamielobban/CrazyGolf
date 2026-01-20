// DebugDropThrowKeys.cs
// Put this on the PLAYER (same object as NetworkClubEquipment).
// G = drop, T = throw.

using Unity.Netcode;
using UnityEngine;

public class DebugDropThrowKeys : NetworkBehaviour
{
     [SerializeField] private NetworkClubEquipment equip;

    [Header("Keys")]
    [SerializeField] private KeyCode dropKey = KeyCode.G;
    [SerializeField] private KeyCode throwKey = KeyCode.T;

    [Header("Charge")]
    [SerializeField] private float chargeTimeToFull = 0.75f; // seconds to reach 1.0
    [SerializeField] private AnimationCurve chargeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private bool charging;
    private float chargeT;

    private void Awake()
    {
        if (!equip) equip = GetComponent<NetworkClubEquipment>();
    }

    private void Update()
    {
        if (!IsOwner || equip == null) return;

        if (Input.GetKeyDown(dropKey))
            equip.DropEquipped();

        if (Input.GetKeyDown(throwKey))
        {
            charging = true;
            chargeT = 0f;
        }

        if (charging && Input.GetKey(throwKey))
        {
            chargeT += Time.deltaTime;
        }

        if (charging && Input.GetKeyUp(throwKey))
        {
            charging = false;

            float t01 = (chargeTimeToFull <= 0.0001f) ? 1f : Mathf.Clamp01(chargeT / chargeTimeToFull);
            float charge01 = chargeCurve != null ? Mathf.Clamp01(chargeCurve.Evaluate(t01)) : t01;

            equip.ThrowEquippedCharged(charge01);
        }
    }
}
