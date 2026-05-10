using HarmonyLib;
using Game.UI.Windows.Windows;
using LogisticsMod.Logic;
using UnityEngine;

namespace LogisticsMod.Patches;

[HarmonyPatch]
internal static class ObjectInfoWindowPatches
{
    [HarmonyPatch(typeof(ObjectInfoWindow), "Awake")]
    [HarmonyPostfix]
    private static void AwakePostfix(ObjectInfoWindow __instance)
    {
        if (__instance == null) return;
        if (__instance.GetComponent<UI.LogisticsUI>() != null) return;
        __instance.gameObject.AddComponent<UI.LogisticsUI>();
    }

    [HarmonyPatch(typeof(ObjectInfoWindow), "SetData", new[] { typeof(Game.ObjectInfoDataScripts.ObjectInfoData), typeof(bool) })]
    [HarmonyPostfix]
    private static void SetDataPostfix(ObjectInfoWindow __instance, Game.ObjectInfoDataScripts.ObjectInfoData objectInfoData, bool fromObjectName)
    {
        var oi = objectInfoData?.ObjectInfo;
        var nameStr = oi?.ObjectName ?? "NULL";
        var idStr = oi?.id ?? -1;
        LogisticsObserver.Log($"DIAG SetData: OIW={__instance.GetInstanceID()} obj=\"{nameStr}\" id={idStr} fromObjectName={fromObjectName}");

        var l = __instance.GetComponent<UI.LogisticsUI>();
        if (l != null && l.isActiveAndEnabled)
            l.RefreshData(objectInfoData);
        else
            LogisticsObserver.LogWarning($"DIAG SetData: LogisticsUI null or disabled on OIW={__instance.GetInstanceID()}");
    }

    [HarmonyPatch(typeof(ObjectInfoWindow), "RebuildLayout")]
    [HarmonyPostfix]
    private static void RebuildLayoutPostfix(ObjectInfoWindow __instance)
    {
        var l = __instance.GetComponent<UI.LogisticsUI>();
        if (l != null && l.isActiveAndEnabled)
            l.RebuildLayout();
    }
}
