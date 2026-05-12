using System.Collections.Generic;
using HarmonyLib;
using CustomUpdate;
using Game.Info;
using Game.UI.Windows.Elements.PlanMissionElements;
using Manager;
using LogisticsMod.Logic;

namespace LogisticsMod.Patches;

[HarmonyPatch]
internal static class SpaceCraftCyclicalMissionControllerPatches
{
    internal static Dictionary<string, double> LogiLoadLimit = new Dictionary<string, double>();
    private static bool _reapplyingLoadLimit = false;

    internal static void SetLogiLoadLimit(string missionName, double limit)
    {
        LogiLoadLimit[missionName] = limit;
    }

    internal static void ClearLogiLoadLimit(string missionName)
    {
        LogiLoadLimit.Remove(missionName);
    }

    [HarmonyPatch(typeof(SpaceCraftCyclicalMissionController), nameof(SpaceCraftCyclicalMissionController.TryPlanCycleMission))]
    [HarmonyPrefix]
    private static bool TryPlanCycleMissionPrefix(SpaceCraftCyclicalMissionController __instance, double? loadLimit2)
    {
        var cmd = __instance.CycleMissionsData;
        if (cmd == null) return true;

        if (cmd.customNameFromPlanMission.StartsWith("[LOGI]"))
        {
            LogisticsObserver.Log($"TryPlanCycleMission[LOGI]: name=\"{cmd.customNameFromPlanMission}\" loadLimit2={loadLimit2?.ToString() ?? "null"} flyWas={__instance.CycleMissionPlanFlyWas}");

            if (__instance.CycleMissionPlanFlyWas)
            {
                return false;
            }

            if (loadLimit2 == null && LogiLoadLimit.TryGetValue(cmd.customNameFromPlanMission, out var storedLimit) && !_reapplyingLoadLimit)
            {
                if (cmd.CheckComplete())
                {
                    LogisticsObserver.Log($"TryPlanCycleMission[LOGI]: cycle already complete, skipping reapply");
                    return false;
                }

                _reapplyingLoadLimit = true;
                LogisticsObserver.Log($"TryPlanCycleMission[LOGI]: reapplying stored loadLimit2={storedLimit}");
                __instance.TryPlanCycleMission(loadLimit2: storedLimit);
                _reapplyingLoadLimit = false;
                return false;
            }
        }
        return true;
    }

    [HarmonyPatch(typeof(SpaceCraftCyclicalMissionController), nameof(SpaceCraftCyclicalMissionController.ShowNotification))]
    [HarmonyPostfix]
    private static void ShowNotificationPostfix(SpaceCraftCyclicalMissionController __instance)
    {
        var cmd = __instance.CycleMissionsData;
        if (cmd == null) return;
        if (!cmd.customNameFromPlanMission.StartsWith("[LOGI]")) return;

        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cmd.ListSC != null)
        {
            foreach (var sci in cmd.ListSC)
            {
                if (sci is Spacecraft sc)
                    cm.RemoveCycleMission(sc);
            }
        }

        var requester = cmd.B;
        if (requester != null)
        {
            var reqData = Data.LogisticsNetwork.Get(requester);
            if (reqData != null && cmd.cargoAllStart?.Tab != null)
            {
                foreach (var res in cmd.cargoAllStart.Tab)
                {
                    foreach (var req in reqData.requests)
                    {
                        if (req.ResourceDefinition == res && req.status == Data.LogisticsRequestStatus.InProgress)
                        {
                            LogisticsObserver.Log($"CLEANUP: {res.ID} to {requester.ObjectName} — mission not feasible");
                            req.status = Data.LogisticsRequestStatus.Pending;
                        }
                    }
                }
            }
        }
    }
}
