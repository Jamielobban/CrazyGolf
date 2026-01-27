using UnityEngine;
using Unity.Netcode;

public class GripInertiaFollower : MonoBehaviour
{
    public enum GripRotationMode { FollowCamera, FollowBodyAnchor }

    [Header("Find by tag (under player)")]
    [SerializeField] private string bodyAnchorTag = "BodyAnchorPivot";

    [Header("Mode")]
    [SerializeField] private GripRotationMode rotationMode = GripRotationMode.FollowCamera;

    [Header("Position Inertia (world)")]
    [SerializeField] private float posSpring = 70f;
    [SerializeField] private float posDamping = 14f;
    [SerializeField] private float maxSpeed = 8f;

    [Header("Rotation Inertia (world)")]
    [SerializeField] private float rotLerp = 10f;
    [SerializeField] private float pitchClamp = 75f;

    [SerializeField] private Transform bodyAnchor;

    private ulong followClientId = ulong.MaxValue;
    private Vector3 vel;

    private NetworkObject cachedPlayerNO;

    private bool canUseLocalCamera;

    private NetworkRigidbodyPlayer ownerPlayer;

    /// <summary>
    /// Called exactly once by NetworkHandRig when the logical owner is known.
    /// </summary>
    public void BindToPlayer(ulong clientId)
    {
        followClientId = clientId;
        bodyAnchor = null;
        cachedPlayerNO = null; // if you added caching
        ownerPlayer = null;

        var nm = NetworkManager.Singleton;
        canUseLocalCamera = nm != null && nm.IsConnectedClient && nm.LocalClientId == followClientId;

        TryResolveOwnerPlayer();
        TryResolveBodyAnchor();

        if (bodyAnchor)
            SnapNow();

        //Debug.Log(
           // $"[GripFollower] Bound to client {followClientId}, " +
            //$"anchor={(bodyAnchor ? bodyAnchor.name : "NULL")}"
        //);
        //Debug.Log($"[GripFollower] Bound followClientId={followClientId} local={nm?.LocalClientId} canUseLocalCamera={canUseLocalCamera}");
    }

   private void TryResolveOwnerPlayer()
    {
        if (ownerPlayer != null) return;

        var nm = NetworkManager.Singleton;
        if (!nm) return;

        foreach (var no in nm.SpawnManager.SpawnedObjectsList)
        {
            if (!no || !no.IsPlayerObject) continue;
            if (no.OwnerClientId != followClientId) continue;

            ownerPlayer = no.GetComponent<NetworkRigidbodyPlayer>();
            return;
        }
    }

    private void TryResolveBodyAnchor()
    {
        if (bodyAnchor) return;
        if (followClientId == ulong.MaxValue) return;

        var nm = NetworkManager.Singleton;
        if (!nm) return;

        if (!cachedPlayerNO)
        {
            foreach (var no in nm.SpawnManager.SpawnedObjectsList)
            {
                if (no && no.IsPlayerObject && no.OwnerClientId == followClientId)
                {
                    cachedPlayerNO = no;
                    break;
                }
            }
        }

        if (!cachedPlayerNO) return;

        foreach (var t in cachedPlayerNO.GetComponentsInChildren<Transform>(true))
        {
            if (t.CompareTag(bodyAnchorTag))
            {
                bodyAnchor = t;
                return;
            }
        }
    }



    private void FixedUpdate()
    {
        if (!bodyAnchor)
        {
            TryResolveBodyAnchor();
            if (!bodyAnchor) return;
        }

       if (ownerPlayer == null)
        {
            TryResolveOwnerPlayer();
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // ---- POSITION ----
        Vector3 targetPos = bodyAnchor.position;

        Vector3 x = transform.position;
        Vector3 v = vel;

        Vector3 a = posSpring * (targetPos - x) - posDamping * v;
        v += a * dt;

        float spd = v.magnitude;
        if (spd > maxSpeed)
            v *= (maxSpeed / spd);

        x += v * dt;

        transform.position = x;
        vel = v;

        // ---- ROTATION ----
        Quaternion targetRot;

        if (rotationMode == GripRotationMode.FollowCamera)
        {
            if (canUseLocalCamera)
            {
                var cam = Camera.main;
                if (!cam) return;

                Vector3 e = cam.transform.eulerAngles;

                float pitch = e.x;
                if (pitch > 180f) pitch -= 360f;
                pitch = Mathf.Clamp(pitch, -pitchClamp, pitchClamp);

                float yaw = e.y;

                targetRot = Quaternion.Euler(pitch, yaw, 0f);
            }
            else
            {
                if (ownerPlayer == null)
                    TryResolveOwnerPlayer();

                if (ownerPlayer != null)
                    targetRot = ownerPlayer.NetAimRotation.Value;
                else
                    targetRot = bodyAnchor.rotation; // safe fallback
            }
        }
        else
        {
            targetRot = bodyAnchor.rotation;
        }

        float t = 1f - Mathf.Exp(-rotLerp * dt);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
    }

    public void SnapNow()
    {
        if (!bodyAnchor) return;

        transform.position = bodyAnchor.position;
        vel = Vector3.zero;

       if (rotationMode == GripRotationMode.FollowCamera)
        {
            Quaternion targetRot;

            if (canUseLocalCamera)
            {
                var cam = Camera.main;
                if (!cam) return;

                Vector3 e = cam.transform.eulerAngles;

                float pitch = e.x;
                if (pitch > 180f) pitch -= 360f;
                pitch = Mathf.Clamp(pitch, -pitchClamp, pitchClamp);

                float yaw = e.y;

                targetRot = Quaternion.Euler(pitch, yaw, 0f);
            }
            else
            {
                if (ownerPlayer == null) TryResolveOwnerPlayer();
                targetRot = (ownerPlayer != null) ? ownerPlayer.NetAimRotation.Value : bodyAnchor.rotation;
            }

            transform.rotation = targetRot;
        }
        else
        {
            transform.rotation = bodyAnchor.rotation;
        }
    }

    public void SetFollowCamera()
    {
        rotationMode = GripRotationMode.FollowCamera;
        SnapNow();
    }

    public void SetFollowBodyAnchor()
    {
        rotationMode = GripRotationMode.FollowBodyAnchor;
        SnapNow();
    }
}
