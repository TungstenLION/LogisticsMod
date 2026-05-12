using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomUpdate;
using Data;
using Data.ScriptableObject;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.UI.Windows.Elements.PlanMissionElements;
using Game.VisualizationScripts;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;

namespace LogisticsMod.Logic;

public static class LogisticsObserver
{
    private static StreamWriter _logWriter;
    private static int _logSession;
    private static List<Spacecraft> _cachedSpacecraft;
    private static List<LaunchVehicle> _cachedLaunchVehicles;

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
        if (player == null) { Log("OnDayChange: player null"); return; }

        Log($"OnDayChange({days}) start");

        var networkResources = Data.LogisticsNetwork.GetNetworkResourcesSet(player);

        _cachedSpacecraft = UnityEngine.Object.FindObjectsOfType<Spacecraft>().ToList();
        _cachedLaunchVehicles = UnityEngine.Object.FindObjectsOfType<LaunchVehicle>().ToList();

        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        var allCycleMissions = cm?.GetAllCycleMission(player) ?? new List<CycleMissionsData>();
        var allObjects = Data.LogisticsNetwork.GetAllObjects();

        CountActiveLogisticsCycles(allCycleMissions, out var scActive, out var lvActive, out var _);

        foreach (var requesterOI in allObjects)
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
                            Log($"OnDayChange: {rd.ID} on {requesterOI.ObjectName} Satisfied→Pending ({currentCount} < {req.requestedAmount})");
                            req.status = Data.LogisticsRequestStatus.Pending;
                        }
                    }
                    if (req.status == Data.LogisticsRequestStatus.Satisfied)
                        Log($"OnDayChange: {rd.ID} on {requesterOI.ObjectName} remains Satisfied");
                    if (req.status == Data.LogisticsRequestStatus.Satisfied
                        || req.status == Data.LogisticsRequestStatus.Failed)
                    {
                        req.statusNote = null;
                        continue;
                    }
                }

                if (req.status == Data.LogisticsRequestStatus.Pending
                    && req.statusNote != null && req.statusNote.StartsWith("stuck_"))
                {
                    if (int.TryParse(req.statusNote.Substring(6), out var skipDays) && skipDays > 0)
                    {
                        req.statusNote = $"stuck_{skipDays - 1}";
                        continue;
                    }
                    req.statusNote = null;
                }

                if (req.status == Data.LogisticsRequestStatus.Pending)
                    req.statusNote = (rd != null && networkResources.Contains(rd)) ? null : "No provider in network";
                else
                    req.statusNote = null;
                if (rd == null) continue;

                var alreadyThere = requesterOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
                if (alreadyThere >= req.requestedAmount)
                {
                    req.status = Data.LogisticsRequestStatus.Satisfied;
                    Log($"OnDayChange: {rd.ID}→{requesterOI.ObjectName} Satisfied (have {alreadyThere} of {req.requestedAmount})");
                    continue;
                }
                Log($"OnDayChange: {rd.ID}→{requesterOI.ObjectName} need={req.requestedAmount} have={alreadyThere} status={req.status}");

                bool hasActiveDelivery = HasActiveCycleDelivering(requesterOI, rd, allCycleMissions);
                if (hasActiveDelivery)
                {
                    req.status = Data.LogisticsRequestStatus.InProgress;
                    Log($"OnDayChange: {rd.ID}→{requesterOI.ObjectName} InProgress (active delivery found)");
                    continue;
                }

                double remaining = req.requestedAmount - alreadyThere;
                Log($"OnDayChange: {rd.ID}→{requesterOI.ObjectName} no active delivery, calling TryCreateDeliveries (need {remaining})");
                TryCreateDeliveries(req, requesterOI, rd, remaining, player, scActive, lvActive);
            }
        }
        CleanupStuckMissions(player, cm, scActive, lvActive);

        _cachedSpacecraft = null;
        _cachedLaunchVehicles = null;
    }

    private static bool HasActiveCycleDelivering(ObjectInfo requester, ResourceDefinition rd,
        List<CycleMissionsData> allCycleMissions)
    {
        foreach (var cmd in allCycleMissions)
        {
            if (cmd.B != requester) continue;
            if (!cmd.customNameFromPlanMission.StartsWith("[LOGI]")) continue;
            if (cmd.CheckComplete()) continue;
            if (cmd.cargoAllStart?.Tab == null) continue;

            foreach (var tabRes in cmd.cargoAllStart.Tab)
            {
                if (tabRes == rd)
                {
                    var aName = cmd.A?.ObjectName ?? "null";
                    var bName = cmd.B?.ObjectName ?? "null";
                    Log($"HasActiveCycle: \"{cmd.customNameFromPlanMission}\" A={aName} B={bName} rd={rd.ID} complete={cmd.CheckComplete()}");
                    return true;
                }
            }
        }

        var mim = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        if (mim != null)
        {
            foreach (var mi in mim.ListMissionInfo)
            {
                if (mi.complete || mi.cancel) continue;
                if (mi.target != requester) continue;
                if (mi.cargoAll?.listCargo == null) continue;

                bool hasRD = false;
                foreach (var c in mi.cargoAll.listCargo)
                {
                    if (c.resourceType == rd) { hasRD = true; break; }
                }
                if (!hasRD) continue;

                if (mi.missionName.StartsWith("[LOGI]"))
                {
                    Log($"HasActiveCycle(MissionInfo): \"{mi.missionName}\" target={mi.target?.ObjectName} rd={rd.ID}");
                    return true;
                }

                if (mi.fromCyclicalMission && mi.spacecraftInfo2 is Spacecraft miSc)
                {
                    var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
                    var cmd = cm?.GetCycleMission(miSc);
                    if (cmd != null && cmd.customNameFromPlanMission.StartsWith("[LOGI]"))
                    {
                        Log($"HasActiveCycle(MissSC): \"{cmd.customNameFromPlanMission}\" target={mi.target?.ObjectName} rd={rd.ID}");
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static void GetActiveCycleCounts(Company player,
        out Dictionary<string, int> scActive, out Dictionary<string, int> lvActive)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        var all = cm?.GetAllCycleMission(player) ?? new List<CycleMissionsData>();
        CountActiveLogisticsCycles(all, out scActive, out lvActive, out var _);
    }

    private static void CountActiveLogisticsCycles(List<CycleMissionsData> allCycleMissions,
        out Dictionary<string, int> scActive, out Dictionary<string, int> lvActive,
        out Dictionary<string, int> mrActive)
    {
        scActive = new Dictionary<string, int>();
        lvActive = new Dictionary<string, int>();
        mrActive = new Dictionary<string, int>();

        foreach (var cmd in allCycleMissions)
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

    private static double GetTargetDistanceFromSun(ObjectInfo target)
    {
        var oim = MonoBehaviourSingleton<ObjectInfoManager>.Instance;
        var parentObjectInfo = target;
        int maxSteps = 100;
        while (parentObjectInfo != null
               && parentObjectInfo.parentObjectInfo != null
               && parentObjectInfo.parentObjectInfo != oim.mainObjectInfoSun)
        {
            parentObjectInfo = parentObjectInfo.parentObjectInfo;
            if (--maxSteps <= 0) break;
        }
        var solarBody = parentObjectInfo?.SolarBody;
        if (solarBody != null) return solarBody.a;
        if (parentObjectInfo?.objectTypes == EObjectTypes.SolarOrbit) return 0.01f;
        return 1.0f;
    }

    private static bool CanSolarShipReach(Spacecraft sc, ObjectInfo target, Company player)
    {
        var scType = sc.spacecraftType;
        if (scType == null || !scType.SolarSC) return true;
        double solarRange = scType.GetSolarRange(player);
        double targetAu = GetTargetDistanceFromSun(target);
        return solarRange > targetAu;
    }

    private static bool HasFuelForReturn(Spacecraft sc, ObjectInfo destination, Company player)
    {
        var scType = sc.spacecraftType;
        if (scType == null || scType.SolarSC) return true;
        var fuelType = scType.GetFuelType();
        if (fuelType == null) return true;
        double fuelCapacity = scType.GetFuelCapacity(player);
        if (fuelCapacity <= 0) return true;
        var oid = destination.GetObjectInfoData(player);
        if (oid == null) return false;
        return oid.CheckResources(fuelType) >= fuelCapacity;
    }

    private static Spacecraft GetMagneticCatapultFromFacility(ObjectInfo providerOI, Company player)
    {
        var oid = providerOI.GetObjectInfoData(player);
        if (oid == null) return null;
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        foreach (var rr in oid.GetListSpacecraftFacility())
        {
            var sc = rr.spacecraft;
            if (sc == null || sc.spacecraftType == null || !sc.spacecraftType.MagneticCatapult) continue;
            var hasCycle = cm.GetCycleMission(sc);
            if (sc.IsReadyToPlan() && hasCycle == null)
                return sc;
        }
        return null;
    }

    private static void TryCreateDeliveries(Data.LogisticsRequest req, ObjectInfo requester,
        ResourceDefinition rd, double remaining, Company player,
        Dictionary<string, int> scActive, Dictionary<string, int> lvActive)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null) { Log("TryCreateDeliveries: cm null"); return; }

        Log($"TryCreateDeliveries: req {rd?.ID} x{remaining} to {requester?.ObjectName}");

        foreach (var providerOI in Data.LogisticsNetwork.GetAllObjects())
        {
            if (providerOI == requester) continue;
            if (remaining <= 0) break;

            var provData = Data.LogisticsNetwork.Get(providerOI);
            if (provData == null) continue;

            if (!provData.providers.Any(p => p.isActive && p.ResourceDefinition == rd))
                continue;

            var oid = providerOI.GetObjectInfoData(player);
            if (oid == null) continue;

            var available = oid.CheckResources(rd);
            var minKeep = provData.providers.Where(p => p.isActive && p.ResourceDefinition == rd).Sum(p => p.minimumKeep);
            available -= minKeep;
            if (available <= req.requestedAmount)
                continue;

            var toDeliver = Math.Min(available, remaining);
            Log($"TryCreateDeliveries: provider={providerOI.ObjectName} available={available} toDeliver={toDeliver}");

            double usableExcess = available - req.requestedAmount;
            double maxTake25 = Math.Floor(usableExcess * 0.25);
            double maxDeliver500 = req.requestedAmount * 5.0;
            double missionCap = Math.Min(maxTake25, maxDeliver500);
            double targetAmount = Math.Min(toDeliver, missionCap);
            if (targetAmount <= 0) continue;

            bool delivered = false;

            // SC delivery
            foreach (var quota in provData.spacecraftQuota)
            {
                if (quota.count <= 0) continue;
                scActive.TryGetValue(quota.typeName, out var activeOfType);
                var canUse = quota.count - activeOfType;
                if (canUse <= 0) continue;

                var idleSC = _cachedSpacecraft
                    .Where(sc => sc != null && sc.spacecraftType != null
                        && !sc.spacecraftType.MagneticCatapult
                        && sc.CurrentlyOnThisObject == providerOI
                        && sc.CurrentPhase == Spacecraft.EPhase.None
                        && sc.spacecraftType.NameRocketType == quota.typeName
                        && cm.GetCycleMission(sc) == null
                        && CanSolarShipReach(sc, requester, player)
                        && HasFuelForReturn(sc, requester, player))
                    .Take(canUse)
                    .OrderByDescending(sc => sc.spacecraftType.GetCargoCapacity(player))
                    .ToList();

                if (idleSC.Count == 0)
                    continue;

                var bestSingle = idleSC
                    .Where(sc => sc.spacecraftType.GetCargoCapacity(player) >= targetAmount)
                    .OrderBy(sc => sc.spacecraftType.GetCargoCapacity(player))
                    .FirstOrDefault();

                List<Spacecraft> selectedShips;
                double totalCapacity;

                if (bestSingle != null)
                {
                    selectedShips = new List<Spacecraft> { bestSingle };
                    totalCapacity = bestSingle.spacecraftType.GetCargoCapacity(player);
                }
                else
                {
                    selectedShips = new List<Spacecraft>();
                    totalCapacity = 0;
                    foreach (var sc in idleSC)
                    {
                        selectedShips.Add(sc);
                        totalCapacity += sc.spacecraftType.GetCargoCapacity(player);
                        if (totalCapacity >= targetAmount) break;
                    }
                }

                double actualAmount = Math.Min(targetAmount, totalCapacity);
                Log($"TryCreateDeliveries: delivering via SC {string.Join(",", selectedShips.Select(s => $"{s.spacecraftType.NameRocketType}#{s.ID}"))} amount={actualAmount}");
                SetupCycleMission(req, selectedShips, rd, actualAmount, requester, providerOI);
                scActive.TryGetValue(quota.typeName, out var cur);
                scActive[quota.typeName] = cur + selectedShips.Count;
                remaining -= actualAmount;
                delivered = true;
                if (remaining <= 0) return;
                break;
            }

            if (delivered) continue;

            // LV delivery — enabled LV types only (quota > 0 means enabled)
            var enabledLVTypes = provData.launchVehicleQuota
                .Where(q => q.count > 0)
                .Select(q => q.typeName)
                .ToHashSet();

            if (enabledLVTypes.Count == 0)
                continue;

            var availableLV = _cachedLaunchVehicles
                .Where(lv => lv != null && lv.launchVehicleType != null
                    && lv.objectInfo == providerOI
                    && lv.IsReadyToLaunchReusable()
                    && enabledLVTypes.Contains(lv.launchVehicleType.Name))
                .ToList();

            if (availableLV.Count == 0)
            {
                Log($"TryCreateDeliveries: found enabled LV types={string.Join(",",enabledLVTypes)} but no ready LVs on {providerOI.ObjectName}");
                continue;
            }

            // Best-fit LV selection (same two-phase logic as SC)
            var sortedLV = availableLV
                .OrderByDescending(lv => lv.launchVehicleType.MaxPayloadOnThisObject(providerOI, player))
                .ToList();

            var bestLV = sortedLV
                .Where(lv => lv.launchVehicleType.MaxPayloadOnThisObject(providerOI, player) >= targetAmount)
                .OrderBy(lv => lv.launchVehicleType.MaxPayloadOnThisObject(providerOI, player))
                .FirstOrDefault() ?? sortedLV[0];

            var lvType = bestLV.launchVehicleType;

            // Check if target is the low orbit of the provider (surface -> orbit case)
            var targetIsOrbitOfProvider = providerOI != null
                && providerOI.LowOrbitCustom != null
                && providerOI.LowOrbitCustom.GetObjectInfo() == requester;

            Spacecraft scOnOrbit = null;
            ObjectInfo lvA = providerOI;
            var providerOrbit = providerOI.LowOrbitCustom?.GetObjectInfo();

            // Magnetic catapult uses cyclical missions (Ends=ThisManyTimes=1).
            // Catapult stays at facility, launches payload via LV, mission completes once.
            if (bestLV.launchVehicleType.FakeForFacility)
            {
                var magSc = GetMagneticCatapultFromFacility(providerOI, player);
                if (magSc != null)
                {
                    double lvCapacity = bestLV.launchVehicleType.MaxPayloadOnThisObject(providerOI, player);
                    double lvActualAmount = Math.Min(targetAmount, lvCapacity);
                    Log($"TryCreateDeliveries: cycle via magnetic catapult {magSc.spacecraftType?.NameRocketType}#{magSc.ID} amount={lvActualAmount} req={rd.ID} reqStatus={req.status} requester={requester.ObjectName} provider={providerOI.ObjectName}");
                    SetupCatapultCycleMission(req, magSc, rd, lvActualAmount, requester, providerOI, bestLV);
                    lvActive.TryGetValue(lvType.Name, out var lvCur);
                    lvActive[lvType.Name] = lvCur + 1;
                    remaining -= lvActualAmount;
                    if (remaining <= 0) return;
                }
                else
                {
                    Log($"TryCreateDeliveries: magnetic catapult not found at {providerOI.ObjectName}");
                }
            }
            else if (targetIsOrbitOfProvider)
            {
                scOnOrbit = MonoBehaviourSingleton<ShipManager>.Instance?.GetLowOrbitContainer(player);
                lvA = providerOI;
            }
            else if (providerOrbit != null)
            {
                scOnOrbit = _cachedSpacecraft
                    .Where(sc => sc != null && sc.spacecraftType != null
                        && sc.CurrentlyOnThisObject == providerOrbit
                        && sc.CurrentPhase == Spacecraft.EPhase.None
                        && !sc.spacecraftType.LowOrbitContainer
                        && cm.GetCycleMission(sc) == null
                        && CanSolarShipReach(sc, requester, player)
                        && HasFuelForReturn(sc, requester, player))
                    .FirstOrDefault();
                lvA = providerOrbit;
            }

            if (scOnOrbit != null)
            {
                double lvCapacity = bestLV.launchVehicleType.MaxPayloadOnThisObject(providerOI, player);
                double lvActualAmount = Math.Min(targetAmount, lvCapacity);
                Log($"TryCreateDeliveries: delivering via LV {lvType.Name} payload SC={scOnOrbit.spacecraftType?.NameRocketType}#{scOnOrbit.ID} amount={lvActualAmount}");

                SetupCycleMission(req, scOnOrbit, rd, lvActualAmount, requester, lvA, lvType);

                lvActive.TryGetValue(lvType.Name, out var lvCur);
                lvActive[lvType.Name] = lvCur + 1;
                remaining -= lvActualAmount;
                if (remaining <= 0) return;
            }


        }

        // No delivery possible this turn(s) - remaining request stays Pending
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

        req.status = Data.LogisticsRequestStatus.InProgress;

        cmd.customNameFromPlanMission = $"[LOGI] {realProvider.ObjectName} → {requesterOI.ObjectName}";

        Log($"SetupCycleMission(SC): \"{cmd.customNameFromPlanMission}\" sc={firstSc.spacecraftType?.NameRocketType}#{firstSc.ID} lvType={lvTypeA?.Name ?? "none"}");

        var ctrl = firstSc.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
        if (ctrl == null)
            ctrl = firstSc.gameObject.AddComponent<SpaceCraftCyclicalMissionController>();
        ctrl.CycleMissionPlanFlyWas = false;
        ctrl.SetSC(firstSc);
        ctrl.TryPlanCycleMission();
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

        req.status = Data.LogisticsRequestStatus.InProgress;

        cmd.customNameFromPlanMission = $"[LOGI] {realProvider.ObjectName} → {requesterOI.ObjectName}";

        Log($"SetupCycleMission(LV): \"{cmd.customNameFromPlanMission}\" sc={container.spacecraftType?.NameRocketType}#{container.ID} lvType={lvTypeA?.Name ?? "none"}");

        var ctrl = container.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
        if (ctrl == null)
            ctrl = container.gameObject.AddComponent<SpaceCraftCyclicalMissionController>();
        ctrl.CycleMissionPlanFlyWas = false;
        ctrl.SetSC(container);
        ctrl.TryPlanCycleMission(loadLimit2: amount);
    }

    private static void SetupCatapultCycleMission(Data.LogisticsRequest req, Spacecraft catapult,
        ResourceDefinition rd, double amount, ObjectInfo requesterOI, ObjectInfo providerOI,
        LaunchVehicle lv)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance.Player;
        if (catapult == null || player == null) return;

        var scList = new List<ISpacecraftInfo> { catapult as ISpacecraftInfo };
        var cargoToB = new CargoAll();
        var item = new Cargo(cargoToB) { resourceType = rd, cargoMass = amount, resourceTypeType = EResourceTypeType.resorces };
        cargoToB.listCargo.Add(item);

        var cmdData = new CycleMissionsDataData
        {
            A = providerOI, B = requesterOI, Company = player,
            CargoStart = ECargoStart.FlyWithWhatIsAvailable, CargoEnd = ECargoStart.FlyWithWhatIsAvailable,
            CargoAllStart = cargoToB, CargoAllEnd = new CargoAll(),
            LvTypeA = lv?.launchVehicleType, LvTypeB = null, TransferType = ETransferType.Optimal,
            Ends = EEnds.ThisManyTimes, EndsObjectThisManyTimes = 1, ListSC = scList
        };
        var cmd = new CycleMissionsData(cmdData);
        MonoBehaviourSingleton<CycleMissionManager>.Instance.AddCycleMission(catapult, cmd, scList);

        req.status = Data.LogisticsRequestStatus.InProgress;

        cmd.customNameFromPlanMission = $"[LOGI] {providerOI.ObjectName} → {requesterOI.ObjectName}";

        Log($"SetupCatapultCycleMission: A={providerOI.ObjectName} B={requesterOI.ObjectName} rd={rd.ID} amount={amount} lv={lv?.launchVehicleType?.Name}");

        Patches.SpaceCraftCyclicalMissionControllerPatches.SetLogiLoadLimit(cmd.customNameFromPlanMission, amount);

        var ctrl = catapult.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
        if (ctrl == null)
            ctrl = catapult.gameObject.AddComponent<SpaceCraftCyclicalMissionController>();
        ctrl.CycleMissionPlanFlyWas = false;
        ctrl.SetSC(catapult);
        ctrl.TryPlanCycleMission(loadLimit2: amount);
    }

    private static void CleanupStuckMissions(Company player, CycleMissionManager cm,
        Dictionary<string, int> scActive, Dictionary<string, int> lvActive)
    {
        var mim = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        var tc = MonoBehaviourSingleton<TimeController>.Instance;
        if (mim == null || tc == null) return;

        var currentTime = tc.CurrentTime;
        var logiStuck = 0;

        foreach (var mi in mim.ListMissionInfo)
        {
            if (mi.complete || mi.cancel) continue;
            if (!mi.fromCyclicalMission && !mi.missionName.StartsWith("[LOGI]")) continue;

            var sc = mi.spacecraftInfo2 as Spacecraft;
            if (sc == null) continue;

            // Stuck if: no departure date, or departure is in the past and SC is still idle
            bool hasPastDate = mi.DateLaunch != default && mi.DateLaunch <= currentTime;
            bool hasNoDate = mi.DateLaunch == default;
            if (!hasPastDate && !hasNoDate) continue;

            // SC still idle at the start location → mission never launched
            if (hasPastDate && sc.CurrentPhase != Spacecraft.EPhase.None && sc.CurrentPhase != Spacecraft.EPhase.PlanedMission) continue;

            if (mi.fromCyclicalMission)
            {
                var cmd = cm.GetCycleMission(sc);
                if (cmd == null || !cmd.customNameFromPlanMission.StartsWith("[LOGI]")) continue;

                var reason = hasNoDate ? "no departure date" : $"past departure ({mi.DateLaunch:yyyy-MM-dd})";
                Log($"CLEANUP stuck cyclical: {cmd.customNameFromPlanMission} — {reason} sc={sc.spacecraftType?.NameRocketType} scAt={sc.CurrentlyOnThisObject?.ObjectName} scPhase={sc.CurrentPhase}");

                cm.RemoveCycleMission(sc);
                ResetCycleRequests(cmd);
                logiStuck++;
            }
            else
            {
                var reason = hasNoDate ? "no departure date" : $"past departure ({mi.DateLaunch:yyyy-MM-dd})";
                Log($"CLEANUP stuck one-time: {mi.missionName} — {reason} sc={sc.spacecraftType?.NameRocketType} scAtId={sc.CurrentlyOnThisObject?.id} scAtName={sc.CurrentlyOnThisObject?.ObjectName} scPhase={sc.CurrentPhase}");

                sc.CancelMission(mi);

                var requester = mi.target;
                var firstCargo = mi.cargoAll?.listCargo?.FirstOrDefault();
                if (requester != null && firstCargo != null)
                {
                    var reqData = Data.LogisticsNetwork.Get(requester);
                    if (reqData != null)
                    {
                        foreach (var req in reqData.requests)
                        {
                            if (req.ResourceDefinition == firstCargo.resourceType && req.status == Data.LogisticsRequestStatus.InProgress)
                            {
                                req.status = Data.LogisticsRequestStatus.Pending;
                                req.statusNote = null;
                                Log($"CLEANUP replan one-time: {firstCargo.resourceType.ID} on {requester.ObjectName}");

                                var alreadyThere = requester.GetObjectInfoData(player)?.CheckResources(firstCargo.resourceType) ?? 0;
                                if (alreadyThere < req.requestedAmount)
                                {
                                    double remaining = req.requestedAmount - alreadyThere;
                                    TryCreateDeliveries(req, requester, firstCargo.resourceType, remaining, player, scActive, lvActive);
                                }
                            }
                        }
                    }
                }
                logiStuck++;
            }
        }
        if (logiStuck > 0) Log($"CLEANUP: removed {logiStuck} stuck LOGI missions");
    }

    private static void ResetCycleRequests(CycleMissionsData cmd)
    {
        var requester = cmd.B;
        if (requester == null) return;
        var reqData = Data.LogisticsNetwork.Get(requester);
        if (reqData == null || cmd.cargoAllStart?.Tab == null) return;
        foreach (var res in cmd.cargoAllStart.Tab)
        {
            foreach (var req in reqData.requests)
            {
                if (req.ResourceDefinition == res && req.status == Data.LogisticsRequestStatus.InProgress)
                {
                    req.status = Data.LogisticsRequestStatus.Pending;
                    Log($"CLEANUP reset request: {res.ID} on {requester.ObjectName} to Pending");
                }
            }
        }
    }
}
