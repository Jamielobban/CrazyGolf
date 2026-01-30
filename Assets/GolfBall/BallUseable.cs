using UnityEngine;

public class BallUseable : MonoBehaviour, IUseable
{
    [SerializeField] private NetworkGolfBallState ball;

    public int Priority => 10;
    public string UsePrompt => ball ? ball.GetUsePrompt() : "";

    private void Awake()
    {
        if (!ball) ball = GetComponent<NetworkGolfBallState>();
    }

    public bool Use(Interactor who)
    {
        if (!ball) return false;

        // Always ask the server; server validates mode/state
        ball.RequestPickupServerRpc(who.NetworkObjectId);
        return true;
    }
}
