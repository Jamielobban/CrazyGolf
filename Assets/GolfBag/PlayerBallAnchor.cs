using UnityEngine;

public class PlayerBallAnchor : MonoBehaviour
{
    [SerializeField] private Transform anchor;
    public Transform Anchor => anchor ? anchor : transform;
}
