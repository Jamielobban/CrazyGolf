using UnityEngine;

public class WorldClubItemSource : MonoBehaviour, IInventoryItemSource
{
    [SerializeField] private WorldClub club;

    public int Priority => 10;
    public string TakePrompt => "Pick up club";

    private void Awake()
    {
        if (!club) club = GetComponentInParent<WorldClub>();
    }

    public bool Take(Interactor who)
    {
        if (club == null) return false;

        var equipment = who.GetComponent<NetworkClubEquipment>();
        if (!equipment) return false;

        equipment.TryPickupWorldClub(club);
        return true;
    }
}
