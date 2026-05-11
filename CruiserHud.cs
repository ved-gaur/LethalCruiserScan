using System.Collections;
using TMPro;
using UnityEngine;

namespace CruiserScan;

internal static class CruiserHud
{
    private const string HudCanvasPath =
        "/Systems/UI/Canvas/IngamePlayerHUD";

    private const string VanillaValueCounterPath =
        "/Systems/UI/Canvas/IngamePlayerHUD/BottomMiddle/ValueCounter";

    private static GameObject? _counterObject;
    private static TextMeshProUGUI? _text;
    private static Coroutine? _displayCoroutine;

    public static void Show(CruiserStats stats)
    {
        EnsureCreated();
        ApplyPosition();

        if (_counterObject == null || _text == null)
            return;

        _text.fontSize = CruiserScan.FontSize.Value;
        _text.text = $"Cruiser: ${stats.TotalValue} | {stats.ItemCount} items";

        _counterObject.SetActive(true);

        if (_displayCoroutine != null)
            GameNetworkManager.Instance.StopCoroutine(_displayCoroutine);

        _displayCoroutine = GameNetworkManager.Instance.StartCoroutine(ValueCoroutine());
    }

    public static void Hide()
    {
        _counterObject?.SetActive(false);
    }

    private static void EnsureCreated()
    {
        if (_counterObject != null && _text != null)
            return;

        GameObject hudCanvas = GameObject.Find(HudCanvasPath);
        GameObject vanillaCounter = GameObject.Find(VanillaValueCounterPath);

        if (hudCanvas == null)
        {
            CruiserScan.Logger.LogError("Failed to find IngamePlayerHUD.");
            return;
        }

        if (vanillaCounter == null)
        {
            CruiserScan.Logger.LogError("Failed to find vanilla ValueCounter.");
            return;
        }

        TextMeshProUGUI vanillaText =
            vanillaCounter.GetComponentInChildren<TextMeshProUGUI>(true);

        if (vanillaText == null)
        {
            CruiserScan.Logger.LogError("Failed to find ValueCounter TMP text.");
            return;
        }

        _counterObject = new GameObject("CruiserScanCounter", typeof(RectTransform));
        _counterObject.transform.SetParent(hudCanvas.transform, false);

        _text = _counterObject.AddComponent<TextMeshProUGUI>();

        CopyTextStyle(vanillaText, _text);
        ConfigureLayout();
        ApplyPosition();

        _counterObject.SetActive(false);
    }

    private static void ConfigureLayout()
    {
        if (_counterObject == null || _text == null)
            return;

        RectTransform rootRect = _counterObject.GetComponent<RectTransform>();

        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);

        rootRect.sizeDelta = new Vector2(100f, 100f);

        RectTransform textRect = _text.rectTransform;

        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        textRect.localPosition = Vector3.zero;
        textRect.localRotation = Quaternion.identity;
        textRect.localScale = Vector3.one;
    }

    private static void CopyTextStyle(TextMeshProUGUI source, TextMeshProUGUI target)
    {
        target.font = source.font;
        target.fontSharedMaterial = source.fontSharedMaterial;
        target.spriteAsset = source.spriteAsset;

        target.color = source.color;
        target.outlineColor = source.outlineColor;
        target.outlineWidth = source.outlineWidth;

        target.fontStyle = source.fontStyle;
        target.characterSpacing = source.characterSpacing;
        target.wordSpacing = source.wordSpacing;
        target.lineSpacing = source.lineSpacing;
        target.paragraphSpacing = source.paragraphSpacing;

        target.enableAutoSizing = false;
        target.fontSize = CruiserScan.FontSize.Value;

        // just in case ..
        target.fontSizeMin = source.fontSizeMin;
        target.fontSizeMax = source.fontSizeMax;

        target.alignment = TextAlignmentOptions.Left;
        target.enableWordWrapping = false;
        target.overflowMode = TextOverflowModes.Overflow;
        target.raycastTarget = false;
    }

    private static void ApplyPosition()
    {
        if (_counterObject == null)
            return;

        RectTransform rect = _counterObject.GetComponent<RectTransform>();

        rect.anchoredPosition = new Vector2(
            CruiserScan.OffsetX.Value,
            CruiserScan.OffsetY.Value
        );
    }

    private static IEnumerator ValueCoroutine()
    {
        yield return new WaitForSeconds(CruiserScan.DisplayDuration.Value);

        _counterObject?.SetActive(false);
        _displayCoroutine = null;
    }
}