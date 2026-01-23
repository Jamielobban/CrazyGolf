using UnityEngine;

public class PlayerBagAnchor : MonoBehaviour
{
    [SerializeField] private Transform anchor;
    public Transform Anchor => anchor;

    private void Awake()
    {
        if (!anchor)
        {
            // Optional fallback: find child named "BagAnchor"
            var t = transform.Find("BagAnchor");
            if (t) anchor = t;
        }
    }
}
