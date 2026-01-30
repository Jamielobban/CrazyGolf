using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class NetworkUI : MonoBehaviour
{
    public void StartHost()
    {
        NetworkManager.Singleton.StartHost();

        var match = FindFirstObjectByType<GolfMatchNet>();
        StartCoroutine(StartHoleWhenReady());
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

    private IEnumerator StartHoleWhenReady()
    {
        var nm = NetworkManager.Singleton;

        while (nm == null || !nm.IsServer)
            yield return null;

        // Wait for host player object
        while (nm.LocalClient == null || nm.LocalClient.PlayerObject == null)
            yield return null;

        var match = FindFirstObjectByType<GolfMatchNet>();
        if (match != null)
            match.RequestStartHole();
    }
}
