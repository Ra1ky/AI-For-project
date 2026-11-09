using System.Collections.Generic;
using UnityEngine;

public static class AlertSystem
{
    private static List<GuardAI> registeredGuards = new List<GuardAI>();

    public static void RegisterGuard(GuardAI guard)
    {
        if (!registeredGuards.Contains(guard))
            registeredGuards.Add(guard);
    }

    public static void UnregisterGuard(GuardAI guard)
    {
        registeredGuards.Remove(guard);
    }

    // broadcast alert in radius / map-wide
    public static void BroadcastAlert(Vector3 alertPosition, float radius, GuardAI emitter)
    {
        foreach (var guard in registeredGuards)
        {
            if (guard == emitter) continue;

            float distance = Vector3.Distance(guard.transform.position, alertPosition);
            if (distance <= radius)
            {
                guard.ReceiveGlobalAlert(alertPosition);
            }
        }
    }
}