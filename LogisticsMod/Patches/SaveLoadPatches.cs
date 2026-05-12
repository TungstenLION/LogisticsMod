using System.Linq;
using HarmonyLib;
using Game;
using Game.Info;
using Game.UI.Windows.Elements.PlanMissionElements;
using LogisticsMod.Logic;
using Manager;

namespace LogisticsMod.Patches;

[HarmonyPatch]
internal static class SaveLoadPatches
{
    [HarmonyPatch(typeof(LoadSaveManager), "SaveToFile", new[] { typeof(string) })]
    [HarmonyPostfix]
    private static void SaveToFilePostfix(string saveName)
    {
        Data.LogisticsPersistence.Save(saveName);
    }

    [HarmonyPatch(typeof(LoadSaveManager), "ExtractAllFromSaveData")]
    [HarmonyPostfix]
    private static void ExtractAllPostfix()
    {
        var saveName = SerializedMonoBehaviourSingleton<LoadSaveManager>.Instance?.LastSaveName;
        if (!string.IsNullOrEmpty(saveName))
            Data.LogisticsPersistence.Load(saveName);

        ReconcileAfterLoad();
    }

    private static void ReconcileAfterLoad()
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (player == null || cm == null) return;

        MatchCyclesToRequests(player, cm);
        MatchMissionsToRequests(player);
        _pendingPostLoadTrigger = true;
    }

    public static bool PendingPostLoadTrigger
    {
        get => _pendingPostLoadTrigger;
        set => _pendingPostLoadTrigger = value;
    }
    private static bool _pendingPostLoadTrigger;

    private static void MatchCyclesToRequests(Company player, CycleMissionManager cm)
    {
        foreach (var requesterOI in Data.LogisticsNetwork.GetAllObjects())
        {
            var reqData = Data.LogisticsNetwork.Get(requesterOI);
            if (reqData == null) continue;

            foreach (var cmd in cm.GetAllCycleMission(player))
            {
                if (cmd.CheckComplete()) continue;
                if (cmd.B != requesterOI) continue;
                if (!cmd.customNameFromPlanMission.StartsWith("[LOGI]")) continue;
                if (cmd.cargoAllStart?.Tab == null) continue;

                foreach (var tabRes in cmd.cargoAllStart.Tab)
                {
                    foreach (var req in reqData.requests)
                    {
                        if (req.status != Data.LogisticsRequestStatus.Pending
                            && req.status != Data.LogisticsRequestStatus.InProgress)
                            continue;
                        if (req.ResourceDefinition == tabRes)
                        {
                            req.status = Data.LogisticsRequestStatus.InProgress;
                            LogisticsObserver.Log($"Reconciled cycle: {cmd.A?.ObjectName} -> {cmd.B?.ObjectName} ({tabRes.ID})");
                            break;
                        }
                    }
                }
            }
        }
    }

    // Backward compat: reconcile any leftover one-shot [LOGI] missions from older save data.
    // One-shot LOGI missions are still actively created by magnetic catapults (FakeForFacility).
    // Catapults cannot use cyclical missions — see comment in TryCreateDeliveries for details.
    // Existing saves may also have pre-catapult one-shot missions that need reconciliation.
    private static void MatchMissionsToRequests(Company player)
    {
        var mim = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        if (mim == null) return;

        foreach (var requesterOI in Data.LogisticsNetwork.GetAllObjects())
        {
            var reqData = Data.LogisticsNetwork.Get(requesterOI);
            if (reqData == null) continue;

            foreach (var mi in mim.ListMissionInfo)
            {
                if (mi.complete || mi.cancel) continue;
                if (!mi.missionName.StartsWith("[LOGI]")) continue;
                if (mi.target != requesterOI) continue;
                if (mi.cargoAll?.listCargo == null) continue;

                foreach (var cargo in mi.cargoAll.listCargo)
                {
                    foreach (var req in reqData.requests)
                    {
                        if (req.status != Data.LogisticsRequestStatus.Pending
                            && req.status != Data.LogisticsRequestStatus.InProgress)
                            continue;
                        if (req.ResourceDefinition == cargo.resourceType)
                        {
                            req.status = Data.LogisticsRequestStatus.InProgress;
                            LogisticsObserver.Log($"Reconciled one-time: {mi.missionName} ({cargo.resourceType.ID})");
                            break;
                        }
                    }
                }
            }
        }
    }
}
