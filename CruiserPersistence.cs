using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;

namespace CruiserScan;

internal static class CruiserPersistence
{
    private const string CruiserObjectName = "CompanyCruiser(Clone)";
    private const string ShipObjectPath = "Environment/HangarShip";

    private static readonly HashSet<ulong> PersistedShipItems = [];

    public static void MarkCurrentShipItemsAsPersisted()
    {
        PersistedShipItems.Clear();

        HashSet<GrabbableObject> foundItems = [];

        AddDescendantGrabbables(
            GameObject.Find(ShipObjectPath),
            foundItems
        );

        AddDescendantGrabbables(
            GameObject.Find(CruiserObjectName),
            foundItems
        );

        foreach (GrabbableObject grabbable in foundItems)
        {
            if (grabbable.itemProperties == null || !grabbable.itemProperties.isScrap)
                continue;

            if (grabbable.NetworkObject == null || !grabbable.NetworkObject.IsSpawned)
                continue;

            PersistedShipItems.Add(grabbable.NetworkObjectId);
        }
    }

    public static bool IsPersisted(GrabbableObject grabbable)
    {
        if (grabbable.NetworkObject == null || !grabbable.NetworkObject.IsSpawned)
            return false;

        return PersistedShipItems.Contains(grabbable.NetworkObjectId);
    }

    public static void Clear()
    {
        PersistedShipItems.Clear();
    }

    private static void AddDescendantGrabbables(
        GameObject? root,
        HashSet<GrabbableObject> foundItems)
    {
        if (root == null)
            return;

        foreach (GrabbableObject grabbable in root.GetComponentsInChildren<GrabbableObject>(true))
        {
            if (grabbable != null)
                foundItems.Add(grabbable);
        }
    }
}

[HarmonyPatch(typeof(RoundManager))]
internal static class RoundManagerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch("DespawnPropsAtEndOfRound")]
    private static void AfterDespawnPropsAtEndOfRound()
    {
        CruiserPersistence.MarkCurrentShipItemsAsPersisted();
    }
}

[HarmonyPatch(typeof(GameNetworkManager))]
internal static class GameNetworkManagerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch("StartDisconnect")]
    private static void AfterStartDisconnect()
    {
        CruiserPersistence.Clear();
    }
}