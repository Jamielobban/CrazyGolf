using UnityEngine;
using Unity.Netcode;

public class NetworkUI : MonoBehaviour
{
    public void StartHost()
    {
        NetworkManager.Singleton.StartHost();

        var match = FindFirstObjectByType<GolfMatchNet>();
        if (match != null)
            match.RequestStartHole();
    }

    public void StartClient()
    {
        NetworkManager.Singleton.StartClient();
        //Debug.Log("Started CLIENT");
    }

    public void StartServer()
    {
        NetworkManager.Singleton.StartServer();
        //Debug.Log("Started SERVER");
    }
}
