using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CruiserScan;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class CruiserScan : BaseUnityPlugin
{
    public static CruiserScan Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger { get; private set; } = null!;
    internal static Harmony? Harmony { get; set; }

    internal static ConfigEntry<float> OffsetX { get; private set; } = null!;
    internal static ConfigEntry<float> OffsetY { get; private set; } = null!;
    internal static ConfigEntry<float> FontSize { get; private set; } = null!;
    internal static ConfigEntry<float> DisplayDuration { get; private set; } = null!;
    
    internal static ConfigEntry<bool> ExcludeKnifeItem { get; private set; } = null!;
    internal static ConfigEntry<bool> ExcludeShotgunItem { get; private set; } = null!;
    internal static ConfigEntry<bool> ExcludePersistedItems { get; private set; } = null!;

    private void Awake()
    {
        Logger = base.Logger;
        Instance = this;

        OffsetX = Config.Bind("Display", "OffsetX", 20f, "Extra horizontal offset from the default position.");
        OffsetY = Config.Bind("Display", "OffsetY", 50f, "Extra vertical offset from the default position.");
        FontSize = Config.Bind("Display", "FontSize", 18f, "Font size for the text.");
        DisplayDuration = Config.Bind("Display", "DisplayDuration", 3f, "How long the text stays visible for after scanning.");
        
        ExcludeKnifeItem = Config.Bind("Filtering", "ExcludeKnifeItem", true, "Whether to exclude butcher knife from the scan.");
        ExcludeShotgunItem = Config.Bind("Filtering", "ExcludeShotgunItem", true, "Whether to exclude shotgun from the scan.");
        ExcludePersistedItems = Config.Bind("Filtering", "ExcludePersistedItems", true, "Whether to exclude items that were picked up in previous rounds.");

        Patch();

        Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
    }

    internal static void Patch()
    {
        Harmony ??= new Harmony(MyPluginInfo.PLUGIN_GUID);

        Logger.LogDebug("Patching...");
        Harmony.PatchAll();
        Logger.LogDebug("Finished patching!");
    }

    internal static void Unpatch()
    {
        Logger.LogDebug("Unpatching...");
        Harmony?.UnpatchSelf();
        Logger.LogDebug("Finished unpatching!");
    }
}

[HarmonyPatch(typeof(HUDManager))]
internal static class HUDManagerPatch
{
    private static FieldInfo? _playerPingingScanField;
    private static float _pingBeforeScan;
    private static bool _candidateScanInput;

    [HarmonyPostfix]
    [HarmonyPatch("Awake")]
    private static void OnHudAwake()
    {
        _playerPingingScanField = AccessTools.Field(
            typeof(HUDManager),
            "playerPingingScan"
        );

        if (_playerPingingScanField == null)
            CruiserScan.Logger.LogError("Failed to find HUDManager.playerPingingScan.");
    }

    [HarmonyPrefix]
    [HarmonyPatch("PingScan_performed")]
    private static void BeforePingScan(
        HUDManager __instance,
        in InputAction.CallbackContext context
    )
    {
        _candidateScanInput = false;
        _pingBeforeScan = 0f;

        if (_playerPingingScanField == null)
            return;

        if (GameNetworkManager.Instance.localPlayerController == null)
            return;

        if (!context.performed)
            return;

        object? value = _playerPingingScanField.GetValue(__instance);

        if (value is not float ping)
            return;

        _pingBeforeScan = ping;
        _candidateScanInput = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch("PingScan_performed")]
    private static void AfterPingScan(HUDManager __instance)
    {
        if (!_candidateScanInput || _playerPingingScanField == null)
            return;

        object? value = _playerPingingScanField.GetValue(__instance);

        if (value is not float pingAfterScan)
            return;

        bool vanillaAcceptedScan =
            _pingBeforeScan <= -1f &&
            pingAfterScan > _pingBeforeScan;

        if (!vanillaAcceptedScan)
            return;

        CruiserStats stats = CruiserValueCalculator.CalculateStats();
        CruiserHud.Show(stats);
    }
}

internal readonly struct CruiserStats(int totalValue, int itemCount)
{
    public int TotalValue { get; } = totalValue;
    public int ItemCount { get; } = itemCount;
}

internal static class CruiserValueCalculator
{
    private const string CruiserPath = "CompanyCruiser(Clone)";
    private const string KitchenKnifeName = "Kitchen knife";
    private const string ShotgunName = "Double-barrel";

    public static CruiserStats CalculateStats()
    {
        GameObject cruiser = GameObject.Find(CruiserPath);

        if (cruiser == null)
            return new CruiserStats(0, 0);

        ScanNodeProperties[] scrapNodes = [.. cruiser
            .GetComponentsInChildren<GrabbableObject>(includeInactive: true)
            .Where(IsCountableGrabbable)
            .Select(GetScanNode)
            .Where(IsCountableScrapNode)
            .Select(node => node!)];

        int totalValue = scrapNodes.Sum(node => node.scrapValue);
        int itemCount = scrapNodes.Length;
        
        return new CruiserStats(totalValue, itemCount);
    }

    private static ScanNodeProperties? GetScanNode(GrabbableObject grabbable)
    {
        return grabbable.GetComponentInChildren<ScanNodeProperties>(includeInactive: true);
    }

    private static bool IsCountableGrabbable(GrabbableObject grabbable)
    {
        if (CruiserScan.ExcludePersistedItems.Value &&
            (grabbable.scrapPersistedThroughRounds || CruiserPersistence.IsPersisted(grabbable)))
        {
            return false;
        }

        if (grabbable is GiftBoxItem giftBoxItem && giftBoxItem.hasUsedGift)
            return false;

        return true;
    }

    private static bool IsCountableScrapNode(ScanNodeProperties? node)
    {
        if (node == null)
            return false;

        if (node.scrapValue <= 0)
            return false;

        if (CruiserScan.ExcludeKnifeItem.Value && node.headerText == KitchenKnifeName)
            return false;

        if (CruiserScan.ExcludeShotgunItem.Value && node.headerText == ShotgunName)
            return false;

        return true;
    }
}