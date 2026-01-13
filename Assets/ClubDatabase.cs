using UnityEngine;

[CreateAssetMenu(menuName = "Golf/Club Database")]
public class ClubDatabase : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public int id;
        public ClubData data;
    }

    [SerializeField] private Entry[] entries;

    public ClubData Get(int id)
    {
        if (entries == null) return null;
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].id == id)
                return entries[i].data;
        return null;
    }
}
