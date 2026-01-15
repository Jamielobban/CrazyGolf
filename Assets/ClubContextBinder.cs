using UnityEngine;
using Unity.Netcode;

public class ClubContextBinder : MonoBehaviour
{
    private ClubBallContactLogger[] loggers;
    private ClubHeadVelocity[] velocities;

    public Transform clubhead;

    private void Awake()
    {
        loggers = GetComponentsInChildren<ClubBallContactLogger>(true);
        velocities = GetComponentsInChildren<ClubHeadVelocity>(true);
    }

    public void Bind(GolferContextLink link)
    {
        foreach (var l in loggers)
            if (l) l.BindContext(link);

        var ownerNb = link != null ? (NetworkBehaviour)link.golfer : null;

        foreach (var v in velocities)
            if (v) v.BindOwner(ownerNb);
    }
}
