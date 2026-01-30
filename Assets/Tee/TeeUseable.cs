using UnityEngine;

public class TeeUseable : MonoBehaviour, IUseable
{
    [SerializeField] private NetworkBallTeeSocket tee;

    public int Priority => 6;
    public string UsePrompt => "Place ball";

    private void Awake()
    {
        if (!tee) tee = GetComponent<NetworkBallTeeSocket>();
    }

    public bool Use(Interactor who)
    {
        if (!tee) return false;

        // Find the ball I'm holding (local lookup; server still validates)
        ulong myClientId = who.OwnerClientId;

        if (!NetworkGolfBallState.TryGetHeldBall(myClientId, out var heldBall))
            return false;

        heldBall.RequestPlaceOnTeeServerRpc(tee.NetworkObjectId, who.NetworkObjectId);
        return true;
    }
}
