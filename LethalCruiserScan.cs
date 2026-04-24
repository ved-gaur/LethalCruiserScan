using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LethalCruiserScan;

[BepInPlugin(ModGuid, ModName, ModVersion)]
public sealed class LethalCruiserScanPlugin : BaseUnityPlugin
{
    public const string ModGuid = "ved-gaur.lethalcruiserscan";
    public const string ModName = "LethalCruiserScan";
    public const string ModVersion = "1.0.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    internal static ConfigEntry<float> OffsetX { get; private set; } = null!;
    internal static ConfigEntry<float> OffsetY { get; private set; } = null!;
    internal static ConfigEntry<float> FontSize { get; private set; } = null!;
    internal static ConfigEntry<float> DisplayDuration { get; private set; } = null!;

    private readonly Harmony _harmony = new(ModGuid);

    private void Awake()
    {
        Log = Logger;

        OffsetX = Config.Bind(
            "Display",
            "OffsetX",
            0f,
            "Extra horizontal offset from the default position."
        );

        OffsetY = Config.Bind(
            "Display",
            "OffsetY",
            25f,
            "Extra vertical offset from the default position."
        );

        FontSize = Config.Bind(
            "Display",
            "FontSize",
            20f,
            "Font size for the text."
        );

        DisplayDuration = Config.Bind(
            "Display",
            "DisplayDuration",
            3f,
            "How long the text stays visible for after scanning."
        );

        _harmony.PatchAll();

        Log.LogInfo($"{ModName} {ModVersion} loaded.");
    }
}

[HarmonyPatch]
internal static class HUDManagerPatch
{
    private static MethodInfo? _canPlayerScanMethod;
    private static FieldInfo? _playerPingingScanField;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HUDManager), "Awake")]
    private static void OnHudAwake(HUDManager __instance)
    {
        _canPlayerScanMethod = typeof(HUDManager).GetMethod(
            "CanPlayerScan",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        _playerPingingScanField = typeof(HUDManager).GetField(
            "playerPingingScan",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(HUDManager), "PingScan_performed")]
    private static void OnScan(HUDManager __instance, InputAction.CallbackContext context)
    {
        if (!IsValidScan(__instance, context))
            return;

        CruiserStats stats = CruiserValueCalculator.CalculateStats();

        CruiserHud.Show(stats);
    }

    private static bool IsValidScan(HUDManager hudManager, InputAction.CallbackContext context)
    {
        if (GameNetworkManager.Instance.localPlayerController == null)
            return false;

        if (!context.performed)
            return false;

        if (_canPlayerScanMethod == null || _playerPingingScanField == null)
            return false;

        bool canPlayerScan = (bool)_canPlayerScanMethod.Invoke(hudManager, null);

        if (!canPlayerScan)
            return false;

        float playerPingingScan = (float)_playerPingingScanField.GetValue(hudManager);

        if (playerPingingScan > -1f)
            return false;

        return true;
    }
}

internal readonly struct CruiserStats
{
    public int TotalValue { get; }
    public int ItemCount { get; }

    public CruiserStats(int totalValue, int itemCount)
    {
        TotalValue = totalValue;
        ItemCount = itemCount;
    }
}

internal static class CruiserValueCalculator
{
    private const string CruiserPath = "CompanyCruiser(Clone)";
    private const string ClipboardName = "ClipboardManual";
    private const string StickyNoteName = "StickyNoteItem";

    public static CruiserStats CalculateStats()
    {
        GameObject cruiser = GameObject.Find(CruiserPath);

        if (cruiser == null)
            return new CruiserStats(0, 0);

        ScanNodeProperties[] scrapNodes = cruiser
            .GetComponentsInChildren<GrabbableObject>(includeInactive: true)
            .Select(GetScanNode)
            .Where(IsCountableScrapNode)
            .Select(node => node!)
            .ToArray();

        int totalValue = scrapNodes.Sum(node => node.scrapValue);
        int itemCount = scrapNodes.Length;

        return new CruiserStats(totalValue, itemCount);
    }

    private static ScanNodeProperties? GetScanNode(GrabbableObject grabbable)
    {
        return grabbable.GetComponentInChildren<ScanNodeProperties>(includeInactive: true);
    }

    private static bool IsCountableScrapNode(ScanNodeProperties? node)
    {
        if (node == null)
            return false;

        if (node.scrapValue <= 0)
            return false;

        if (node.headerText == ClipboardName || node.headerText == StickyNoteName)
            return false;

        return true;
    }
}

internal static class CruiserHud
{
    private const string VanillaValueCounterPath =
        "/Systems/UI/Canvas/IngamePlayerHUD/BottomMiddle/ValueCounter";

    private const string BackdropImageName = "Text (TMP) (1)";

    private static Vector3 _baseLocalPosition;
    private static GameObject? _counterObject;
    private static TextMeshProUGUI? _text;  
    private static float _displayTimeLeft;

    public static void Show(CruiserStats stats)
    {
        EnsureCreated();
        ApplyPosition();

        if (_counterObject == null || _text == null)
            return;

        _text.fontSize = LethalCruiserScanPlugin.FontSize.Value;
        _text.text =
            $"Cruiser Total: {stats.TotalValue}\nCruiser Items: {stats.ItemCount}";

        _displayTimeLeft = LethalCruiserScanPlugin.DisplayDuration.Value;

        if (_counterObject.activeSelf)
            return;

        GameNetworkManager.Instance.StartCoroutine(ValueCoroutine());
    }

    public static void Hide()
    {
        _counterObject?.SetActive(false);
    }

    private static void EnsureCreated()
    {
        if (_counterObject != null && _text != null)
            return;

        GameObject vanillaCounter = GameObject.Find(VanillaValueCounterPath);

        if (vanillaCounter == null)
        {
            LethalCruiserScanPlugin.Log.LogError("Failed to find ValueCounter object to copy.");
            return;
        }

        _counterObject = Object.Instantiate(
            vanillaCounter,
            vanillaCounter.transform.parent,
            false
        );

        _counterObject.name = "LethalCruiserScanCounter";
        _baseLocalPosition = _counterObject.transform.localPosition;

        RemoveBackdropImage();
        ConfigureText();
        ApplyPosition();

        _counterObject.SetActive(false);
    }

    private static void RemoveBackdropImage()
    {
        if (_counterObject == null)
            return;

        Image[] images = _counterObject.GetComponentsInChildren<Image>(true);

        foreach (Image image in images)
        {
            if (image.gameObject.name == BackdropImageName)
                image.gameObject.SetActive(false);
        }
    }

    private static void ConfigureText()
    {
        if (_counterObject == null)
            return;

        _text = _counterObject.GetComponentInChildren<TextMeshProUGUI>();

        if (_text == null)
        {
            LethalCruiserScanPlugin.Log.LogError(
                "Failed to find TextMeshProUGUI on copied ValueCounter."
            );

            Object.Destroy(_counterObject);
            _counterObject = null;
            return;
        }

        _text.enableAutoSizing = false;
        _text.fontSizeMin = 5f;
        _text.fontSize = LethalCruiserScanPlugin.FontSize.Value;
        _text.alignment = TextAlignmentOptions.BottomLeft;
        _text.ForceMeshUpdate();

        RectTransform textRectTransform = _text.rectTransform;

        textRectTransform.anchorMin = new Vector2(0.5f, 2f);
        textRectTransform.anchorMax = new Vector2(0.5f, 2f);
        textRectTransform.pivot = new Vector2(0.5f, 0f);

        Vector3 textLocalPosition = textRectTransform.localPosition;

        textRectTransform.localPosition = new Vector3(
            textLocalPosition.x,
            textLocalPosition.y - 140f,
            textLocalPosition.z
        );
    }

    private static void ApplyPosition()
    {
        if (_counterObject == null)
            return;

        float adjustedX = Mathf.Clamp(
            _baseLocalPosition.x + LethalCruiserScanPlugin.OffsetX.Value + 50f,
            -6000f,
            Screen.width
        );

        float adjustedY = Mathf.Clamp(
            _baseLocalPosition.y + LethalCruiserScanPlugin.OffsetY.Value - 80f,
            -6000f,
            Screen.height
        );

        _counterObject.transform.localPosition = new Vector3(
            adjustedX,
            adjustedY,
            _baseLocalPosition.z
        );
    }

    private static IEnumerator ValueCoroutine()
    {
        if (_counterObject == null)
            yield break;

        _counterObject.SetActive(true);

        while (_displayTimeLeft > 0f)
        {
            float displayTimeLeft = _displayTimeLeft;
            _displayTimeLeft = 0f;

            yield return new WaitForSeconds(displayTimeLeft);
        }

        _counterObject?.SetActive(false);
    }
}