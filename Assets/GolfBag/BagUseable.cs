using UnityEngine;

public class BagUseable : MonoBehaviour, IUseable
{
    [SerializeField] private NetworkGolfBag bagInv;

    public int Priority => 5;
    public string UsePrompt => "Open bag";

    private void Awake()
    {
        if (!bagInv) bagInv = GetComponent<NetworkGolfBag>();
    }

    public bool Use(Interactor who)
    {
        if (bagInv == null) return false;

        //BagUI.Instance.Open(bagInv);
        return true;
    }
}
