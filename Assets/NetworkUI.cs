using UnityEngine;
using Unity.Netcode;

public class NetworkUI : MonoBehaviour
{
    public void StartHost()
    {
        NetworkManager.Singleton.StartHost();
        Debug.Log("Started HOST");
    }

    public void StartClient()
    {
        NetworkManager.Singleton.StartClient();
        Debug.Log("Started CLIENT");
    }

    public void StartServer()
    {
        NetworkManager.Singleton.StartServer();
        Debug.Log("Started SERVER");
    }
}
