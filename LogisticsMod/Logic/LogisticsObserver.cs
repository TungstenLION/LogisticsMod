using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomUpdate;
using Data.ScriptableObject;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.UI.Windows.Elements.PlanMissionElements;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;

namespace LogisticsMod.Logic;

public static class LogisticsObserver
{
    private static StreamWriter _logWriter;
    private static int _logSession;

    public static void Log(string msg)
    {
        WriteLog("", msg);
        Debug.Log("[LogisticsMod] " + msg);
    }

    public static void LogWarning(string msg)
    {
        WriteLog("[WARN] ", msg);
        Debug.LogWarning("[LogisticsMod] " + msg);
    }

    public static void LogError(string msg)
    {
        WriteLog("[ERROR] ", msg);
        Debug.LogError("[LogisticsMod] " + msg);
    }

    private static void WriteLog(string level, string msg)
    {
        if (_logWriter == null)
        {
            _logSession++;
            var path = Path.Combine(Application.dataPath, "..", "BepInEx", $"LogisticsMod_{_logSession}.log");
            _logWriter = new StreamWriter(path, false) { AutoFlush = true };
            _logWriter.WriteLine($"=== {DateTime.Now} session={_logSession} ===");
        }
        var line = $"[{DateTime.Now:HH:mm:ss}] {level}{msg}";
        _logWriter.WriteLine(line);
    }

    public static void OnDayChange(double days)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (player == null) return;

        var networkResources = Data.LogisticsNetwork.GetNetworkResourcesSet(player);

        foreach (var requesterOI in Data.LogisticsNetwork.GetAllObjects())
        {
            var reqData = Data.LogisticsNetwork.Get(requesterOI);
            if (reqData == null) continue;

            foreach (var req in reqData.requests)
            {
                var rd = req.ResourceDefinition;

                if (req.status == Data.LogisticsRequestStatus.Satisfied
                    || req.status == Data.LogisticsRequestStatus.Failed)
                {
                    if (rd != null)
                    {
                        var currentCount = requesterOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
                        if (currentCount < req.requestedAmount)
                        {
                            Log($"REOPEN: {rd.ID} on {requesterOI?.ObjectName} ({currentCount}/{req.requestedAmount})");
                            req.status = Data.LogisticsRequestStatus.Pending;
                        }
                    }
                    if (req.status == Data.LogisticsRequestStatus.Satisfied
                        || req.status == Data.LogisticsRequestStatus.Failed)
                    {
                        req.statusNote = null;
                        continue;
                    }
                }

                if (req.status == Data.LogisticsRequestStatus.Pending)
                    req.statusNote = (rd != null && networkResources.Contains(rd)) ? null : "No provider in network";
                else
                    req.statusNote = null;
                if (rd == null) continue;

                var alreadyThere = requesterOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
                if (alreadyThere >= req.requestedAmount)
                {
                    if (req.status != Data.LogisticsRequestStatus.Satisfied)
                        Log($"SATISFIED: {rd.ID} on {requesterOI?.ObjectName} ({alreadyThere}/{req.requestedAmount})");
                    req.status = Data.LogisticsRequestStatus.Satisfied;
                    continue;
                }

                bool hasActiveDelivery = HasActiveCycleDelivering(requesterOI, rd, player);
                if (hasActiveDelivery)
                {
                    req.status = Data.LogisticsRequestStatus.InProgress;
                    continue;
                }

                double remaining = req.requestedAmount - alreadyThere;
                TryCreateDeliveries(req, requesterOI, rd, remaining, player);
            }
        }
    }

    private static bool HasActiveCycleDelivering(ObjectInfo requester, ResourceDefinition rd, Company player)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null) return false;

        foreach (var cmd in cm.GetAllCycleMission(player))
        {
            if (cmd.B != requester) continue;
            if (cmd.CheckComplete()) continue;
            if (cmd.cargoAllStart?.Tab == null) continue;

            foreach (var tabRes in cmd.cargoAllStart.Tab)
            {
                if (tabRes == rd)
                    return true;
            }
        }
        return false;
    }

    public static void GetActiveCycleCounts(Company player,
        out Dictionary<string, int> scActive, out Dictionary<string, int> lvActive)
    {
        CountActiveLogisticsCycles(player, out scActive, out lvActive);
    }

    private static void CountActiveLogisticsCycles(Company player,
        out Dictionary<string, int> scActive, out Dictionary<string, int> lvActive)
    {
        scActive = new Dictionary<string, int>();
        lvActive = new Dictionary<string, int>();
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null) return;

        foreach (var cmd in cm.GetAllCycleMission(player))
        {
            if (cmd.CheckComplete()) continue;
            if (!cmd.customNameFromPlanMission.StartsWith("[LOGI]")) continue;
            if (cmd.ListSC == null) continue;

            foreach (var sci in cmd.ListSC)
            {
                var sc = sci as Spacecraft;
                if (sc == null || sc.spacecraftType == null) continue;
                var tn = sc.spacecraftType.NameRocketType ?? "SC";
                if (!scActive.ContainsKey(tn)) scActive[tn] = 0;
                scActive[tn]++;
            }

            if (cmd.LvTypeA?.Name != null)
            {
                if (!lvActive.ContainsKey(cmd.LvTypeA.Name)) lvActive[cmd.LvTypeA.Name] = 0;
                lvActive[cmd.LvTypeA.Name]++;
            }
            if (cmd.LvTypeB?.Name != null)
            {
                if (!lvActive.ContainsKey(cmd.LvTypeB.Name)) lvActive[cmd.LvTypeB.Name] = 0;
                lvActive[cmd.LvTypeB.Name]++;
            }
        }
    }

    private static void TryCreateDeliveries(Data.LogisticsRequest req, ObjectInfo requester,
        ResourceDefinition rd, double remaining, Company player)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null) return;

        CountActiveLogisticsCycles(player, out var scActive, out var lvActive);
        bool anyProviderWithStock = false;

        foreach (var providerOI in Data.LogisticsNetwork.GetAllObjects())
        {
            if (providerOI == requester) continue;

            var provData = Data.LogisticsNetwork.Get(providerOI);
            if (provData == null) continue;

            if (!provData.providers.Any(p => p.isActive && p.ResourceDefinition == rd))
                continue;

            var oid = providerOI.GetObjectInfoData(player);
            if (oid == null) continue;

            var available = oid.CheckResources(rd);
            var minKeep = provData.providers.Where(p => p.isActive && p.ResourceDefinition == rd).Sum(p => p.minimumKeep);
            available -= minKeep;
            if (available <= 0) continue;

            anyProviderWithStock = true;
            var toDeliver = Math.Min(available, remaining);

            foreach (var quota in provData.spacecraftQuota)
            {
                if (quota.count <= 0) continue;
                scActive.TryGetValue(quota.typeName, out var activeOfType);
                var canUse = quota.count - activeOfType;
                if (canUse <= 0) continue;

                var idleSC = UnityEngine.Object.FindObjectsOfType<Spacecraft>()
                    .Where(sc => sc != null && sc.spacecraftType != null
                        && sc.CurrentlyOnThisObject == providerOI
                        && sc.CurrentPhase == Spacecraft.EPhase.None
                        && sc.spacecraftType.NameRocketType == quota.typeName
                        && cm.GetCycleMission(sc) == null)
                    .Take(canUse)
                    .ToList();

                if (idleSC.Count > 0)
                {
                    var cap = idleSC[0].spacecraftType.GetCargoCapacity(player);
                    if (cap > 0)
                    {
                        var needed = Math.Min(idleSC.Count, (int)Math.Ceiling(toDeliver / cap));
                        var used = idleSC.Take(needed).ToList();
                        SetupCycleMission(req, used, rd, toDeliver, requester, providerOI);
                        Log($"PROC: {rd.ID} x{toDeliver} ({used.Count}ships from quota {quota.typeName}) {providerOI?.ObjectName} -> {requester?.ObjectName}");
                        return;
                    }
                }
            }

                // LV delivery - use enabled LV types only (quota > 0 means enabled)
                var enabledLVTypes = provData.launchVehicleQuota
                    .Where(q => q.count > 0)
                    .Select(q => q.typeName)
                    .ToHashSet();

                if (enabledLVTypes.Count == 0) continue;

                var availableLV = UnityEngine.Object.FindObjectsOfType<LaunchVehicle>()
                    .Where(lv => lv != null && lv.launchVehicleType != null
                        && lv.objectInfo == providerOI
                        && lv.IsReadyToLaunchReusable()
                        && enabledLVTypes.Contains(lv.launchVehicleType.Name))
                    .ToList();

                if (availableLV.Count > 0)
                {
                    var lvType = availableLV[0].launchVehicleType;

                    // Check if target is the orbit of the same body (surface -> orbit case)
                    var targetIsOrbitOfProvider = requester != null 
                        && requester.LowOrbitCustom != null 
                        && requester.LowOrbitCustom.GetObjectInfo() == providerOI;

                    Spacecraft scOnOrbit = null;

                    if (targetIsOrbitOfProvider)
                    {
                        // Surface -> orbit: use container only
                        scOnOrbit = MonoBehaviourSingleton<ShipManager>.Instance?.GetLowOrbitContainer(player);
                    }
                    else
                    {
                        // Surface -> other body: MUST use regular SC, no container fallback!
                        scOnOrbit = UnityEngine.Object.FindObjectsOfType<Spacecraft>()
                            .Where(sc => sc != null && sc.spacecraftType != null
                                && sc.CurrentlyOnThisObject == providerOI
                                && sc.CurrentPhase == Spacecraft.EPhase.None
                                && !sc.spacecraftType.LowOrbitContainer
                                && MonoBehaviourSingleton<CycleMissionManager>.Instance.GetCycleMission(sc) == null)
                            .FirstOrDefault();
                    }

                    if (scOnOrbit != null)
                    {
                        SetupCycleMission(req, scOnOrbit, rd, toDeliver, requester, providerOI, lvType);
                        Log($"PROC: {rd.ID} x{toDeliver} (LV {lvType.Name}) {providerOI?.ObjectName} -> {requester?.ObjectName}");
                        return;
                    }
                    // No SC available - skip silently, wait for next turn
                }
            }

            // No delivery possible this turn - keep Pending, wait for next turn
    }

    private static void SetupCycleMission(Data.LogisticsRequest req, List<Spacecraft> scs,
        ResourceDefinition rd, double amount, ObjectInfo requesterOI, ObjectInfo providerOI,
        LaunchVehicleType lvTypeA = null)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance.Player;
        var firstSc = scs[0];
        if (firstSc == null) return;

        var realProvider = firstSc.CurrentlyOnThisObject;
        if (realProvider == null) return;

        var scList = scs.Select(s => s as ISpacecraftInfo).ToList();
        var cargoToB = new CargoAll();
        var item = new Cargo(cargoToB) { resourceType = rd, cargoMass = amount, resourceTypeType = EResourceTypeType.resorces };
        cargoToB.listCargo.Add(item);

        var cmdData = new CycleMissionsDataData
        {
            A = realProvider, B = requesterOI, Company = player,
            CargoStart = ECargoStart.FlyWithWhatIsAvailable, CargoEnd = ECargoStart.FlyWithWhatIsAvailable,
            CargoAllStart = cargoToB, CargoAllEnd = new CargoAll(),
            LvTypeA = lvTypeA, LvTypeB = null, TransferType = ETransferType.Optimal,
            Ends = EEnds.ThisManyTimes, EndsObjectThisManyTimes = 1, ListSC = scList
        };
        var cmd = new CycleMissionsData(cmdData);
        MonoBehaviourSingleton<CycleMissionManager>.Instance.AddCycleMission(firstSc, cmd, scList);

        var label = lvTypeA != null
            ? $"LV+Container: A={realProvider.ObjectName} B={requesterOI.ObjectName} lv={lvTypeA.Name}"
            : $"SC: A={realProvider.ObjectName} B={requesterOI.ObjectName} ships={scs.Count}";
        Log($"Cycle: {label}");

        req.status = Data.LogisticsRequestStatus.InProgress;

        var ctrl = firstSc.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
        if (ctrl == null)
            ctrl = firstSc.gameObject.AddComponent<SpaceCraftCyclicalMissionController>();
        ctrl.CycleMissionPlanFlyWas = false;
        ctrl.SetSC(firstSc);
        ctrl.TryPlanCycleMission();

        cmd.customNameFromPlanMission = $"[LOGI] {realProvider.ObjectName} → {requesterOI.ObjectName}";
    }

    private static void SetupCycleMission(Data.LogisticsRequest req, Spacecraft container,
        ResourceDefinition rd, double amount, ObjectInfo requesterOI, ObjectInfo providerOI,
        LaunchVehicleType lvTypeA)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance.Player;
        if (container == null || player == null) return;

        var realProvider = providerOI;
        if (realProvider == null) return;

        var scList = new List<ISpacecraftInfo> { container as ISpacecraftInfo };
        var cargoToB = new CargoAll();
        var item = new Cargo(cargoToB) { resourceType = rd, cargoMass = amount, resourceTypeType = EResourceTypeType.resorces };
        cargoToB.listCargo.Add(item);

        var cmdData = new CycleMissionsDataData
        {
            A = realProvider, B = requesterOI, Company = player,
            CargoStart = ECargoStart.FlyWithWhatIsAvailable, CargoEnd = ECargoStart.FlyWithWhatIsAvailable,
            CargoAllStart = cargoToB, CargoAllEnd = new CargoAll(),
            LvTypeA = lvTypeA, LvTypeB = null, TransferType = ETransferType.Optimal,
            Ends = EEnds.ThisManyTimes, EndsObjectThisManyTimes = 1, ListSC = scList
        };
        var cmd = new CycleMissionsData(cmdData);
        MonoBehaviourSingleton<CycleMissionManager>.Instance.AddCycleMission(container, cmd, scList);

        var isLOC = container.spacecraftType?.LowOrbitContainer == true;
        var label = $"LV+{(isLOC?"Container":"SC")} Cycle: A={realProvider.ObjectName} B={requesterOI.ObjectName} lv={lvTypeA.Name}";
        Log($"Cycle: {label}");

        req.status = Data.LogisticsRequestStatus.InProgress;

        var ctrl = container.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
        if (ctrl == null)
            ctrl = container.gameObject.AddComponent<SpaceCraftCyclicalMissionController>();
        ctrl.CycleMissionPlanFlyWas = false;
        ctrl.SetSC(container);
        ctrl.TryPlanCycleMission();

        cmd.customNameFromPlanMission = $"[LOGI] {realProvider.ObjectName} → {requesterOI.ObjectName}";
    }
}
