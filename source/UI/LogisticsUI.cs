using System.Collections.Generic;
using System.Linq;
using CustomUpdate;
using Data;
using Data.ScriptableObject;
using Extensions;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.UI.Windows.Elements.ObjectInfoElements;
using Game.UI.Windows.Windows;
using Language;
using LogisticsMod.Logic;
using Manager;
using ScriptableObjectScripts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogisticsMod.UI;

public class LogisticsUI : MonoBehaviour
{
    private static readonly Color RowBgColor = new Color(0.12f, 0.12f, 0.14f, 0.96f);
    private static readonly Color AccentButtonColor = new Color(0.24f, 0.29f, 0.36f, 0.98f);
    private static readonly Color ConfirmButtonColor = new Color(0.23f, 0.33f, 0.25f, 0.98f);
    private static readonly Color BackButtonColor = new Color(0.24f, 0.22f, 0.24f, 0.98f);
    private static readonly Color RemoveButtonColor = new Color(0.27f, 0.2f, 0.2f, 0.98f);
    private static readonly Color CountButtonColor = new Color(0.25f, 0.28f, 0.33f, 0.98f);
    private static readonly Color CountButtonPositiveColor = new Color(0.24f, 0.31f, 0.27f, 0.98f);
    private static readonly Color ToggleOnRowColor = new Color(0.12f, 0.27f, 0.16f, 0.96f);
    private static readonly Color ToggleOffRowColor = new Color(0.16f, 0.16f, 0.2f, 0.96f);
    private static readonly Color SubtleTextColor = new Color(0.8f, 0.8f, 0.82f, 1f);

    private List<LogisticsSection> _sections = new List<LogisticsSection>();
    private ObjectInfoData _currentData;
    private ObjectInfo _currentObjectInfo;
    private ObjectInfoWindow _objectInfoWindow;
    private RectTransform _parentRt;
    private bool _built;
    private TMP_FontAsset _font;
    private RuntimeUiStyle _runtimeStyle = new RuntimeUiStyle();

    private LogisticsSection _getSection;
    private LogisticsSection _sendSection;
    private LogisticsSection _scSection;
    private LogisticsSection _lvSection;
    private LogisticsSection _netSection;

    private string _currentNetworkId = "";
    private HashSet<string> _knownNetworks = new HashSet<string> { "" };

    private sealed class RuntimeUiStyle
    {
        public TMP_FontAsset Font;
        public float RowFontSize = 13f;
        public float HeaderFontSize = 15f;
        public float HeaderHeight = 50f;
        public float RowHeight = 28f;
        public Color HeaderTextColor = new Color(0.604f, 0.604f, 0.604f, 1f);
        public Color HeaderDividerColor = new Color(0.425f, 0.425f, 0.425f, 1f);
        public Color HeaderBackgroundColor = new Color(0f, 0f, 0f, 0f);
        public Color RowBackgroundColor = RowBgColor;
        public Color RowTextColor = SubtleTextColor;
        public Color ActionButtonColor = LogisticsUI.AccentButtonColor;
        public Color ConfirmButtonColor = LogisticsUI.ConfirmButtonColor;
        public Color BackButtonColor = LogisticsUI.BackButtonColor;
        public Color RemoveButtonColor = LogisticsUI.RemoveButtonColor;
        public Color SmallButtonColor = LogisticsUI.CountButtonColor;
        public Color SmallButtonPositiveColor = LogisticsUI.CountButtonPositiveColor;
        public Color ToggleOnColor = LogisticsUI.ToggleOnRowColor;
        public Color ToggleOffColor = LogisticsUI.ToggleOffRowColor;
        public ColorBlock HeaderButtonColors;
        public bool HasHeaderButtonColors;
    }

    private void Start()
    {
        try
        {
            _font = FindFont();
            if (_font == null) return;

            _objectInfoWindow = GetComponent<ObjectInfoWindow>();
            if (_objectInfoWindow == null) return;

            var oics = _objectInfoWindow.GetComponent<ObjectInfoCollapseSections>();
            if (oics == null || oics.uiLists == null || oics.uiLists.Count == 0) return;

            var sectionParent = oics.uiLists[0].transform;
            _parentRt = sectionParent.parent as RectTransform;
            float sectionWidth = (sectionParent as RectTransform).sizeDelta.x;
            if (sectionWidth <= 0) sectionWidth = _parentRt.rect.width;

            var styleButton = oics.expandButtons != null && oics.expandButtons.Count > 5 ? oics.expandButtons[5] : null;
            var styleIcon = oics.buttonsIcons != null && oics.buttonsIcons.Count > 5 ? oics.buttonsIcons[5] : null;
            CaptureRuntimeStyle(oics, styleButton);

            _netSection = new LogisticsSection(_parentRt, "LOGISTICS NETWORKS", _font, sectionWidth,
                styleButton, styleIcon, oics.spriteExpand, oics.spriteCollapse,
                _runtimeStyle.HeaderHeight, _runtimeStyle.HeaderFontSize, _runtimeStyle.HeaderBackgroundColor,
                _runtimeStyle.HeaderTextColor, _runtimeStyle.HeaderDividerColor, new Color(0f, 0f, 0f, 0f),
                _runtimeStyle.HasHeaderButtonColors ? _runtimeStyle.HeaderButtonColors : null);
            _sections.Add(_netSection);

            _getSection = new LogisticsSection(_parentRt, FormatSectionTitle("IMPORT", "Request Resources"), _font, sectionWidth,
                styleButton, styleIcon, oics.spriteExpand, oics.spriteCollapse,
                _runtimeStyle.HeaderHeight, _runtimeStyle.HeaderFontSize, _runtimeStyle.HeaderBackgroundColor,
                _runtimeStyle.HeaderTextColor, _runtimeStyle.HeaderDividerColor, new Color(0f, 0f, 0f, 0f),
                _runtimeStyle.HasHeaderButtonColors ? _runtimeStyle.HeaderButtonColors : null);
            _sections.Add(_getSection);

            _sendSection = new LogisticsSection(_parentRt, FormatSectionTitle("EXPORT", "Provide Resources"), _font, sectionWidth,
                styleButton, styleIcon, oics.spriteExpand, oics.spriteCollapse,
                _runtimeStyle.HeaderHeight, _runtimeStyle.HeaderFontSize, _runtimeStyle.HeaderBackgroundColor,
                _runtimeStyle.HeaderTextColor, _runtimeStyle.HeaderDividerColor, new Color(0f, 0f, 0f, 0f),
                _runtimeStyle.HasHeaderButtonColors ? _runtimeStyle.HeaderButtonColors : null);
            _sections.Add(_sendSection);

            _scSection = new LogisticsSection(_parentRt, FormatSectionTitle("SPACECRAFT", "Logistics Vessels"), _font, sectionWidth,
                styleButton, styleIcon, oics.spriteExpand, oics.spriteCollapse,
                _runtimeStyle.HeaderHeight, _runtimeStyle.HeaderFontSize, _runtimeStyle.HeaderBackgroundColor,
                _runtimeStyle.HeaderTextColor, _runtimeStyle.HeaderDividerColor, new Color(0f, 0f, 0f, 0f),
                _runtimeStyle.HasHeaderButtonColors ? _runtimeStyle.HeaderButtonColors : null);
            _sections.Add(_scSection);

            _lvSection = new LogisticsSection(_parentRt, FormatSectionTitle("LAUNCH SYSTEMS", "Transporters to orbit and beyond"), _font, sectionWidth,
                styleButton, styleIcon, oics.spriteExpand, oics.spriteCollapse,
                _runtimeStyle.HeaderHeight, _runtimeStyle.HeaderFontSize, _runtimeStyle.HeaderBackgroundColor,
                _runtimeStyle.HeaderTextColor, _runtimeStyle.HeaderDividerColor, new Color(0f, 0f, 0f, 0f),
                _runtimeStyle.HasHeaderButtonColors ? _runtimeStyle.HeaderButtonColors : null);
            _sections.Add(_lvSection);

            _built = true;
            TrySyncFromWindow(force: true);
            RefreshAllSections();
        }
        catch { }
    }

    private void OnEnable()
    {
        TrySyncFromWindow(force: true);
    }

    private void LateUpdate()
    {
        if (!_built || !isActiveAndEnabled) return;
        TrySyncFromWindow(force: false);
    }

    private TMP_FontAsset FindFont()
    {
        foreach (var tmp in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            if (tmp.font != null && tmp.isActiveAndEnabled)
                return tmp.font;
        return null;
    }

    private void CaptureRuntimeStyle(ObjectInfoCollapseSections oics, Button headerButton)
    {
        _runtimeStyle.Font = _font;
        if (headerButton != null)
        {
            _runtimeStyle.HeaderButtonColors = headerButton.colors;
            _runtimeStyle.HasHeaderButtonColors = true;
        }

        TryCaptureHeaderTypography(oics, 5, "PLANNED");
        TryCaptureLaunchListRowStyle(_objectInfoWindow?.launchVehicleList);
    }

    private void TryCaptureHeaderTypography(ObjectInfoCollapseSections oics, int sectionIndex, string headerHint)
    {
        var button = oics?.expandButtons != null && sectionIndex >= 0 && sectionIndex < oics.expandButtons.Count
            ? oics.expandButtons[sectionIndex]
            : null;
        if (button == null) return;

        foreach (var tmp in button.transform.parent.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (tmp == null) continue;
            if (tmp.text == null || tmp.text.IndexOf(headerHint, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            _runtimeStyle.Font ??= tmp.font;
            _runtimeStyle.HeaderFontSize = tmp.fontSize;
            _runtimeStyle.HeaderTextColor = tmp.color;
            var rt = button.transform.parent as RectTransform;
            if (rt != null && rt.rect.height >= 20f)
                _runtimeStyle.HeaderHeight = rt.rect.height;
            break;
        }
    }

    private void TryCaptureLaunchListRowStyle(MonoBehaviour donorList)
    {
        if (donorList == null) return;

        foreach (var btn in donorList.GetComponentsInChildren<Button>(true))
        {
            if (btn == null || btn.gameObject == donorList.gameObject) continue;
            var rt = btn.transform as RectTransform;
            if (rt == null || rt.rect.height < 40f) continue;

            var bg = btn.GetComponent<Image>();
            if (bg != null)
                _runtimeStyle.RowBackgroundColor = bg.color;

            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null)
            {
                _runtimeStyle.Font ??= tmp.font;
                _runtimeStyle.RowFontSize = tmp.fontSize;
                _runtimeStyle.RowTextColor = tmp.color;
            }
            _runtimeStyle.RowHeight = rt.rect.height;

            break;
        }
    }

    private string FormatSectionTitle(string primary, string secondary)
    {
        var subtitleColor = Color.Lerp(_runtimeStyle.HeaderTextColor, new Color(0.45f, 0.45f, 0.48f, _runtimeStyle.HeaderTextColor.a), 0.35f);
        var subtitleHex = ColorUtility.ToHtmlStringRGBA(subtitleColor);
        return $"{primary} <size=82%><color=#{subtitleHex}>— {secondary}</color></size>";
    }

    public void RefreshData(ObjectInfoData oid)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (oid != null && player != null && oid.company != player)
        {
            _currentData = null;
            _currentObjectInfo = null;
            if (_built)
                ClearForNonPlayerCompany();
            return;
        }

        var newOi = oid?.ObjectInfo;
        _currentData = oid;
        _currentObjectInfo = newOi;
        _currentNetworkId = "";
        if (!_built) return;
        RefreshAllSections();
    }

    private void TrySyncFromWindow(bool force)
    {
        if (_objectInfoWindow == null)
            _objectInfoWindow = GetComponent<ObjectInfoWindow>();
        if (_objectInfoWindow == null) return;

        var liveData = _objectInfoWindow.ObjectInfoDataCurrent;
        var liveOi = liveData?.ObjectInfo;
        var liveId = liveOi?.id ?? -1;
        var currentId = _currentObjectInfo?.id ?? -1;
        var liveCompany = liveData?.company;
        var currentCompany = _currentData?.company;

        if (!force && liveId == currentId && liveCompany == currentCompany)
            return;

        RefreshData(liveData);
    }

    private void ClearForNonPlayerCompany()
    {
        foreach (var section in _sections)
            section.ClearContent();

        _getSection?.AddTextRow("Logistics are only available for the player company.", _font, 13f, new Color(0.55f, 0.55f, 0.6f, 1f));
        LayoutRebuilder.ForceRebuildLayoutImmediate(_parentRt);
    }

    private void BuildNetSection()
    {
        _netSection.ClearContent();
        var dataIds = Data.LogisticsNetwork.GetAllNetworkIds();
        foreach (var id in dataIds)
            _knownNetworks.Add(id);

        var allIds = _knownNetworks.OrderBy(id => id).ToList();

        var currentLabel = string.IsNullOrEmpty(_currentNetworkId) ? "Global" : _currentNetworkId;
        _netSection.AddTextRow($"Active network: {currentLabel}", _font, 13f, new Color(0.7f, 0.9f, 0.7f, 1f));

        foreach (var netId in allIds)
        {
            var label = string.IsNullOrEmpty(netId) ? "Global" : netId;
            var isActive = netId == _currentNetworkId;

            var row = MakeHLRow(_netSection.ContentArea, 24f, 4);
            row.GetComponent<Image>().color = isActive ? new Color(0.28f, 0.38f, 0.52f, 1f) : _runtimeStyle.RowBackgroundColor;

            MakeTMP(row.transform, label, 13, isActive ? Color.white : _runtimeStyle.RowTextColor);

            if (!isActive)
            {
                var btn = row.AddComponent<Button>();
                btn.navigation = new Navigation { mode = Navigation.Mode.None };
                var capturedNet = netId;
                btn.onClick.AddListener(() =>
                {
                    _currentNetworkId = capturedNet;
                    RefreshAllSections();
                });
            }

            // Delete button (not for Global)
            if (!string.IsNullOrEmpty(netId))
            {
                var capturedNet = netId;
                MakeXButton(row.transform, () =>
                {
                    Data.LogisticsNetwork.RemoveAllForNetwork(capturedNet);
                    _knownNetworks.Remove(capturedNet);
                    if (_currentNetworkId == capturedNet)
                        _currentNetworkId = "";
                    RefreshAllSections();
                });
            }
        }

        AddBigButton(_netSection.ContentArea, "+ Add Network", _runtimeStyle.SmallButtonPositiveColor, () =>
        {
            ShowAddNetworkInput();
        });

        _netSection.Root.SetActive(true);
        _netSection.SetExpanded(true);
        RebuildSectionLayout(_netSection);
    }

    private void ShowAddNetworkInput()
    {
        _netSection.ClearContent();
        _inputConfirmed = false;
        var currentName = "";

        AddBigButton(_netSection.ContentArea, "\u2190 Back to networks", _runtimeStyle.BackButtonColor, () =>
        {
            BuildNetSection();
            RebuildSectionLayout(_netSection);
        });

        var titleLabel = MakeTMP(_netSection.ContentArea, "Enter network name:", 14, new Color(0.9f, 0.9f, 0.5f, 1f));
        titleLabel.rectTransform.sizeDelta = new Vector2(0, 22);

        var displayRow = MakeHLRow(_netSection.ContentArea, 28f, 0);
        var displayTmp = MakeTMP(displayRow.transform, "", 16, Color.white);
        displayTmp.alignment = TextAlignmentOptions.Center;

        var warnRow = MakeHLRow(_netSection.ContentArea, 18f, 0);
        warnRow.GetComponent<Image>().color = new Color(0, 0, 0, 0);
        var warnTmp = MakeTMP(warnRow.transform, "", 12, new Color(0.9f, 0.4f, 0.2f, 1f));
        warnTmp.alignment = TextAlignmentOptions.Center;

        void UpdateDisplay()
        {
            displayTmp.text = string.IsNullOrEmpty(currentName) ? "(type a name)" : currentName;
            warnTmp.text = "";
        }
        UpdateDisplay();

        void AddKey(Transform parent, string key, float width)
        {
            var btnGo = new GameObject("Key", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            btnGo.transform.SetParent(parent, false);
            btnGo.GetComponent<LayoutElement>().preferredWidth = width;
            btnGo.GetComponent<LayoutElement>().preferredHeight = 26f;
            btnGo.GetComponent<LayoutElement>().flexibleWidth = 0;
            btnGo.GetComponent<Image>().color = _runtimeStyle.ActionButtonColor;
            var btn = btnGo.GetComponent<Button>();
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var captured = key;
            btn.onClick.AddListener(() =>
            {
                currentName += captured;
                UpdateDisplay();
            });

            var tmp = MakeTMP(btnGo.transform, key, 13, Color.white);
            tmp.alignment = TextAlignmentOptions.Center;
        }

        float keyW = 30f;

        // Row 1: 1 2 3 4 5 6 7 8 9 0 -
        var row1 = MakeHLRow(_netSection.ContentArea, 28f, 3);
        foreach (char c in "1234567890")
            AddKey(row1.transform, c.ToString(), keyW);
        AddBigButtonInline(row1.transform, "-", _runtimeStyle.ActionButtonColor, () =>
        {
            currentName += "-";
            UpdateDisplay();
        });

        // Row 2: Q W E R T Y U I O P ⌫
        var row2 = MakeHLRow(_netSection.ContentArea, 28f, 3);
        foreach (char c in "QWERTYUIOP")
            AddKey(row2.transform, c.ToString(), keyW);
        AddBigButtonInline(row2.transform, "BACKSPACE", _runtimeStyle.SmallButtonColor, () =>
        {
            if (currentName.Length > 0)
                currentName = currentName.Substring(0, currentName.Length - 1);
            UpdateDisplay();
        });

        // Row 3: A S D F G H J K L ( ) [ ] / .
        var row3 = MakeHLRow(_netSection.ContentArea, 28f, 3);
        foreach (char c in "ASDFGHJKL()[]/.")
            AddKey(row3.transform, c.ToString(), keyW);

        // Row 4: Z X C V B N M Space
        var row4 = MakeHLRow(_netSection.ContentArea, 28f, 3);
        foreach (char c in "ZXCVBNM")
            AddKey(row4.transform, c.ToString(), keyW);
        AddBigButtonInline(row4.transform, "Space", _runtimeStyle.ActionButtonColor, () =>
        {
            currentName += " ";
            UpdateDisplay();
        });

        // Row 5: Clear, Confirm, Cancel
        var row5 = MakeHLRow(_netSection.ContentArea, 28f, 4);
        AddBigButtonInline(row5.transform, "Clear", _runtimeStyle.RemoveButtonColor, () =>
        {
            currentName = "";
            UpdateDisplay();
        });
        AddBigButtonInline(row5.transform, "Confirm", _runtimeStyle.ConfirmButtonColor, () =>
        {
            if (_inputConfirmed) return;
            var trimmed = currentName.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                warnTmp.text = "Name cannot be empty";
                return;
            }
            if (_knownNetworks.Contains(trimmed))
            {
                warnTmp.text = $"\"{trimmed}\" already exists";
                return;
            }
            _inputConfirmed = true;
            _currentNetworkId = trimmed;
            _knownNetworks.Add(trimmed);
            RefreshAllSections();
        });
        AddBigButtonInline(row5.transform, "Cancel", _runtimeStyle.BackButtonColor, () =>
        {
            _inputConfirmed = true;
            BuildNetSection();
            RebuildSectionLayout(_netSection);
        });

        RebuildSectionLayout(_netSection);
    }

    private void RefreshAllSections()
    {
        if (_currentObjectInfo == null) return;
        BuildNetSection();
        BuildGetSection();
        _getSection.Root.SetActive(true);

        _sendSection.Root.SetActive(true);
        _scSection.Root.SetActive(true);
        _lvSection.Root.SetActive(true);

        BuildSendSection();
        BuildSCSection();
        BuildLVSection();

        LayoutRebuilder.ForceRebuildLayoutImmediate(_parentRt);
    }

    private void RebuildSectionLayout(LogisticsSection section)
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(section.ContentArea);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_parentRt);
    }

    private void BuildGetSection()
    {
        _getSection.ClearContent();
        var data = Data.LogisticsNetwork.Get(_currentObjectInfo);

        var requests = data?.requests.Where(r => r.networkId == _currentNetworkId).ToList() ?? new List<Data.LogisticsRequest>();

        if (requests.Count > 0)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                var req = requests[i];
                var rd = req.ResourceDefinition;
                var displayName = ResourceLabel(rd, req.resourceDef?.id);
                var statusStr = StatusToString(req.status);
                var noteStr = !string.IsNullOrEmpty(req.statusNote) ? $" ({req.statusNote})" : "";

                var row = MakeHLRow(_getSection.ContentArea, 24f, 8);
                MakeTMP(row.transform, $"{displayName}: {req.requestedAmount:0.#}  [{statusStr}]{noteStr}", 13, StatusColor(req.status));
                var capturedOi = _currentObjectInfo;
                var capturedIdx = data.requests.IndexOf(req);
                MakeXButton(row.transform, () =>
                {
                    if (capturedIdx >= 0) Data.LogisticsNetwork.RemoveRequest(capturedOi, capturedIdx);
                    BuildGetSection();
                    RebuildSectionLayout(_getSection);
                });
            }
        }
        else
        {
            _getSection.AddTextRow("No resource requests configured.", _font, 13f, new Color(0.5f, 0.5f, 0.5f, 1f));
        }

        AddBigButton(_getSection.ContentArea, "+ Add Request", _runtimeStyle.ConfirmButtonColor, () =>
        {
            ShowResourcePicker(_getSection, true);
        });
    }

    private void BuildSendSection()
    {
        _sendSection.ClearContent();
        var data = Data.LogisticsNetwork.Get(_currentObjectInfo);

        var providers = data?.providers.Where(p => p.networkId == _currentNetworkId).ToList() ?? new List<Data.LogisticsProvider>();
        bool objectHasOrbit = _currentObjectInfo != null
            && _currentObjectInfo.objectTypes != EObjectTypes.Orbit
            && Data.LogisticsNetwork.GetLowOrbitOf(_currentObjectInfo) != null;

        if (providers.Count > 0)
        {
            for (int i = 0; i < providers.Count; i++)
            {
                var prov = providers[i];
                var rd = prov.ResourceDefinition;
                var displayName = ResourceLabel(rd, prov.resourceDef?.id);

                var row = MakeHLRow(_sendSection.ContentArea, 24f, 8);
                MakeTMP(row.transform, $"{displayName}: min keep {prov.minimumKeep:0.#}", 13, new Color(0.7f, 0.7f, 0.7f, 1f));

                // To-Orbit toggle button (only if object has low orbit)
                if (objectHasOrbit)
                {
                    var toOrbitLabel = prov.toOrbit ? "\u2191Orbit" : "\u2193Orbit";
                    var toOrbitColor = prov.toOrbit ? new Color(0.24f, 0.54f, 0.27f, 1f) : new Color(0.28f, 0.28f, 0.3f, 1f);
                    var toOrbitTxtColor = prov.toOrbit ? new Color(0.58f, 0.9f, 0.58f, 1f) : new Color(0.48f, 0.48f, 0.52f, 1f);
                    var orbitBtnGo = new GameObject("OrbitBtn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                    orbitBtnGo.transform.SetParent(row.transform, false);
                    orbitBtnGo.GetComponent<LayoutElement>().preferredWidth = 50f;
                    orbitBtnGo.GetComponent<Image>().color = toOrbitColor;
                    var orbitBtn = orbitBtnGo.GetComponent<Button>();
                    orbitBtn.navigation = new Navigation { mode = Navigation.Mode.None };
                    var orbitTmp = MakeTMP(orbitBtnGo.transform, toOrbitLabel, 11, toOrbitTxtColor);
                    orbitTmp.alignment = TextAlignmentOptions.Center;

                    var capturedProv = prov;
                    var capturedImg = orbitBtnGo.GetComponent<Image>();
                    var capturedTmp = orbitTmp;
                    orbitBtn.onClick.AddListener(() =>
                    {
                        capturedProv.toOrbit = !capturedProv.toOrbit;
                        capturedTmp.text = capturedProv.toOrbit ? "\u2191Orbit" : "\u2193Orbit";
                        capturedImg.color = capturedProv.toOrbit ? new Color(0.24f, 0.54f, 0.27f, 1f) : new Color(0.28f, 0.28f, 0.3f, 1f);
                        capturedTmp.color = capturedProv.toOrbit ? new Color(0.58f, 0.9f, 0.58f, 1f) : new Color(0.48f, 0.48f, 0.52f, 1f);
                    });
                }

                var capturedOi = _currentObjectInfo;
                var capturedIdx = data.providers.IndexOf(prov);
                MakeXButton(row.transform, () =>
                {
                    if (capturedIdx >= 0) Data.LogisticsNetwork.RemoveProvider(capturedOi, capturedIdx);
                    BuildSendSection();
                    RebuildSectionLayout(_sendSection);
                });
            }
        }
        else
        {
            _sendSection.AddTextRow("No resource exports configured.", _font, 13f, new Color(0.5f, 0.5f, 0.5f, 1f));
        }

        AddBigButton(_sendSection.ContentArea, "+ Add Provider", _runtimeStyle.ActionButtonColor, () =>
        {
            ShowResourcePicker(_sendSection, false);
        });
    }

    private void BuildSCSection()
    {
        BuildShipSection(_scSection, true);
    }

    private void BuildLVSection()
    {
        BuildShipSection(_lvSection, false);
    }

    private void BuildShipSection(LogisticsSection section, bool isSpacecraft)
    {
        section.ClearContent();
        if (_currentObjectInfo == null) return;

        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (player == null)
        {
            RebuildSectionLayout(section);
            return;
        }

        var typeCounts = isSpacecraft
            ? Data.LogisticsNetwork.GetShipTypeCountsOnObject(_currentObjectInfo, true, player)
            : Data.LogisticsNetwork.GetAllLVTypeCountsOnObject(_currentObjectInfo, player);

        var typeName = isSpacecraft ? "spacecraft" : "launch vehicles";

        if (typeCounts.Count == 0)
        {
            section.AddTextRow($"No {typeName} on this object.", _font, 13f, new Color(0.5f, 0.5f, 0.5f, 1f));
            RebuildSectionLayout(section);
            return;
        }

        section.AddTextRow("Click to toggle:", _font, 12f, new Color(0.5f, 0.5f, 0.58f, 1f));

        foreach (var kv in typeCounts)
        {
            var shipTypeName = kv.Key;
            var count = kv.Value;

            if (isSpacecraft && TypeWouldGetStuckOnSurface(FindSpacecraftType(shipTypeName), _currentObjectInfo))
                continue;

            var currentQuota = Data.LogisticsNetwork.GetQuota(_currentObjectInfo, shipTypeName, isSpacecraft, _currentNetworkId);
            var isEnabled = currentQuota > 0;

            var row = MakeHLRow(section.ContentArea, 26f, 4);
            row.GetComponent<Image>().color = isEnabled ? _runtimeStyle.ToggleOnColor : _runtimeStyle.ToggleOffColor;

            var activeColor = isEnabled ? new Color(0.54f, 0.9f, 0.62f, 1f) : new Color(0.66f, 0.66f, 0.7f, 1f);
            MakeTMP(row.transform, $"{ShipDisplayName(shipTypeName, isSpacecraft)}  x{count}", 13, activeColor);

            var statusText = isEnabled ? "ON" : "OFF";
            var statusColor = isEnabled ? new Color(0.58f, 0.9f, 0.58f, 1f) : new Color(0.48f, 0.48f, 0.52f, 1f);
            MakeTMP(row.transform, statusText, 11, statusColor);

            var btn = row.AddComponent<Button>();
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var capturedOi = _currentObjectInfo;
            var capturedType = shipTypeName;
            var capturedIsSc = isSpacecraft;
            btn.onClick.AddListener(() =>
            {
                if (Data.LogisticsNetwork.GetQuota(capturedOi, capturedType, capturedIsSc, _currentNetworkId) > 0)
                    Data.LogisticsNetwork.RemoveQuota(capturedOi, capturedType, capturedIsSc, _currentNetworkId);
                else
                    Data.LogisticsNetwork.SetQuota(capturedOi, capturedType, 1, capturedIsSc, _currentNetworkId);
                BuildShipSection(section, capturedIsSc);
                RebuildSectionLayout(section);
            });
        }

        RebuildSectionLayout(section);
    }

    private void ShowResourcePicker(LogisticsSection section, bool isGet)
    {
        section.ClearContent();

        AddBigButton(section.ContentArea, "\u2190 Back", _runtimeStyle.BackButtonColor, () =>
        {
            if (isGet) BuildGetSection(); else BuildSendSection();
            RebuildSectionLayout(section);
        });

        var am = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance;
        if (am == null || am.AllResourceDefinitions == null)
        {
            section.AddTextRow("Resource list not available.", _font);
            RebuildSectionLayout(section);
            return;
        }

        var gm = MonoBehaviourSingleton<GameManager>.Instance;
        var player = gm?.Player;
        HashSet<ResourceDefinition> available;
        if (player != null && _currentObjectInfo != null)
        {
            if (isGet)
                available = Data.LogisticsNetwork.GetNetworkResourcesSet(player, _currentNetworkId);
            else
                available = Data.LogisticsNetwork.GetAvailableResourcesOnObject(_currentObjectInfo, player);
        }
        else
        {
            available = new HashSet<ResourceDefinition>();
        }

        var sorted = am.AllResourceDefinitions.ListNotEmpty
            .Where(rd => rd.ResourceType != ResourceDefinition.EResourceType.Energy && rd.ResourceType != ResourceDefinition.EResourceType.Human)
            .OrderByDescending(rd => available.Contains(rd))
            .ThenBy(rd => rd.ID)
            .ToList();

        foreach (var rd in sorted)
        {
            var rdCaptured = rd;
            var sectionRef = section;
            var isGetCaptured = isGet;
            var isAvailable = available.Contains(rd);

            var row = MakeHLRow(section.ContentArea, 24f, 0);
            row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
            var color = isAvailable ? new Color(0.8f, 0.8f, 0.8f, 1f) : new Color(0.35f, 0.35f, 0.35f, 1f);
            var label = isAvailable ? ResourceLabel(rd) : $"{ResourceLabel(rd)} (not available)";
            MakeTMP(row.transform, label, 13, color);

            var btn = row.AddComponent<Button>();
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.onClick.AddListener(() =>
            {
                ShowAmountInput(sectionRef, rdCaptured, isGetCaptured, isAvailable);
            });
        }

        RebuildSectionLayout(section);
    }

    private bool _inputConfirmed;

    private void ShowAmountInput(LogisticsSection section, ResourceDefinition rd, bool isGet, bool isAvailable = true)
    {
        var capturedOi = _currentObjectInfo;
        _inputConfirmed = false;
        double currentAmount = 0;
        section.ClearContent();

        AddBigButton(section.ContentArea, "\u2190 Back to resources", _runtimeStyle.BackButtonColor, () =>
        {
            ShowResourcePicker(section, isGet);
        });

        if (!isAvailable)
        {
            var warnTmp = MakeTMP(section.ContentArea, "WARNING: Resource not currently available", 12, new Color(0.9f, 0.6f, 0.1f, 1f));
            warnTmp.rectTransform.sizeDelta = new Vector2(0, 20);
        }

        var titleLabel = MakeTMP(section.ContentArea, $"{(isGet ? "Request" : "Provide")}: {ResourceLabel(rd)}", 14, new Color(0.9f, 0.9f, 0.5f, 1f));
        titleLabel.rectTransform.sizeDelta = new Vector2(0, 22);

        var amountRow = MakeHLRow(section.ContentArea, 34f, 0);
        var amountDisplay = MakeTMP(amountRow.transform, "0", 22, Color.white);
        amountDisplay.alignment = TextAlignmentOptions.Center;

        void UpdateAmountDisplay()
        {
            if (currentAmount >= 1_000_000)
                amountDisplay.text = (currentAmount / 1_000_000).ToString("0.##") + "M";
            else if (currentAmount >= 1_000)
                amountDisplay.text = (currentAmount / 1_000).ToString("0.##") + "K";
            else
                amountDisplay.text = currentAmount.ToString("0");
        }

        var plusRow = MakeHLRow(section.ContentArea, 28f, 4);
        AddSmallButton(plusRow.transform, "+10", _runtimeStyle.SmallButtonPositiveColor, () => { currentAmount += 10; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+100", _runtimeStyle.SmallButtonPositiveColor, () => { currentAmount += 100; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+1K", _runtimeStyle.SmallButtonPositiveColor, () => { currentAmount += 1000; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+10K", _runtimeStyle.SmallButtonPositiveColor, () => { currentAmount += 10000; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+100K", _runtimeStyle.SmallButtonPositiveColor, () => { currentAmount += 100000; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+1M", _runtimeStyle.SmallButtonPositiveColor, () => { currentAmount += 1000000; UpdateAmountDisplay(); });

        var minusRow = MakeHLRow(section.ContentArea, 28f, 4);
        AddSmallButton(minusRow.transform, "\u221210", _runtimeStyle.SmallButtonColor, () => { currentAmount = System.Math.Max(0, currentAmount - 10); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u2212100", _runtimeStyle.SmallButtonColor, () => { currentAmount = System.Math.Max(0, currentAmount - 100); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u22121K", _runtimeStyle.SmallButtonColor, () => { currentAmount = System.Math.Max(0, currentAmount - 1000); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u221210K", _runtimeStyle.SmallButtonColor, () => { currentAmount = System.Math.Max(0, currentAmount - 10000); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u2212100K", _runtimeStyle.SmallButtonColor, () => { currentAmount = System.Math.Max(0, currentAmount - 100000); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u22121M", _runtimeStyle.SmallButtonColor, () => { currentAmount = System.Math.Max(0, currentAmount - 1000000); UpdateAmountDisplay(); });

        // To Orbit toggle (EXPORT only, object with low orbit, not itself orbit)
        bool toOrbit = false;
        bool hasOrbit = !isGet && _currentObjectInfo != null
            && _currentObjectInfo.objectTypes != EObjectTypes.Orbit
            && Data.LogisticsNetwork.GetLowOrbitOf(_currentObjectInfo) != null;

        if (hasOrbit)
        {
            var orbitRow = MakeHLRow(section.ContentArea, 28f, 4);
            var orbitTmp = MakeTMP(orbitRow.transform, "\u2191 To Orbit:", 13, new Color(0.7f, 0.7f, 0.7f, 1f));

            {
                var toggleBtnGo = new GameObject("ToOrbitToggle", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                toggleBtnGo.transform.SetParent(orbitRow.transform, false);
                toggleBtnGo.GetComponent<LayoutElement>().preferredWidth = 60f;
                toggleBtnGo.GetComponent<LayoutElement>().preferredHeight = 24f;
                var toggleBtn = toggleBtnGo.GetComponent<Button>();
                toggleBtn.navigation = new Navigation { mode = Navigation.Mode.None };
                var toggleTmp = MakeTMP(toggleBtnGo.transform, "OFF", 12, new Color(0.86f, 0.86f, 0.88f, 1f));
                toggleTmp.alignment = TextAlignmentOptions.Center;
                toggleBtnGo.GetComponent<Image>().color = _runtimeStyle.ToggleOffColor;

                var capturedTmp = toggleTmp;
                var capturedImg = toggleBtnGo.GetComponent<Image>();
                toggleBtn.onClick.AddListener(() =>
                {
                    toOrbit = !toOrbit;
                    capturedTmp.text = toOrbit ? "ON" : "OFF";
                    capturedImg.color = toOrbit ? _runtimeStyle.ToggleOnColor : _runtimeStyle.ToggleOffColor;
                });
            }
        }

        void DoConfirm()
        {
            if (_inputConfirmed) return;
            _inputConfirmed = true;
            if (currentAmount > 0)
            {
                if (isGet)
                    Data.LogisticsNetwork.AddRequest(capturedOi, rd, currentAmount, _currentNetworkId);
                else
                    Data.LogisticsNetwork.AddProvider(capturedOi, rd, currentAmount, _currentNetworkId, toOrbit);
            }
            if (isGet) BuildGetSection(); else BuildSendSection();
            RebuildSectionLayout(section);
        }

        var confirmRow = new GameObject("ConfirmRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        confirmRow.transform.SetParent(section.ContentArea, false);
        confirmRow.GetComponent<LayoutElement>().preferredHeight = 32f;
        var crHLG = confirmRow.GetComponent<HorizontalLayoutGroup>();
        crHLG.spacing = 8;

        AddBigButtonInline(confirmRow.transform, "Confirm", _runtimeStyle.ConfirmButtonColor, () => DoConfirm());
        AddBigButtonInline(confirmRow.transform, "Cancel", _runtimeStyle.BackButtonColor, () =>
        {
            _inputConfirmed = true;
            ShowResourcePicker(section, isGet);
        });

        RebuildSectionLayout(section);
    }

    private GameObject MakeHLRow(Transform parent, float height, float spacing)
    {
        var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(Image), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = height;
        row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.spacing = spacing;
        hlg.padding = new RectOffset(8, 8, 2, 2);
        return row;
    }

    private void MakeXButton(Transform parent, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject("XBtn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(parent, false);
        btnGo.GetComponent<LayoutElement>().preferredWidth = 24f;
        btnGo.GetComponent<Image>().color = _runtimeStyle.RemoveButtonColor;
        var btn = btnGo.GetComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var tmp = MakeTMP(btnGo.transform, "X", 12, new Color(0.92f, 0.88f, 0.88f, 1f));
        tmp.alignment = TextAlignmentOptions.Center;
        btn.onClick.AddListener(onClick);
    }

    private void AddBigButton(Transform parent, string text, Color color, UnityEngine.Events.UnityAction onClick)
    {
        AddBigButtonInline(parent, text, color, onClick);
    }

    private void AddBigButtonInline(Transform parent, string text, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(parent, false);
        var layout = btnGo.GetComponent<LayoutElement>();
        layout.preferredHeight = 28f;
        layout.minWidth = 120f;
        layout.flexibleWidth = 1f;
        btnGo.GetComponent<Image>().color = color;
        var btn = btnGo.GetComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };

        var labelTmp = MakeTMP(btnGo.transform, text, 14, new Color(0.86f, 0.86f, 0.88f, 1f));
        labelTmp.alignment = TextAlignmentOptions.Center;
        labelTmp.rectTransform.offsetMin = new Vector2(8, 2);
        labelTmp.rectTransform.offsetMax = new Vector2(-8, -2);

        btn.onClick.AddListener(onClick);
    }

    private void AddSmallButton(Transform parent, string text, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(parent, false);
        btnGo.GetComponent<LayoutElement>().preferredWidth = 46f;
        btnGo.GetComponent<LayoutElement>().preferredHeight = 24f;
        btnGo.GetComponent<Image>().color = color;
        var btn = btnGo.GetComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };

        var labelTmp = MakeTMP(btnGo.transform, text, 12, new Color(0.86f, 0.86f, 0.88f, 1f));
        labelTmp.alignment = TextAlignmentOptions.Center;
        labelTmp.rectTransform.offsetMin = new Vector2(4, 1);
        labelTmp.rectTransform.offsetMax = new Vector2(-4, -1);

        btn.onClick.AddListener(onClick);
    }

    private TextMeshProUGUI MakeTMP(Transform parent, string text, float fontSize, Color color)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(4, 2); rt.offsetMax = new Vector2(-4, -2);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.font = _font; tmp.fontSize = fontSize; tmp.color = color;
        tmp.richText = true;
        return tmp;
    }

    public void RebuildLayout()
    {
        if (_built && isActiveAndEnabled)
            LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
    }

    private void OnDestroy()
    {
        foreach (var sec in _sections)
            if (sec?.Root != null) Destroy(sec.Root);
        _sections.Clear();
    }

    private static string StatusToString(Data.LogisticsRequestStatus s) => s switch
    {
        Data.LogisticsRequestStatus.Pending => "pending",
        Data.LogisticsRequestStatus.InProgress => "in transit",
        Data.LogisticsRequestStatus.Satisfied => "satisfied",
        Data.LogisticsRequestStatus.Failed => "failed",
        _ => "?"
    };

    private static Color StatusColor(Data.LogisticsRequestStatus s) => s switch
    {
        Data.LogisticsRequestStatus.Pending => new Color(0.7f, 0.7f, 0.3f, 1f),
        Data.LogisticsRequestStatus.InProgress => new Color(0.3f, 0.5f, 0.9f, 1f),
        Data.LogisticsRequestStatus.Satisfied => new Color(0.3f, 0.8f, 0.3f, 1f),
        Data.LogisticsRequestStatus.Failed => new Color(0.9f, 0.3f, 0.3f, 1f),
        _ => new Color(0.5f, 0.5f, 0.5f, 1f)
    };

    private static string ResourceLabel(ResourceDefinition rd, string fallbackId = null)
    {
        if (rd == null) return fallbackId ?? "?";
        var name = LEManager.Get(rd.ID, rd.ID);
        return $"{rd.IconString} {name}";
    }

    private static string ShipDisplayName(string typeKey, bool isSpacecraft)
    {
        if (string.IsNullOrEmpty(typeKey)) return "?";

        if (isSpacecraft)
        {
            foreach (var sc in Object.FindObjectsOfType<Spacecraft>())
            {
                var type = sc?.spacecraftType;
                if (type == null) continue;
                if (sc.GetCompany() != MonoBehaviourSingleton<GameManager>.Instance?.Player) continue;
                if (Data.LogisticsNetwork.TypeKey(type.ID, type.NameRocketType ?? "SC") == typeKey || type.NameRocketType == typeKey)
                    return ShipIcon(type.SpriteId) + " " + type.NameRocketType;
            }
        }
        else
        {
            foreach (var lv in Object.FindObjectsOfType<LaunchVehicle>())
            {
                var type = lv?.launchVehicleType;
                if (type == null) continue;
                if (lv.GetCompany() != MonoBehaviourSingleton<GameManager>.Instance?.Player) continue;
                if (Data.LogisticsNetwork.TypeKey(type.ID, type.Name ?? "LV") == typeKey || type.Name == typeKey)
                    return ShipIcon(type.SpriteId) + " " + type.Name;
            }
        }

        return typeKey;
    }

    private static string ShipIcon(string spriteId)
    {
        if (string.IsNullOrEmpty(spriteId)) return "";
        var objManager = MonoBehaviourSingleton<ObjectInfoManager>.Instance;
        return objManager != null ? objManager.spriteTextStart5.MyFormat(spriteId, "") : "";
    }

    private static bool TypeWouldGetStuckOnSurface(SpacecraftType scType, ObjectInfo body)
    {
        if (scType == null || body == null) return false;
        if (body.objectTypes != EObjectTypes.Planet && body.objectTypes != EObjectTypes.Moons)
            return false;
        if (scType.DestroyOnLand || scType.LowOrbitContainer || scType.MagneticCatapult)
            return false;
        return scType.needLaunchVehicleToGoToMoon;
    }

    private static SpacecraftType FindSpacecraftType(string typeKey)
    {
        foreach (var sc in Object.FindObjectsOfType<Spacecraft>())
        {
            var t = sc?.spacecraftType;
            if (t == null) continue;
            if (Data.LogisticsNetwork.TypeKey(t.ID, t.NameRocketType ?? "SC") == typeKey || t.NameRocketType == typeKey)
                return t;
        }
        return null;
    }

}