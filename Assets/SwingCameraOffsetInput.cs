using UnityEngine;
using Unity.Netcode;
using Unity.Cinemachine;

public class SwingCameraOffsetInput : NetworkBehaviour
{
    [SerializeField] private GolfStanceController stance;

    [Header("Cinemachine")]
    [SerializeField] private CinemachineCamera swingCam; // your swing Cinemachine Camera (CM3)

    [Header("Offset X (peek left/right)")]
    [SerializeField] private float offsetSpeed = 1.2f;   // units per second
    [SerializeField] private float maxAbsOffsetX = 1.0f;  // clamp
    [SerializeField] private float recenterSpeed = 2.0f;  // units per second (0 = no recenter)
    [SerializeField] private KeyCode leftKey = KeyCode.Q;
    [SerializeField] private KeyCode rightKey = KeyCode.E;

    private CinemachineRotationComposer composer;
    private float baseOffsetX;

    private void Awake()
    {
        if (!stance) stance = GetComponent<GolfStanceController>();
    }

    private void Start()
    {
        if (swingCam != null)
        {
            composer = swingCam.GetComponent<CinemachineRotationComposer>();
            if (composer != null)
                baseOffsetX = composer.TargetOffset.x;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (!stance || stance.stance != GolfStanceController.Stance.Swing) return;
        if (!composer) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float input = 0f;
        if (Input.GetKey(leftKey)) input -= 1f;
        if (Input.GetKey(rightKey)) input += 1f;

        Vector3 off = composer.TargetOffset;

        if (Mathf.Abs(input) > 0.001f)
        {
            off.x += input * offsetSpeed * dt;
            off.x = Mathf.Clamp(off.x, baseOffsetX - maxAbsOffsetX, baseOffsetX + maxAbsOffsetX);
        }
        else if (recenterSpeed > 0f)
        {
            off.x = Mathf.MoveTowards(off.x, baseOffsetX, recenterSpeed * dt);
        }

        composer.TargetOffset = off;
    }
}
