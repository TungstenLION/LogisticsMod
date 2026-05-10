using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LogisticsMod.UI;

public class LogisticsSection
{
    public GameObject Root { get; private set; }
    public RectTransform ContentArea { get; private set; }
    public bool Expanded { get; private set; }

    private TextMeshProUGUI _arrowText;
    private GameObject _contentGo;
    private RectTransform _parentRt;

    private const float HeaderHeight = 28f;

    private static readonly Color HeaderColor = new Color(0.15f, 0.15f, 0.17f, 1f);
    private static readonly Color ContentBgColor = new Color(0.1f, 0.1f, 0.12f, 1f);

    public LogisticsSection(Transform parent, string title, TMP_FontAsset font, float fixedWidth)
    {
        _parentRt = parent as RectTransform;

        Root = new GameObject("Log_" + title.Replace(" ", "").Replace("\u2014", ""),
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        Root.transform.SetParent(parent, false);
        Root.transform.SetAsLastSibling();

        var rootRt = Root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0, 1);
        rootRt.anchorMax = new Vector2(0, 1);
        rootRt.pivot = new Vector2(0, 1);
        rootRt.sizeDelta = new Vector2(fixedWidth, HeaderHeight);

        var rootVlg = Root.GetComponent<VerticalLayoutGroup>();
        rootVlg.childForceExpandWidth = true;
        rootVlg.childForceExpandHeight = false;
        rootVlg.childControlHeight = true;
        rootVlg.childControlWidth = true;
        rootVlg.padding = new RectOffset(0, 0, 0, 0);
        rootVlg.spacing = 0;

        var rootCsf = Root.GetComponent<ContentSizeFitter>();
        rootCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        rootCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var headerGo = new GameObject("Header", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        headerGo.transform.SetParent(Root.transform, false);
        var headerLayout = headerGo.GetComponent<LayoutElement>();
        headerLayout.preferredHeight = HeaderHeight;

        var headerImg = headerGo.GetComponent<Image>();
        headerImg.color = HeaderColor;

        var btn = headerGo.GetComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };

        var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(TextMeshProUGUI));
        arrowGo.transform.SetParent(headerGo.transform, false);
        var arrowRt = arrowGo.GetComponent<RectTransform>();
        arrowRt.anchorMin = new Vector2(0, 0.5f);
        arrowRt.anchorMax = new Vector2(0, 0.5f);
        arrowRt.pivot = new Vector2(0, 0.5f);
        arrowRt.sizeDelta = new Vector2(20, 20);
        arrowRt.anchoredPosition = new Vector2(8, 0);
        _arrowText = arrowGo.GetComponent<TextMeshProUGUI>();
        _arrowText.text = "\u25B6";
        _arrowText.font = font;
        _arrowText.fontSize = 12;
        _arrowText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        _arrowText.alignment = TextAlignmentOptions.Center;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(headerGo.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 0);
        labelRt.anchorMax = new Vector2(1, 1);
        labelRt.offsetMin = new Vector2(30, 4);
        labelRt.offsetMax = new Vector2(-6, -4);
        var labelTmp = labelGo.GetComponent<TextMeshProUGUI>();
        labelTmp.text = title;
        labelTmp.font = font;
        labelTmp.fontSize = 14;
        labelTmp.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        labelTmp.fontStyle = FontStyles.Bold;
        labelTmp.alignment = TextAlignmentOptions.Left;

        _contentGo = new GameObject("Content", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        _contentGo.transform.SetParent(Root.transform, false);
        ContentArea = _contentGo.GetComponent<RectTransform>();

        _contentGo.GetComponent<Image>().color = ContentBgColor;

        var cVlg = _contentGo.GetComponent<VerticalLayoutGroup>();
        cVlg.childForceExpandWidth = true;
        cVlg.childForceExpandHeight = false;
        cVlg.childControlHeight = true;
        cVlg.childControlWidth = true;
        cVlg.padding = new RectOffset(8, 8, 6, 6);
        cVlg.spacing = 4;

        var cCsf = _contentGo.GetComponent<ContentSizeFitter>();
        cCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        cCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
        placeholderGo.transform.SetParent(_contentGo.transform, false);
        var phTmp = placeholderGo.GetComponent<TextMeshProUGUI>();
        phTmp.text = "No items configured.";
        phTmp.font = font;
        phTmp.fontSize = 13;
        phTmp.color = new Color(0.5f, 0.5f, 0.5f, 1f);

        SetExpanded(false);

        btn.onClick.AddListener(() => Toggle());
    }

    public void Toggle()
    {
        Expanded = !Expanded;
        _contentGo.SetActive(Expanded);
        _arrowText.text = Expanded ? "\u25BC" : "\u25B6";
        LayoutRebuilder.ForceRebuildLayoutImmediate(_parentRt);
    }

    public void SetExpanded(bool expanded)
    {
        Expanded = expanded;
        _contentGo.SetActive(expanded);
        _arrowText.text = expanded ? "\u25BC" : "\u25B6";
    }

    public TextMeshProUGUI AddTextRow(string text, TMP_FontAsset font, float fontSize = 13f, Color? color = null)
    {
        var go = new GameObject("Row", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(_contentGo.transform, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.color = color ?? new Color(0.7f, 0.7f, 0.7f, 1f);
        return tmp;
    }

    public void ClearContent()
    {
        var es = EventSystem.current;
        var children = new List<GameObject>();
        foreach (Transform child in _contentGo.transform)
            children.Add(child.gameObject);
        foreach (var child in children)
        {
            if (es != null && es.currentSelectedGameObject == child)
                es.SetSelectedGameObject(null);
            Object.DestroyImmediate(child);
        }
    }
}
