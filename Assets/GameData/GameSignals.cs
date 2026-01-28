using System;
using UnityEngine;

public static class GameSignals
{
    // clientId, strokes
    public static event Action<ulong, int> StrokeCountChanged;
    public static void RaiseStrokeCountChanged(ulong clientId, int strokes)
        => StrokeCountChanged?.Invoke(clientId, strokes);

    // clientId, strokes, cupWorldPos
    public static event Action<ulong, int, Vector3> BallHoled;
    public static void RaiseBallHoled(ulong clientId, int strokes, Vector3 cupPos)
        => BallHoled?.Invoke(clientId, strokes, cupPos);

    public static event System.Action<ulong, Vector3, float> BallHit; // clientId, dir, impulse
    public static void RaiseBallHit(ulong clientId, Vector3 dir, float impulse)
        => BallHit?.Invoke(clientId, dir, impulse);

    public static event System.Action<ulong, int> ClubEquipped; // clientId, clubId (0 = none)
    public static void RaiseClubEquipped(ulong clientId, int clubId)
        => ClubEquipped?.Invoke(clientId, clubId);

    public static event System.Action BagOpened;
    public static event System.Action BagClosed;


    public static void RaiseBagOpened() => BagOpened?.Invoke();
    public static void RaiseBagClosed() => BagClosed?.Invoke();
}
