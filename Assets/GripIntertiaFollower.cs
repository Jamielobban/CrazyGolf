using Unity.Netcode;
using UnityEngine;

public class GripInertiaFollower : NetworkBehaviour
{
    public enum GripRotationMode { FollowCamera, FollowBodyAnchor }

    [Header("Find by tag")]
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
    private Vector3 vel;

    public override void OnNetworkSpawn()
    {
        // Only owner drives the motion; NetworkTransform replicates it.
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        ResolveBodyAnchor();
        SnapNow();
    }

    void ResolveBodyAnchor()
    {
        bodyAnchor = null;

        if (!NetworkManager.Singleton)
            return;

        // HandRig is owned by a player. Find THAT player's NetworkObject.
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(OwnerClientId, out var client) ||
            client == null || client.PlayerObject == null)
        {
            // Player object might not exist yet on this frame (spawn order)
            return;
        }

        var playerRoot = client.PlayerObject.transform;

        // Find the anchor under THAT player's hierarchy by tag
        var all = playerRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].CompareTag(bodyAnchorTag))
            {
                bodyAnchor = all[i];
                break;
            }
        }

        if (!bodyAnchor)
            Debug.LogError($"[GripInertiaFollower] No '{bodyAnchorTag}' found under owner player {OwnerClientId}.");
    }

    void FixedUpdate()
    {
        if (!IsOwner) return;
        if (!bodyAnchor)
        {
            // try again if it spawned later
            ResolveBodyAnchor();
            if (!bodyAnchor) return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // ---- POSITION (world) ----
        Vector3 targetPos = bodyAnchor.position;

        Vector3 x = transform.position;
        Vector3 v = vel;

        Vector3 a = posSpring * (targetPos - x) - posDamping * v;
        v += a * dt;

        float spd = v.magnitude;
        if (spd > maxSpeed) v *= (maxSpeed / spd);

        x += v * dt;

        transform.position = x;
        vel = v;

        // ---- ROTATION (world) ----
        Quaternion targetRot;

        if (rotationMode == GripRotationMode.FollowCamera)
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
            var cam = Camera.main;
            if (!cam) return;

            Vector3 e = cam.transform.eulerAngles;

            float pitch = e.x;
            if (pitch > 180f) pitch -= 360f;
            pitch = Mathf.Clamp(pitch, -pitchClamp, pitchClamp);

            float yaw = e.y;
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        else
        {
            transform.rotation = bodyAnchor.rotation;
        }
    }

    public void SetFollowCamera() { rotationMode = GripRotationMode.FollowCamera; SnapNow(); }
    public void SetFollowBodyAnchor() { rotationMode = GripRotationMode.FollowBodyAnchor; SnapNow(); }
}
