using HarmonyLib;
using Game.UI.Windows.Elements.PlanMissionElements;

namespace LogisticsMod.Patches;

[HarmonyPatch]
internal static class SpaceCraftCyclicalMissionControllerPatches
{
    [HarmonyPatch(typeof(SpaceCraftCyclicalMissionController), nameof(SpaceCraftCyclicalMissionController.TryPlanCycleMission))]
    [HarmonyPrefix]
    private static bool TryPlanCycleMissionPrefix(SpaceCraftCyclicalMissionController __instance)
    {
        var cmd = __instance.CycleMissionsData;
        if (cmd == null) return true;
        if (cmd.customNameFromPlanMission.StartsWith("[LOGI]") && __instance.CycleMissionPlanFlyWas) return false;
        return true;
    }
}
