using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.UI.Windows.Elements.ObjectInfoElements;
using Game.UI.Windows.Windows;
using LogisticsMod.Logic;
using Manager;
using ScriptableObjectScripts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogisticsMod.UI;

public class LogisticsUI : MonoBehaviour
{
    private List<LogisticsSection> _sections = new List<LogisticsSection>();
    private ObjectInfoData _currentData;
    private ObjectInfo _currentObjectInfo;
    private RectTransform _parentRt;
    private bool _built;
    private TMP_FontAsset _font;

    private LogisticsSection _getSection;
    private LogisticsSection _sendSection;
    private LogisticsSection _scSection;
    private LogisticsSection _lvSection;

    private void Start()
    {
        try
        {
            _font = FindFont();
            if (_font == null) { LogisticsObserver.LogError("No TMP font found!"); return; }

            var oiw = GetComponent<ObjectInfoWindow>();
            if (oiw == null) { LogisticsObserver.LogError("No ObjectInfoWindow"); return; }

            var oics = oiw.GetComponent<ObjectInfoCollapseSections>();
            if (oics == null || oics.uiLists == null || oics.uiLists.Count == 0)
            { LogisticsObserver.LogError("No ObjectInfoCollapseSections"); return; }

            var sectionParent = oics.uiLists[0].transform;
            _parentRt = sectionParent.parent as RectTransform;
            float sectionWidth = (sectionParent as RectTransform).sizeDelta.x;
            if (sectionWidth <= 0) sectionWidth = _parentRt.rect.width;

            _getSection = new LogisticsSection(_parentRt, "GET \u2014 Request Resources", _font, sectionWidth);
            _sections.Add(_getSection);

            _sendSection = new LogisticsSection(_parentRt, "SEND \u2014 Provide Resources", _font, sectionWidth);
            _sections.Add(_sendSection);

            _scSection = new LogisticsSection(_parentRt, "SPACECRAFT \u2014 Logistics Vessels", _font, sectionWidth);
            _sections.Add(_scSection);

            _lvSection = new LogisticsSection(_parentRt, "LAUNCH VEHICLE \u2014 Surface Shuttles", _font, sectionWidth);
            _sections.Add(_lvSection);

            _built = true;
            RefreshAllSections();
        }
        catch (System.Exception ex) { LogisticsObserver.LogError("Start Exception: " + ex); }
    }

    private TMP_FontAsset FindFont()
    {
        foreach (var tmp in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            if (tmp.font != null && tmp.isActiveAndEnabled)
                return tmp.font;
        return null;
    }

    public void RefreshData(ObjectInfoData oid)
    {
        var newOi = oid?.ObjectInfo;
        var newName = newOi?.ObjectName ?? "NULL";
        var newId = newOi?.id ?? -1;
        var prevName = _currentObjectInfo?.ObjectName ?? "null";
        var prevId = _currentObjectInfo?.id ?? -1;
        LogisticsObserver.Log($"RefreshData: \"{newName}\" (id={newId}), _built={_built}, prev=\"{prevName}\" (id={prevId})");

        if (newOi != null && _currentObjectInfo != null && newId == prevId && newName != prevName)
            LogisticsObserver.LogWarning($"DIAG RefreshData: SAME id ({newId}) but DIFFERENT name! prev=\"{prevName}\" new=\"{newName}\"");

        if (newOi != null)
        {
            var dictData = Data.LogisticsNetwork.Get(newOi);
            if (dictData != null)
            {
                var storedOiName = (dictData.ObjectInfo as ObjectInfo)?.ObjectName ?? "NULL";
                if (storedOiName != newName)
                    LogisticsObserver.LogWarning($"DIAG RefreshData: dict entry id={newId} has storedOI=\"{storedOiName}\" but incoming OI name=\"{newName}\" — MISMATCH!");
                LogisticsObserver.Log($"DIAG RefreshData: dict data for id={newId}: {dictData.requests.Count}req {dictData.providers.Count}prov");
            }
            else
            {
                LogisticsObserver.Log($"DIAG RefreshData: NO dict entry for id={newId} name=\"{newName}\"");
            }
        }

        _currentData = oid;
        _currentObjectInfo = newOi;
        if (!_built) return;
        RefreshAllSections();
    }

    private void RefreshAllSections()
    {
        if (_currentObjectInfo == null) return;
        BuildGetSection();
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
        var data = Data.LogisticsNetwork.GetOrCreate(_currentObjectInfo);
        LogisticsObserver.Log($"BuildGet for {_currentObjectInfo?.ObjectName}: {data.requests.Count} requests");

        if (data.requests.Count > 0)
        {
            for (int i = 0; i < data.requests.Count; i++)
            {
                var req = data.requests[i];
                var idx = i;
                var rd = req.ResourceDefinition;
                var displayName = rd != null ? rd.ID : req.resourceDef.id;
                var statusStr = StatusToString(req.status);
                var noteStr = !string.IsNullOrEmpty(req.statusNote) ? $" ({req.statusNote})" : "";

                var row = MakeHLRow(_getSection.ContentArea, 24f, 8);
                MakeTMP(row.transform, $"{displayName}: {req.requestedAmount:0.#}  [{statusStr}]{noteStr}", 13, StatusColor(req.status));
                MakeXButton(row.transform, () =>
                {
                    var capturedOi = _currentObjectInfo;
                    LogisticsObserver.Log($"X clicked on GET req idx={idx} capturedOi=\"{capturedOi?.ObjectName}\"(id={capturedOi?.id})");
                    Data.LogisticsNetwork.RemoveRequest(capturedOi, idx);
                    BuildGetSection();
                    RebuildSectionLayout(_getSection);
                });
            }
        }
        else
        {
            _getSection.AddTextRow("No resource requests configured.", _font, 13f, new Color(0.5f, 0.5f, 0.5f, 1f));
        }

        AddBigButton(_getSection.ContentArea, "+ Add Request", new Color(0.2f, 0.3f, 0.2f, 1f), () =>
        {
            ShowResourcePicker(_getSection, true);
        });
    }

    private void BuildSendSection()
    {
        _sendSection.ClearContent();
        var data = Data.LogisticsNetwork.GetOrCreate(_currentObjectInfo);

        if (data.providers.Count > 0)
        {
            for (int i = 0; i < data.providers.Count; i++)
            {
                var prov = data.providers[i];
                var idx = i;
                var rd = prov.ResourceDefinition;
                var displayName = rd != null ? rd.ID : prov.resourceDef.id;

                var row = MakeHLRow(_sendSection.ContentArea, 24f, 8);
                MakeTMP(row.transform, $"{displayName}: min keep {prov.minimumKeep:0.#}", 13, new Color(0.7f, 0.7f, 0.7f, 1f));
                MakeXButton(row.transform, () =>
                {
                    var capturedOi = _currentObjectInfo;
                    LogisticsObserver.Log($"X clicked on SEND prov idx={idx} capturedOi=\"{capturedOi?.ObjectName}\"(id={capturedOi?.id})");
                    Data.LogisticsNetwork.RemoveProvider(capturedOi, idx);
                    BuildSendSection();
                    RebuildSectionLayout(_sendSection);
                });
            }
        }
        else
        {
            _sendSection.AddTextRow("No resource exports configured.", _font, 13f, new Color(0.5f, 0.5f, 0.5f, 1f));
        }

        AddBigButton(_sendSection.ContentArea, "+ Add Provider", new Color(0.2f, 0.25f, 0.3f, 1f), () =>
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

        var typeName = isSpacecraft ? "spacecraft" : "launch vehicles";

        // LV section - no quotas, just show available types
        if (!isSpacecraft)
        {
            BuildLVSectionOnly(section);
            return;
        }

        var quotas = Data.LogisticsNetwork.GetQuotas(_currentObjectInfo, true);

        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        LogisticsObserver.GetActiveCycleCounts(player, out var scActive, out var lvActive);
        var active = isSpacecraft ? scActive : lvActive;

        if (quotas.Count > 0)
        {
            foreach (var q in quotas)
            {
                var quotaTypeName = q.typeName;
                var quotaCount = q.count;
                active.TryGetValue(quotaTypeName, out var activeCount);
                var free = quotaCount - activeCount;

                var row = MakeHLRow(section.ContentArea, 28f, 4);
                row.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.12f, 1f);

                var countColor = free > 0
                    ? new Color(0.5f, 0.9f, 0.5f, 1f)
                    : new Color(0.9f, 0.55f, 0.1f, 1f);
                var countLabel = MakeTMP(row.transform, $"{free}/{quotaCount}", 14, countColor);
                countLabel.alignment = TextAlignmentOptions.Center;
                countLabel.rectTransform.sizeDelta = new Vector2(72, 0);

                MakeTMP(row.transform, quotaTypeName, 13, new Color(0.8f, 0.8f, 0.8f, 1f));

                AddSmallButton(row.transform, "-", new Color(0.4f, 0.15f, 0.15f, 1f), () =>
                {
                    var capturedOi = _currentObjectInfo;
                    var newVal = quotaCount - 1;
                    if (newVal <= 0)
                        Data.LogisticsNetwork.RemoveQuota(capturedOi, quotaTypeName, isSpacecraft);
                    else
                        Data.LogisticsNetwork.SetQuota(capturedOi, quotaTypeName, newVal, isSpacecraft);
                    BuildShipSection(section, isSpacecraft);
                    RebuildSectionLayout(section);
                });

                AddSmallButton(row.transform, "+", new Color(0.15f, 0.4f, 0.15f, 1f), () =>
                {
                    var capturedOi = _currentObjectInfo;
                    Data.LogisticsNetwork.SetQuota(capturedOi, quotaTypeName, quotaCount + 1, isSpacecraft);
                    BuildShipSection(section, isSpacecraft);
                    RebuildSectionLayout(section);
                });
            }
        }
        else
        {
            section.AddTextRow($"No logistics {typeName} quotas set.", _font, 13f, new Color(0.5f, 0.5f, 0.5f, 1f));
        }

        AddBigButton(section.ContentArea, $"+ Add {typeName} quota", new Color(0.2f, 0.25f, 0.35f, 1f), () =>
        {
            ShowShipPicker(section, true);
        });
    }

    private void BuildLVSectionOnly(LogisticsSection section)
    {
        section.ClearContent();
        if (_currentObjectInfo == null) return;

        var typeCounts = Data.LogisticsNetwork.GetShipTypeCountsOnObject(_currentObjectInfo, false);

        if (typeCounts.Count == 0)
        {
            section.AddTextRow("No launch vehicles on this object.", _font, 13f, new Color(0.5f, 0.5f, 0.5f, 1f));
            RebuildSectionLayout(section);
            return;
        }

        section.AddTextRow("Click to toggle:", _font, 12f, new Color(0.5f, 0.5f, 0.6f, 1f));

        foreach (var kv in typeCounts)
        {
            var lvTypeName = kv.Key;
            var count = kv.Value;

            // Check if this LV type is "enabled" (has quota > 0)
            var currentQuota = Data.LogisticsNetwork.GetQuota(_currentObjectInfo, lvTypeName, false);
            var isEnabled = currentQuota > 0;

            var row = MakeHLRow(section.ContentArea, 26f, 4);
            row.GetComponent<Image>().color = isEnabled ? new Color(0.1f, 0.25f, 0.15f, 1f) : new Color(0.15f, 0.15f, 0.2f, 1f);

            var activeColor = isEnabled ? new Color(0.5f, 0.9f, 0.6f, 1f) : new Color(0.6f, 0.6f, 0.65f, 1f);
            MakeTMP(row.transform, $"{lvTypeName}  x{count}", 13, activeColor);

            var statusText = isEnabled ? "ON" : "OFF";
            var statusColor = isEnabled ? new Color(0.5f, 0.9f, 0.5f, 1f) : new Color(0.4f, 0.4f, 0.45f, 1f);
            MakeTMP(row.transform, statusText, 11, statusColor);

            // Click to toggle
            var btn = row.AddComponent<Button>();
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var capturedOi = _currentObjectInfo;
            var capturedType = lvTypeName;
            btn.onClick.AddListener(() =>
            {
                if (currentQuota > 0)
                    Data.LogisticsNetwork.RemoveQuota(capturedOi, capturedType, false);
                else
                    Data.LogisticsNetwork.SetQuota(capturedOi, capturedType, 1, false);
                BuildShipSection(section, false);
                RebuildSectionLayout(section);
            });
        }

        RebuildSectionLayout(section);
    }

    private void ShowShipPicker(LogisticsSection section, bool isSpacecraft)
    {
        section.ClearContent();

        AddBigButton(section.ContentArea, "\u2190 Back", new Color(0.25f, 0.15f, 0.15f, 1f), () =>
        {
            if (isSpacecraft) BuildSCSection(); else BuildLVSection();
            RebuildSectionLayout(section);
        });

        if (_currentObjectInfo == null)
        {
            section.AddTextRow("No object selected.", _font);
            RebuildSectionLayout(section);
            return;
        }

        var typeName = isSpacecraft ? "spacecraft" : "launch vehicles";
        var typeCounts = Data.LogisticsNetwork.GetShipTypeCountsOnObject(_currentObjectInfo, isSpacecraft);

        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        LogisticsObserver.GetActiveCycleCounts(player, out var scActive, out var lvActive);
        var active = isSpacecraft ? scActive : lvActive;

        if (typeCounts.Count == 0)
        {
            section.AddTextRow($"No {typeName} found on this object.", _font);
            RebuildSectionLayout(section);
            return;
        }

        foreach (var kv in typeCounts)
        {
            var shipTypeName = kv.Key;
            var totalCount = kv.Value;
            var currentQuota = Data.LogisticsNetwork.GetQuota(_currentObjectInfo, shipTypeName, isSpacecraft);
            active.TryGetValue(shipTypeName, out var activeCount);
            var freeQuota = (currentQuota > 0) ? $"{currentQuota - activeCount}/{currentQuota}" : "0";
            var displayQuota = currentQuota > 0 ? $"quota: {freeQuota}" : "no quota";

            var row = MakeHLRow(section.ContentArea, 26f, 4);
            row.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.2f, 1f);

            MakeTMP(row.transform, $"{shipTypeName}  {totalCount} available ({displayQuota})", 13, new Color(0.8f, 0.8f, 0.8f, 1f));

            AddSmallButton(row.transform, "+", new Color(0.15f, 0.4f, 0.15f, 1f), () =>
            {
                var capturedOi = _currentObjectInfo;
                Data.LogisticsNetwork.SetQuota(capturedOi, shipTypeName, currentQuota + 1, isSpacecraft);
                if (isSpacecraft) BuildSCSection(); else BuildLVSection();
                RebuildSectionLayout(section);
            });
        }

        RebuildSectionLayout(section);
    }

    private void ShowResourcePicker(LogisticsSection section, bool isGet)
    {
        section.ClearContent();

        AddBigButton(section.ContentArea, "\u2190 Back", new Color(0.25f, 0.15f, 0.15f, 1f), () =>
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
                available = Data.LogisticsNetwork.GetNetworkResourcesSet(player);
            else
                available = Data.LogisticsNetwork.GetAvailableResourcesOnObject(_currentObjectInfo, player);
        }
        else
        {
            available = new HashSet<ResourceDefinition>();
        }

        foreach (var rd in am.AllResourceDefinitions.ListNotEmpty)
        {
            var rdCaptured = rd;
            var sectionRef = section;
            var isGetCaptured = isGet;
            var isAvailable = available.Contains(rd);

            var row = MakeHLRow(section.ContentArea, 24f, 0);
            row.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.15f, 1f);
            var color = isAvailable ? new Color(0.8f, 0.8f, 0.8f, 1f) : new Color(0.35f, 0.35f, 0.35f, 1f);
            var label = isAvailable ? rd.ID : $"{rd.ID} (not available)";
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
        LogisticsObserver.Log($"ShowAmountInput: rd={rd.ID} isGet={isGet} capturedOi=\"{capturedOi?.ObjectName}\"(id={capturedOi?.id})");
        _inputConfirmed = false;
        double currentAmount = 0;
        section.ClearContent();

        AddBigButton(section.ContentArea, "\u2190 Back to resources", new Color(0.25f, 0.15f, 0.15f, 1f), () =>
        {
            ShowResourcePicker(section, isGet);
        });

        if (!isAvailable)
        {
            var warnTmp = MakeTMP(section.ContentArea, "WARNING: Resource not currently available", 12, new Color(0.9f, 0.6f, 0.1f, 1f));
            warnTmp.rectTransform.sizeDelta = new Vector2(0, 20);
        }

        var titleLabel = MakeTMP(section.ContentArea, $"{(isGet ? "Request" : "Provide")}: {rd.ID}", 14, new Color(0.9f, 0.9f, 0.5f, 1f));
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
        AddSmallButton(plusRow.transform, "+10", new Color(0.16f, 0.38f, 0.16f, 1f), () => { currentAmount += 10; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+100", new Color(0.14f, 0.42f, 0.14f, 1f), () => { currentAmount += 100; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+1K", new Color(0.14f, 0.42f, 0.14f, 1f), () => { currentAmount += 1000; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+10K", new Color(0.12f, 0.45f, 0.12f, 1f), () => { currentAmount += 10000; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+100K", new Color(0.12f, 0.45f, 0.12f, 1f), () => { currentAmount += 100000; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+1M", new Color(0.12f, 0.45f, 0.12f, 1f), () => { currentAmount += 1000000; UpdateAmountDisplay(); });

        var minusRow = MakeHLRow(section.ContentArea, 28f, 4);
        AddSmallButton(minusRow.transform, "\u221210", new Color(0.38f, 0.16f, 0.16f, 1f), () => { currentAmount = System.Math.Max(0, currentAmount - 10); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u2212100", new Color(0.42f, 0.14f, 0.14f, 1f), () => { currentAmount = System.Math.Max(0, currentAmount - 100); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u22121K", new Color(0.42f, 0.14f, 0.14f, 1f), () => { currentAmount = System.Math.Max(0, currentAmount - 1000); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u221210K", new Color(0.45f, 0.12f, 0.12f, 1f), () => { currentAmount = System.Math.Max(0, currentAmount - 10000); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u2212100K", new Color(0.45f, 0.12f, 0.12f, 1f), () => { currentAmount = System.Math.Max(0, currentAmount - 100000); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u22121M", new Color(0.45f, 0.12f, 0.12f, 1f), () => { currentAmount = System.Math.Max(0, currentAmount - 1000000); UpdateAmountDisplay(); });

        void DoConfirm()
        {
            if (_inputConfirmed) return;
            _inputConfirmed = true;
            if (currentAmount > 0)
            {
                if (isGet)
                    Data.LogisticsNetwork.AddRequest(capturedOi, rd, currentAmount);
                else
                    Data.LogisticsNetwork.AddProvider(capturedOi, rd, currentAmount);
            }
            if (isGet) BuildGetSection(); else BuildSendSection();
            RebuildSectionLayout(section);
        }

        var confirmRow = new GameObject("ConfirmRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        confirmRow.transform.SetParent(section.ContentArea, false);
        confirmRow.GetComponent<LayoutElement>().preferredHeight = 32f;
        var crHLG = confirmRow.GetComponent<HorizontalLayoutGroup>();
        crHLG.spacing = 8;

        AddBigButtonInline(confirmRow.transform, "Confirm", new Color(0.2f, 0.4f, 0.2f, 1f), () => DoConfirm());
        AddBigButtonInline(confirmRow.transform, "Cancel", new Color(0.3f, 0.15f, 0.15f, 1f), () =>
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
        row.GetComponent<Image>().color = new Color(0, 0, 0, 0);
        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.spacing = spacing;
        hlg.padding = new RectOffset(4, 4, 0, 0);
        return row;
    }

    private void MakeXButton(Transform parent, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject("XBtn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(parent, false);
        btnGo.GetComponent<LayoutElement>().preferredWidth = 24f;
        btnGo.GetComponent<Image>().color = new Color(0.4f, 0.15f, 0.15f, 1f);
        var btn = btnGo.GetComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var tmp = MakeTMP(btnGo.transform, "X", 12, Color.white);
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
        btnGo.GetComponent<LayoutElement>().preferredHeight = 28f;
        btnGo.GetComponent<Image>().color = color;
        var btn = btnGo.GetComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };

        var labelTmp = MakeTMP(btnGo.transform, text, 14, new Color(0.85f, 0.85f, 0.85f, 1f));
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

        var labelTmp = MakeTMP(btnGo.transform, text, 12, new Color(0.85f, 0.85f, 0.85f, 1f));
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
}
