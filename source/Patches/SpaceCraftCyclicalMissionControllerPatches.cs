using System.Collections.Generic;
using Data;
using HarmonyLib;
using CustomUpdate;
using Game.Info;
using Game.UI.Windows.Elements.PlanMissionElements;
using Game.UI.Windows.Windows;
using Manager;

namespace LogisticsMod.Patches;

[HarmonyPatch]
internal static class SpaceCraftCyclicalMissionControllerPatches
{
    internal static Dictionary<string, double> LogiLoadLimit = new Dictionary<string, double>();
    private static bool _reapplyingLoadLimit = false;

    internal static void SetLogiLoadLimit(string loadLimitKey, double limit)
    {
        LogiLoadLimit[loadLimitKey] = limit;
    }

    internal static void ClearLogiLoadLimit(string loadLimitKey)
    {
        LogiLoadLimit.Remove(loadLimitKey);
    }

    private static string GetLogiLoadLimitKey(CycleMissionsData cmd)
    {
        var rd = (cmd.cargoAllStart?.Tab != null && cmd.cargoAllStart.Tab.Length > 0)
            ? cmd.cargoAllStart.Tab[0] : null;
        return cmd.customNameFromPlanMission + "_" + (rd?.Name ?? "");
    }

    [HarmonyPatch(typeof(SpaceCraftCyclicalMissionController), nameof(SpaceCraftCyclicalMissionController.TryPlanCycleMission))]
    [HarmonyPrefix]
    private static bool TryPlanCycleMissionPrefix(SpaceCraftCyclicalMissionController __instance, double? loadLimit2)
    {
        var cmd = __instance.CycleMissionsData;
        if (cmd == null) return true;

        if (cmd.customNameFromPlanMission.StartsWith("[LOGI]"))
        {
            if (__instance.CycleMissionPlanFlyWas)
                return false;

            if (loadLimit2 == null && !_reapplyingLoadLimit)
            {
                var key = GetLogiLoadLimitKey(cmd);
                if (LogiLoadLimit.TryGetValue(key, out var storedLimit))
                {
                    if (cmd.CheckComplete())
                        return false;

                    _reapplyingLoadLimit = true;
                    __instance.TryPlanCycleMission(loadLimit2: storedLimit);
                    _reapplyingLoadLimit = false;
                    return false;
                }
            }
        }
        return true;
    }

    // DISABLED: ShowNotification fires during normal planning, which prematurely
    // removes the cycle mission and breaks the return leg (B→A).
    // Stuck/failed cycle missions are handled by CleanupStuckMissions on each OnDayChange.
    //[HarmonyPatch(typeof(SpaceCraftCyclicalMissionController), nameof(SpaceCraftCyclicalMissionController.ShowNotification))]
    //[HarmonyPostfix]
    //private static void ShowNotificationPostfix(SpaceCraftCyclicalMissionController __instance)
    //{
    //    var cmd = __instance.CycleMissionsData;
    //    if (cmd == null) return;
    //    if (!cmd.customNameFromPlanMission.StartsWith("[LOGI]")) return;
    //
    //    var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
    //    if (cmd.ListSC != null)
    //    {
    //        foreach (var sci in cmd.ListSC)
    //        {
    //            if (sci is Spacecraft sc)
    //            {
    //                cm.RemoveCycleMission(sc);
    //                ClearLogiLoadLimit(GetLogiLoadLimitKey(cmd));
    //            }
    //        }
    //    }
    //
    //    var requester = cmd.B;
    //    if (requester != null)
    //    {
    //        var reqData = Data.LogisticsNetwork.Get(requester);
    //        if (reqData != null && cmd.cargoAllStart?.Tab != null)
    //        {
    //            foreach (var res in cmd.cargoAllStart.Tab)
    //            {
    //                foreach (var req in reqData.requests)
    //                {
    //                    if (req.ResourceDefinition == res && req.status == Data.LogisticsRequestStatus.InProgress)
    //                        req.status = Data.LogisticsRequestStatus.Pending;
    //                }
    //            }
    //        }
    //    }
    //}

    [HarmonyPatch(typeof(PMTabSchedule), nameof(PMTabSchedule.OnClickScheduleButtonForCode))]
    [HarmonyPostfix]
    private static void OnClickScheduleButtonForCodePostfix(PMTabSchedule __instance, ref MissionInfo __result)
    {
        if (__result != null)
        {
            string restoredName = null;

            // For cyclical missions: restore [LOGI] name from CycleMissionsData.customNameFromPlanMission
            // (game overwrites it to "Cyclical missions N" in TryPlanCycleMission)
            if (__result.spacecraftInfo2 is Spacecraft sc)
            {
                var cmd = MonoBehaviourSingleton<CycleMissionManager>.Instance?.GetCycleMission(sc);
                if (cmd?.customNameFromPlanMission.StartsWith("[LOGI]") == true)
                {
                    __result.missionName = cmd.customNameFromPlanMission;
                    __result.fromCyclicalMission = true;
                    restoredName = cmd.customNameFromPlanMission;
                    Logic.LogisticsObserver.Log($"[SCP] restored LOGI name: \"{restoredName}\"");
                }
            }

            // For one-shot catapult missions: ensure fromCyclicalMission flag is set
            if (restoredName == null && __result.missionName.StartsWith("[LOGI]"))
            {
                __result.fromCyclicalMission = true;
                Logic.LogisticsObserver.Log($"[SCP] set fromCyclicalMission=true for LOGI mission \"{__result.missionName}\"");
            }

            Logic.LogisticsObserver.Log($"[SCP] mission created ID={__result.id} name=\"{__result.missionName}\"");
        }
        else
            Logic.LogisticsObserver.Log($"[SCP] FAILED — returned null");
    }

    // Protect [LOGI] mission names from auto-generation in ChangeMissionName()
    // Game overwrites to "Destination N" when stage changes to Schedule
    [HarmonyPatch(typeof(PMTabDestination), nameof(PMTabDestination.ChangeMissionName), new System.Type[] { })]
    [HarmonyPrefix]
    private static bool ChangeMissionNamePrefix(PMTabDestination __instance)
    {
        var pmw = HarmonyLib.Traverse.Create(__instance).Field("planMissionWindow").GetValue<PlanMissionWindow>();
        if (pmw?.MissionInfo?.missionName?.StartsWith("[LOGI]") == true)
        {
            return false;
        }
        return true;
    }

    // Force full fuel tank + fastest route for LOGI cyclical missions.
    // Vanilla TryPlanCycleMission sets ReduceFuelToMinimum=true (line 231),
    // which overrides the fuel slider to minimum fuel → low effective dV →
    // ButtonFastestClickButton can't find the truly fastest trajectory.
    // This prefix runs before PlanFlyCode queues the final async planning
    // cycle, ensuring the SECOND porkchop compute uses full fuel + fastest.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.PlanFlyCode))]
    [HarmonyPrefix]
    private static void PlanFlyCodePrefix(PMMissionParameter missionParameter)
    {
        if (missionParameter?.ForCyclicalMission != true || missionParameter.SCList?.Count <= 0)
            return;

        var sc = missionParameter.SCList[0] as Spacecraft;
        if (sc == null) return;

        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        var cmd = cm?.GetCycleMission(sc);
        if (cmd?.customNameFromPlanMission?.StartsWith("[LOGI]") != true)
            return;

        missionParameter.ReduceFuelToMinimum = false;
        missionParameter.ClickFastestButton = true;
        if (missionParameter.MoonCase)
            missionParameter.TransferTypeMoonCase = ETransferType.Fastest;
    }

    // Restore [LOGI] name before CreateMissionInfo (safety net if ChangeMissionName bypassed)
    [HarmonyPatch(typeof(PMTabSchedule), "CreateFly")]
    [HarmonyPrefix]
    private static void CreateFlyPrefix(PMTabSchedule __instance)
    {
        var pmw = Traverse.Create(__instance).Field("planMissionWindow").GetValue<PlanMissionWindow>();
        var ppm = pmw?.PMMissionParameter;
        if (ppm == null) return;

        var logiName = ppm.MissionName;
        if (!string.IsNullOrEmpty(logiName) && logiName.StartsWith("[LOGI]"))
        {
            ppm.ChangeMissionName(logiName, _manualChangeName: true);
            Logic.LogisticsObserver.Log($"[SCP] CreateFly: restored LOGI name \"{logiName}\"");
        }
    }
}
