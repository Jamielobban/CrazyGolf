using UnityEngine;

public class EquippedClubFollowGrip : MonoBehaviour
{
    Transform gripPivot;

    public void Bind(Transform pivot)
    {
        gripPivot = pivot;
    }

    void LateUpdate()
    {
        if (!gripPivot) return;

        // Hard follow (you can add smoothing later)
        transform.position = gripPivot.position;
        transform.rotation = gripPivot.rotation;
    }
}
