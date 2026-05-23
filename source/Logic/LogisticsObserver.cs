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
using Game.UI.Windows.Windows;
using Game.VisualizationScripts;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;

namespace LogisticsMod.Logic;

public static class LogisticsObserver
{
    private static List<Spacecraft> _cachedSpacecraft;
    private static List<LaunchVehicle> _cachedLaunchVehicles;
    private static HashSet<int> _busyCatapultIds = new HashSet<int>();
    private static HashSet<int> _busyLvIds = new HashSet<int>();
    private static Dictionary<int, int> _busyLvTick = new Dictionary<int, int>();
    private static int _tickCount = 0;
    private const bool _logDiag = false;

    private static string _logPath;

    static LogisticsObserver()
    {
        try
        {
            _logPath = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogisticsMod.log");
            var dir = Path.GetDirectoryName(_logPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch
        {
            _logPath = null;
        }
    }

    public static void ResetRuntimeState()
    {
        _busyCatapultIds.Clear();
        _busyLvIds.Clear();
        _busyLvTick.Clear();
        _tickCount = 0;
    }

    internal static void Log(string msg)
    {
        if (_logPath == null) return;
        try { File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss} [LOG] {msg}\n"); }
        catch { }
    }

    internal static void LogWarning(string msg)
    {
        if (_logPath == null) return;
        try { File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss} [WARN] {msg}\n"); }
        catch { }
    }

    internal static void LogError(string msg)
    {
        if (_logPath == null) return;
        try { File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss} [ERROR] {msg}\n"); }
        catch { }
    }

    public static void OnDayChange(double days)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (player == null) return;

        _cachedSpacecraft = UnityEngine.Object.FindObjectsOfType<Spacecraft>()
            .Where(sc => sc != null && sc.GetCompany() == player).ToList();
        _cachedLaunchVehicles = UnityEngine.Object.FindObjectsOfType<LaunchVehicle>()
            .Where(lv => lv != null && lv.GetCompany() == player && Data.LogisticsNetwork.IsLVAvailableForLogistics(lv, player)).ToList();

        _tickCount++;

        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        var allCycleMissions = cm?.GetAllCycleMission(player) ?? new List<CycleMissionsData>();
        var allObjects = Data.LogisticsNetwork.GetAllObjects();

        CountActiveLogisticsCycles(allCycleMissions, out var scActive, out var lvActive, out var _);

        var usedLvIds = new HashSet<int>();
        var usedCatapultIds = new HashSet<int>();

        Log($"OnDayChange: {allObjects.Count} objects, {allCycleMissions.Count} cycles");

        var mimInit = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        if (mimInit != null)
        {
            // Clear busy catapults whose async PlanFlyCode completed
            var toRemove = new List<int>();
            foreach (var cid in _busyCatapultIds)
            {
                bool hasMission = false;
                foreach (var mi in mimInit.ListMissionInfo)
                {
                    if (mi.complete || mi.cancel) continue;
                    if (mi.company != player) continue;
                    if (!mi.missionName.StartsWith("[LOGI]")) continue;
                    foreach (var sci in mi.ListSpacecraftInfo2)
                    {
                        if (sci is Spacecraft sc && sc.GetHashCode() == cid)
                        { hasMission = true; break; }
                    }
                    if (hasMission) break;
                }
                if (hasMission)
                    toRemove.Add(cid);
            }
            foreach (var cid in toRemove)
                _busyCatapultIds.Remove(cid);
        }

        if (_logDiag)
        {
            var busyHashes = string.Join(",", _busyLvIds);
            var mimLines = new List<string>();
            var mimInstance = MonoBehaviourSingleton<MissionInfoManager>.Instance;
            if (mimInstance != null)
            {
                foreach (var mi in mimInstance.ListMissionInfo)
                {
                    if (mi.complete || mi.cancel) continue;
                    if (mi.company != player) continue;
                    if (!mi.missionName.StartsWith("[LOGI]")) continue;
                    var lvHashes = mi.ListLaunchVehicleInfo2?.Select(lvi => lvi?.GetHashCode() ?? -1);
                    mimLines.Add($"\"{mi.missionName}\" LVs=[{string.Join(",", lvHashes ?? Enumerable.Empty<int>())}]");
                }
            }
            Log($"[LV] DIAG: busy=[{busyHashes}] MIM=[{string.Join("; ", mimLines)}] tick={_tickCount}");
        }

        // Clear busy LVs that are no longer in the async PlanFlyCode window.
        // LV stays busy when: not in MIM AND still ready AND grace period (< 3 ticks).
        // LV is cleared when: null, HasLVActiveMission true, !Ready, or grace expired.
        {
            var lvToRemove = new List<int>();
            var lvDetails = new List<string>();
            foreach (var lvId in _busyLvIds)
            {
                var lv = _cachedLaunchVehicles?.FirstOrDefault(l => l?.GetHashCode() == lvId);
                if (lv == null)
                {
                    lvToRemove.Add(lvId);
                    lvDetails.Add($"{lvId}:null");
                    continue;
                }
                bool hasMission = HasLVActiveMission(lv, player);
                bool ready = lv.IsReadyToLaunchReusable();
                bool graceExpired = _busyLvTick.TryGetValue(lvId, out var startTick) && _tickCount - startTick >= 3;
                string reason = hasMission ? "inMIM" : !ready ? "notReady" : graceExpired ? "grace" : "KEPT";
                lvDetails.Add($"{lvId}:{reason}(MIM={hasMission} ready={ready} tick={startTick}-{_tickCount})");
                if (hasMission || !ready || graceExpired)
                {
                    lvToRemove.Add(lvId);
                }
            }
            foreach (var id in lvToRemove)
            {
                _busyLvIds.Remove(id);
                _busyLvTick.Remove(id);
            }
            if (_logDiag)
                Log($"[LV] CLEAR: {string.Join(" ", lvDetails)}");
        }

        // Update frozen state based on atmospheric pressure
        foreach (var oi in allObjects)
        {
            var data = Data.LogisticsNetwork.Get(oi);
            if (data == null) continue;
            data.IsFrozen = (oi.HabitabilityParameters?.pressure ?? 0d) > 0.0001d;
        }

        // Process each network separately
        var allNetworkIds = Data.LogisticsNetwork.GetAllNetworkIds();
        foreach (var networkId in allNetworkIds)
        {
            var networkResources = Data.LogisticsNetwork.GetNetworkResourcesSet(player, networkId);

            foreach (var requesterOI in allObjects)
            {
                var reqData = Data.LogisticsNetwork.Get(requesterOI);
                if (reqData == null) continue;

                foreach (var req in reqData.requests)
                {
                    if (req.networkId != networkId) continue;
                var rd = req.ResourceDefinition;
                var rdName = rd?.Name ?? "NULL";

                Log($"[{requesterOI.ObjectName}] req={rdName} amt={req.requestedAmount} status={req.status}");

                // Compute total covered (already there + in transit)
                double inTransit = 0;
                if (rd != null)
                {
                    var mim = MonoBehaviourSingleton<MissionInfoManager>.Instance;
                    if (mim != null)
                    {
                        foreach (var mi in mim.ListMissionInfo)
                        {
                            if (mi.complete || mi.cancel) continue;
                            if (mi.company != player) continue;
                            if (mi.target != requesterOI) continue;
                            if (!mi.missionName.StartsWith("[LOGI]")) continue;
                            if (mi.cargoAll?.listCargo == null) continue;
                            foreach (var c in mi.cargoAll.listCargo)
                            {
                                if (c.resourceType == rd)
                                    inTransit += c.cargoMass;
                            }
                        }
                    }

                }
                var alreadyThere = requesterOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
                var totalCovered = alreadyThere + inTransit;
                Log($"[{requesterOI.ObjectName}] {rdName}: alreadyThere={alreadyThere} inTransit={inTransit} totalCovered={totalCovered} requested={req.requestedAmount}");

                // totalCovered >= requestedAmount → Satisfied, no more ordering
                if (totalCovered >= req.requestedAmount)
                {
                    req.status = Data.LogisticsRequestStatus.Satisfied;
                    req.statusNote = null;
                    Log($"[{requesterOI.ObjectName}] {rdName}: Satisfied (totalCovered >= requested)");
                    continue;
                }

                // If Satisfied/Failed but stock dropped below target, reopen to Pending
                if ((req.status == Data.LogisticsRequestStatus.Satisfied || req.status == Data.LogisticsRequestStatus.Failed) && totalCovered < req.requestedAmount)
                {
                    req.status = Data.LogisticsRequestStatus.Pending;
                    req.statusNote = null;
                    Log($"[{requesterOI.ObjectName}] {rdName}: reopened to Pending (stock dropped below target)");
                }

                // Pending + stuck_N → skip remaining days
                if (req.status == Data.LogisticsRequestStatus.Pending
                    && req.statusNote != null && req.statusNote.StartsWith("stuck_"))
                {
                    if (int.TryParse(req.statusNote.Substring(6), out var skipDays) && skipDays > 0)
                    {
                        req.statusNote = $"stuck_{skipDays - 1}";
                        Log($"[{requesterOI.ObjectName}] {rdName}: stuck skip {skipDays - 1} days left");
                        continue;
                    }
                    req.statusNote = null;
                }

                // InProgress + no HACD → retry loop (no confirmed mission)
                if (req.status == Data.LogisticsRequestStatus.InProgress)
                {
                    var allCycles = cm?.GetAllCycleMission(player) ?? new List<CycleMissionsData>();
                    bool hacdResult = HasActiveCycleDelivering(requesterOI, rd, allCycles);
                    if (!hacdResult)
                    {
                        int retries = 0;
                        if (req.statusNote != null && req.statusNote.StartsWith("retry_") && int.TryParse(req.statusNote.Substring(6), out var parsed))
                            retries = parsed;

                        const int maxRetries = 3;
                        if (retries >= maxRetries)
                        {
                            LogWarning($"[{requesterOI.ObjectName}] {rdName}: InProgress but no mission after {retries} retries — resetting to Pending");
                            req.status = Data.LogisticsRequestStatus.Pending;
                            req.statusNote = null;
                            continue;
                        }

                        req.statusNote = $"retry_{retries + 1}";
                        Log($"[{requesterOI.ObjectName}] {rdName}: InProgress, async mission pending (retry {retries + 1}/{maxRetries}) — skip");
                        continue;
                    }
                    // HACD = true → mission confirmed, totalCovered < requested → may order more
                    Log($"[{requesterOI.ObjectName}] {rdName}: InProgress with HACD, totalCovered={totalCovered} < {req.requestedAmount} — may order more");
                }

                // Pending → check network resources
                bool networkNoteSet = false;
                if (req.status == Data.LogisticsRequestStatus.Pending)
                {
                    bool hasInNetwork = rd != null && networkResources.Contains(rd);
                    req.statusNote = hasInNetwork ? null : "No provider in network";
                    if (!hasInNetwork)
                    {
                        Log($"[{requesterOI.ObjectName}] {rdName}: No provider in network (skip)");
                        networkNoteSet = true;
                    }
                }
                if (rd == null) continue;

                // HACD → set InProgress (mission exists), delivery already in transit — skip
                if (HasActiveCycleDelivering(requesterOI, rd, allCycleMissions))
                {
                    req.status = Data.LogisticsRequestStatus.InProgress;
                    Log($"[{requesterOI.ObjectName}] {rdName}: HACD confirmed, delivery in transit — skip");
                    continue;
                }

                // Skip TryCreateDeliveries if no provider in network
                if (networkNoteSet) continue;

                // Try to create deliveries to cover the gap
                double remaining = req.requestedAmount - totalCovered;
                Log($"[{requesterOI.ObjectName}] {rdName}: creating delivery remaining={remaining}");

                // For orbit requesters: try surface-to-orbit (LV from parent body) first
                if (requesterOI.objectTypes == EObjectTypes.Orbit)
                {
                    Log($"[{requesterOI.ObjectName}] {rdName}: trying surface-to-orbit delivery first (remaining≈{remaining})");
                    TryCreateSurfaceToOrbitDelivery(req, requesterOI, rd, remaining, player, usedLvIds, usedCatapultIds, networkId);
                }

                // If still Pending after surface-to-orbit attempt (or not an orbit), try network-wide delivery
                if (req.status == Data.LogisticsRequestStatus.Pending && remaining > 0)
                {
                    TryCreateDeliveries(req, requesterOI, rd, remaining, player, scActive, lvActive, usedLvIds, usedCatapultIds, networkId);
                }

                // If still Pending after delivery attempts — set meaningful note
                if (req.status == Data.LogisticsRequestStatus.Pending && string.IsNullOrEmpty(req.statusNote))
                    req.statusNote = "No provider available";
                }
            }
        }

        // Process to-orbit providers with batched deliveries for each network
        // Guard: skip on post-load trigger (days == 0) where async PlanFlyCode missions aren't yet in MIM,
        // causing HasLVActiveMission to miss them and create duplicates.
        if (days > 0)
        {
            foreach (var networkId in allNetworkIds)
            {
                TryCreateBatchedToOrbitDeliveries(player, usedLvIds, usedCatapultIds, networkId);
            }
        }

        //CleanupStuckMissions(player, cm);

        _cachedSpacecraft = null;
        _cachedLaunchVehicles = null;
    }

    private static bool HasActiveCycleDelivering(ObjectInfo requester, ResourceDefinition rd,
        List<CycleMissionsData> allCycleMissions)
    {
        var reqName = requester?.ObjectName ?? "?";
        var rdName = rd?.Name ?? "?";
        var reqHash = requester?.GetHashCode() ?? 0;

        Log($"[HACD] {reqName}/{rdName}: checking {allCycleMissions?.Count ?? 0} cycles (requester hash={reqHash})");

        int cyclesChecked = 0;
        foreach (var cmd in allCycleMissions)
        {
            if (cmd.B != requester) continue;
            if (!cmd.customNameFromPlanMission.StartsWith("[LOGI]")) continue;
            if (cmd.CheckComplete()) continue;
            if (cmd.cargoAllStart?.Tab == null) continue;
            cyclesChecked++;

            foreach (var tabRes in cmd.cargoAllStart.Tab)
            {
                if (tabRes == rd)
                {
                    Log($"[HACD] {reqName}/{rdName}: FOUND in cycle \"{cmd.customNameFromPlanMission}\" (cyclesChecked={cyclesChecked})");
                    return true;
                }
            }
        }

        var mim = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        int logiInMim = 0;
        int matchedTarget = 0;
        if (mim != null)
        {
            foreach (var mi in mim.ListMissionInfo)
            {
                if (mi.complete || mi.cancel) continue;
                if (!mi.missionName.StartsWith("[LOGI]")) continue;
                logiInMim++;
                if (mi.target != requester) continue;
                matchedTarget++;

                if (mi.cargoAll?.listCargo == null) continue;

                bool hasRD = false;
                foreach (var c in mi.cargoAll.listCargo)
                {
                    if (c.resourceType == rd) { hasRD = true; break; }
                }
                if (!hasRD) continue;

                if (mi.missionName.StartsWith("[LOGI]"))
                {
                    Log($"[HACD] {reqName}/{rdName}: FOUND in LOGI mission \"{mi.missionName}\" ID={mi.id} phase={((mi.spacecraftInfo2 as Spacecraft)?.CurrentPhase)} launch={mi.DateLaunch} (logiInMIM={logiInMim} matchedTarget={matchedTarget})");
                    return true;
                }

                if (mi.fromCyclicalMission && mi.spacecraftInfo2 is Spacecraft miSc)
                {
                    var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
                    var cmd = cm?.GetCycleMission(miSc);
                    if (cmd != null && cmd.customNameFromPlanMission.StartsWith("[LOGI]"))
                    {
                        Log($"[HACD] {reqName}/{rdName}: FOUND via cyclical mission \"{cmd.customNameFromPlanMission}\" (logiInMIM={logiInMim} matchedTarget={matchedTarget})");
                        return true;
                    }
                }
            }
        }

        Log($"[HACD] {reqName}/{rdName}: NOT FOUND (cyclesChecked={cyclesChecked} logiInMIM={logiInMim} matchedTarget={matchedTarget} mimNull={mim==null})");
        return false;
    }

    public static void GetActiveCycleCounts(Company player,
        out Dictionary<string, int> scActive, out Dictionary<string, int> lvActive)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        var all = cm?.GetAllCycleMission(player) ?? new List<CycleMissionsData>();
        CountActiveLogisticsCycles(all, out scActive, out lvActive, out var _);
    }

    private static string GetNetworkIdFromName(string name)
    {
        if (name.StartsWith("[LOGI][") && name.Length > 7)
        {
            var close = name.IndexOf(']', 7);
            if (close > 7) return name.Substring(7, close - 7);
            return "";
        }
        return "";
    }

    private static string ActiveKey(string networkId, string typeName)
    {
        return $"{networkId}\0{typeName}";
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

            var netId = GetNetworkIdFromName(cmd.customNameFromPlanMission);

            foreach (var sci in cmd.ListSC)
            {
                var sc = sci as Spacecraft;
                if (sc == null || sc.spacecraftType == null) continue;
                var tn = Data.LogisticsNetwork.TypeKey(sc.spacecraftType.ID, sc.spacecraftType.NameRocketType ?? "SC");
                var key = ActiveKey(netId, tn);
                if (!scActive.ContainsKey(key)) scActive[key] = 0;
                scActive[key]++;
            }

            if (cmd.LvTypeA != null)
            {
                var tnA = Data.LogisticsNetwork.TypeKey(cmd.LvTypeA.ID, cmd.LvTypeA.Name ?? "LV");
                var key = ActiveKey(netId, tnA);
                if (!lvActive.ContainsKey(key)) lvActive[key] = 0;
                lvActive[key]++;
            }
            if (cmd.LvTypeB != null)
            {
                var tnB = Data.LogisticsNetwork.TypeKey(cmd.LvTypeB.ID, cmd.LvTypeB.Name ?? "LV");
                var key = ActiveKey(netId, tnB);
                if (!lvActive.ContainsKey(key)) lvActive[key] = 0;
                lvActive[key]++;
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

    private static bool HasFuelForReturnOrbit(Spacecraft sc, ObjectInfo body, Company player)
    {
        var scType = sc.spacecraftType;
        if (scType == null || scType.SolarSC) return true;
        var fuelType = scType.GetFuelType();
        if (fuelType == null) return true;
        double fuelCapacity = scType.GetFuelCapacity(player);
        if (fuelCapacity <= 0) return true;
        var orbitOI = body?.LowOrbitCustom?.GetObjectInfo();
        if (orbitOI == null) return false;
        var oid = orbitOI.GetObjectInfoData(player);
        if (oid == null) return false;
        return oid.CheckResources(fuelType) >= fuelCapacity;
    }

    private static bool WouldGetStuckOnSurface(Spacecraft sc, ObjectInfo body)
    {
        if (sc == null || sc.spacecraftType == null || body == null) return false;
        if (body.objectTypes != EObjectTypes.Planet && body.objectTypes != EObjectTypes.Moons)
            return false;
        if (sc.spacecraftType.DestroyOnLand || sc.spacecraftType.LowOrbitContainer || sc.spacecraftType.MagneticCatapult)
            return false;
        return sc.spacecraftType.needLaunchVehicleToGoToMoon;
    }

    private static bool WouldGetStuckOnReturn(Spacecraft sc, ObjectInfo providerOI, ObjectInfo requester)
    {
        if (sc == null || sc.spacecraftType == null || providerOI == null || requester == null)
            return false;
        if (WouldGetStuckOnSurface(sc, providerOI))
            return false;
        var providerOrbit = providerOI.LowOrbitCustom?.GetObjectInfo();
        if (providerOrbit == null)
            return false;
        return requester == providerOrbit;
    }

    private static Spacecraft GetMagneticCatapultFromFacility(ObjectInfo providerOI, Company player,
        HashSet<int> usedCatapultIds)
    {
        var oid = providerOI.GetObjectInfoData(player);
        if (oid == null)
        {
            LogWarning($"[GMCF] {providerOI?.ObjectName}: oid is null");
            return null;
        }
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        int totalCatapults = 0;
        Log($"[GMCF] {providerOI.ObjectName}: scanning facility, usedCatapultIds=[{string.Join(",", usedCatapultIds)}]");
        foreach (var rr in oid.GetListSpacecraftFacility())
        {
            var sc = rr.spacecraft;
            if (sc == null || sc.spacecraftType == null || !sc.spacecraftType.MagneticCatapult) continue;
            if (sc.GetCompany() != player) continue;
            totalCatapults++;
            var scName = sc.spacecraftType.NameRocketType ?? "?";
            var scId = sc.GetHashCode();
            Log($"[GMCF] {providerOI.ObjectName}: found catapult {scName} id={scId}");
            if (usedCatapultIds.Contains(scId) || _busyCatapultIds.Contains(scId))
            {
                Log($"[GMCF] {providerOI.ObjectName}: catapult {scName} already used, skip");
                continue;
            }
            if (!sc.IsReadyToPlan())
            {
                Log($"[GMCF] {providerOI.ObjectName}: catapult {scName} NOT ready");
                continue;
            }
            var existingCycle = cm.GetCycleMission(sc);
            if (existingCycle != null)
            {
                if (!existingCycle.customNameFromPlanMission.StartsWith("[LOGI]"))
                {
                    Log($"[GMCF] {providerOI.ObjectName}: catapult {scName} has non-LOGI cycle, skip");
                    continue;
                }
                Log($"[GMCF] {providerOI.ObjectName}: catapult {scName} removing stale cycle \"{existingCycle.customNameFromPlanMission}\"");
                cm.RemoveCycleMission(sc);
                ResetCycleRequests(existingCycle);
            }

            if (HasCatapultActiveMission(sc, player))
            {
                Log($"[GMCF] {providerOI.ObjectName}: catapult {scName} has active one-shot LOGI mission, skip");
                continue;
            }

            Log($"[GMCF] {providerOI.ObjectName}: returning catapult {scName} id={scId}");
            return sc;
        }
        Log($"[GMCF] {providerOI.ObjectName}: {totalCatapults} catapults found, NONE available");
        return null;
    }

    private static List<Spacecraft> GetAllAvailableCatapultsFromFacility(ObjectInfo providerOI, Company player,
        HashSet<int> usedCatapultIds)
    {
        var result = new List<Spacecraft>();
        var oid = providerOI.GetObjectInfoData(player);
        if (oid == null)
        {
            LogWarning($"[GACF] {providerOI?.ObjectName}: oid is null");
            return result;
        }
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        Log($"[GACF] {providerOI.ObjectName}: scanning all catapults, usedCatapultIds=[{string.Join(",", usedCatapultIds)}]");
        foreach (var rr in oid.GetListSpacecraftFacility())
        {
            var sc = rr.spacecraft;
            if (sc == null || sc.spacecraftType == null || !sc.spacecraftType.MagneticCatapult) continue;
            if (sc.GetCompany() != player) continue;
            var scName = sc.spacecraftType.NameRocketType ?? "?";
            var scId = sc.GetHashCode();
            Log($"[GACF] {providerOI.ObjectName}: found catapult {scName} id={scId}");
            if (usedCatapultIds.Contains(scId) || _busyCatapultIds.Contains(scId))
            {
                Log($"[GACF] {providerOI.ObjectName}: catapult {scName} already used, skip");
                continue;
            }
            if (!sc.IsReadyToPlan())
            {
                Log($"[GACF] {providerOI.ObjectName}: catapult {scName} NOT ready");
                continue;
            }
            var existingCycle = cm.GetCycleMission(sc);
            if (existingCycle != null)
            {
                if (!existingCycle.customNameFromPlanMission.StartsWith("[LOGI]"))
                {
                    Log($"[GACF] {providerOI.ObjectName}: catapult {scName} has non-LOGI cycle, skip");
                    continue;
                }
                Log($"[GACF] {providerOI.ObjectName}: catapult {scName} removing stale cycle \"{existingCycle.customNameFromPlanMission}\"");
                cm.RemoveCycleMission(sc);
                ResetCycleRequests(existingCycle);
            }

            if (HasCatapultActiveMission(sc, player))
            {
                Log($"[GACF] {providerOI.ObjectName}: catapult {scName} has active one-shot LOGI mission, skip");
                continue;
            }

            result.Add(sc);
            Log($"[GACF] {providerOI.ObjectName}: collected catapult {scName} id={scId} (total so far: {result.Count})");
        }
        Log($"[GACF] {providerOI.ObjectName}: returning {result.Count} catapults");
        return result;
    }

    private static bool HasCatapultActiveMission(Spacecraft sc, Company player)
    {
        var mim = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        if (mim == null) return false;
        foreach (var mi in mim.ListMissionInfo)
        {
            if (mi.complete || mi.cancel) continue;
            if (mi.company != player) continue;
            if (!mi.missionName.StartsWith("[LOGI]")) continue;
            if (mi.spacecraftInfo2 as Spacecraft == sc)
                return true;
        }
        return false;
    }

    private static bool HasLVActiveMission(LaunchVehicle lv, Company player)
    {
        var mim = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        if (mim == null) return false;
        var lvInfo = lv as ILaunchVehicleInfo;
        if (lvInfo == null) return false;
        foreach (var mi in mim.ListMissionInfo)
        {
            if (mi.complete || mi.cancel) continue;
            if (mi.company != player) continue;
            if (!mi.missionName.StartsWith("[LOGI]")) continue;
            if (mi.ListLaunchVehicleInfo2 != null && mi.ListLaunchVehicleInfo2.Contains(lvInfo))
                return true;
        }
        return false;
    }

    private static bool HasActiveToOrbitMission(ObjectInfo startOI, Company player, ObjectInfo targetOI)
    {
        var mim = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        if (mim == null) return false;
        foreach (var mi in mim.ListMissionInfo)
        {
            if (mi.complete || mi.cancel) continue;
            if (mi.company != player) continue;
            if (!mi.missionName.StartsWith("[LOGI]")) continue;
            if (mi.start == startOI && mi.target == targetOI)
                return true;
        }
        return false;
    }

    private static List<ObjectInfo> GetBestProviderOrder(
        Data.LogisticsRequest req, ObjectInfo requester, ResourceDefinition rd,
        double remaining, Company player,
        Dictionary<string, int> scActive,
        CycleMissionManager cm, string networkId)
    {
            var scored = new List<(ObjectInfo provider, double deliverable)>();

        foreach (var providerOI in Data.LogisticsNetwork.GetAllObjects())
        {
            if (providerOI == requester) continue;

            var provData = Data.LogisticsNetwork.Get(providerOI);
            if (provData == null) continue;
            if (!provData.providers.Any(p => p.isActive && !p.toOrbit && p.ResourceDefinition == rd && p.networkId == networkId))
                continue;

            var oid = providerOI.GetObjectInfoData(player);
            if (oid == null) continue;

            var available = oid.CheckResources(rd);
            var minKeep = provData.providers.Where(p => p.isActive && p.ResourceDefinition == rd && p.networkId == networkId).Sum(p => p.minimumKeep);
            available -= minKeep;
            if (available <= req.requestedAmount) continue;

            double usableExcess = available - req.requestedAmount;
            double maxTake25 = Math.Floor(usableExcess * 0.25);
            double maxDeliver500 = req.requestedAmount * 5.0;
            double extraCap = Math.Min(maxTake25, maxDeliver500);
            double targetAmount = Math.Min(available, req.requestedAmount + extraCap);
            if (targetAmount <= 0) continue;

            // Best single-ship SC capacity (closest from above, or largest if none)
            double bestSCCapacity = 0;
            foreach (var quota in provData.spacecraftQuota ?? new List<Data.ShipQuotaEntry>())
            {
                if (quota.count <= 0 || quota.networkId != networkId) continue;

                var idleSC = _cachedSpacecraft
                    .Where(sc => sc != null && sc.spacecraftType != null
                        && !sc.spacecraftType.MagneticCatapult
                        && sc.CurrentlyOnThisObject == providerOI
                        && sc.CurrentPhase == Spacecraft.EPhase.None
                        && Data.LogisticsNetwork.QuotaMatches(quota, sc.spacecraftType.ID, sc.spacecraftType.NameRocketType ?? "SC")
                        && cm.GetCycleMission(sc) == null
                        && CanSolarShipReach(sc, requester, player)
                        && (WouldGetStuckOnSurface(sc, requester)
                              ? HasFuelForReturnOrbit(sc, requester, player)
                              : HasFuelForReturn(sc, requester, player))
                        && !WouldGetStuckOnSurface(sc, providerOI)
                        && !WouldGetStuckOnReturn(sc, providerOI, requester))
                    .OrderByDescending(sc => sc.spacecraftType.GetCargoCapacity(player))
                    .ToList();

                if (idleSC.Count == 0) continue;

                var bestSingle = idleSC
                    .Where(sc => sc.spacecraftType.GetCargoCapacity(player) >= req.requestedAmount)
                    .OrderBy(sc => sc.spacecraftType.GetCargoCapacity(player))
                    .FirstOrDefault();

                double typeBest;
                if (bestSingle != null)
                {
                    typeBest = bestSingle.spacecraftType.GetCargoCapacity(player);
                }
                else
                {
                    typeBest = 0;
                    foreach (var sc in idleSC)
                    {
                        typeBest += sc.spacecraftType.GetCargoCapacity(player);
                        if (typeBest >= req.requestedAmount) break;
                    }
                }

                if (typeBest > bestSCCapacity) bestSCCapacity = typeBest;
            }

            // Also consider catapult capacity if no suitable SC
            double bestCatapultCapacity = 0;
            if (bestSCCapacity <= 0)
            {
                foreach (var lvQuota in provData.launchVehicleQuota ?? new List<Data.ShipQuotaEntry>())
                {
                    if (lvQuota.count <= 0 || lvQuota.networkId != networkId) continue;

                    foreach (var rr in oid.GetListSpacecraftFacility())
                    {
                        var sc = rr.spacecraft;
                        if (sc == null || sc.spacecraftType == null || !sc.spacecraftType.MagneticCatapult) continue;
                        if (sc.GetCompany() != player) continue;
                        if (!sc.IsReadyToPlan()) continue;

                        var existingCycle = cm.GetCycleMission(sc);
                        if (existingCycle != null && !existingCycle.customNameFromPlanMission.StartsWith("[LOGI]"))
                            continue;

                        double cap = sc.spacecraftType.GetCargoCapacity(player);
                        if (cap > bestCatapultCapacity) bestCatapultCapacity = cap;
                    }

                    if (bestCatapultCapacity > 0) break;
                }
            }

            double bestCapacity = bestSCCapacity > 0 ? bestSCCapacity : bestCatapultCapacity;
            if (bestCapacity <= 0) continue;

            double deliverable = Math.Min(targetAmount, bestCapacity);
            scored.Add((providerOI, deliverable));
        }

        return scored
            .OrderBy(p => p.deliverable >= remaining ? 0 : 1)
            .ThenBy(p => p.deliverable >= remaining
                ? p.deliverable - remaining
                : remaining - p.deliverable)
            .Select(p => p.provider)
            .ToList();
    }

    private static void TryCreateDeliveries(Data.LogisticsRequest req, ObjectInfo requester,
        ResourceDefinition rd, double remaining, Company player,
        Dictionary<string, int> scActive, Dictionary<string, int> lvActive,
        HashSet<int> usedLvIds, HashSet<int> usedCatapultIds, string networkId)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null) return;

        var reqName = requester?.ObjectName ?? "?";
        var rdName = rd?.Name ?? "?";

        Log($"[TCD] {reqName}/{rdName}: starting remaining={remaining}");

        var orderedProviders = GetBestProviderOrder(req, requester, rd, remaining, player, scActive, cm, networkId);
        foreach (var providerOI in orderedProviders)
        {
            if (remaining <= 0) break;

            var provName = providerOI?.ObjectName ?? "?";

            var provData = Data.LogisticsNetwork.Get(providerOI);
            if (provData == null) continue;

            if (!provData.providers.Any(p => p.isActive && !p.toOrbit && p.ResourceDefinition == rd && p.networkId == networkId))
                continue;

            var oid = providerOI.GetObjectInfoData(player);
            if (oid == null) continue;

            var available = oid.CheckResources(rd);
            var minKeep = provData.providers.Where(p => p.isActive && !p.toOrbit && p.ResourceDefinition == rd && p.networkId == networkId).Sum(p => p.minimumKeep);
            available -= minKeep;
            if (available <= req.requestedAmount)
            {
                Log($"[TCD] {reqName}/{rdName}: prov={provName} available={available} <= requestedAmount={req.requestedAmount} -> skip");
                continue;
            }

            double toDeliver = available;

            double usableExcess = available - req.requestedAmount;
            double maxTake25 = Math.Floor(usableExcess * 0.25);
            double maxDeliver500 = req.requestedAmount * 5.0;
            double extraCap = Math.Min(maxTake25, maxDeliver500);
            double targetAmount = Math.Min(toDeliver, req.requestedAmount + extraCap);
            if (targetAmount <= 0) continue;

            Log($"[TCD] {reqName}/{rdName}: prov={provName} available={available} minKeep={minKeep} toDeliver={toDeliver} targetAmount={targetAmount} extraCap={extraCap}");

            bool delivered = false;

            Log($"[TCD] {reqName}/{rdName}: SC quotas={provData.spacecraftQuota?.Count ?? 0} LV quotas={provData.launchVehicleQuota?.Count ?? 0} idleSC={_cachedSpacecraft?.Count ?? 0} idleLV={_cachedLaunchVehicles?.Count ?? 0}");

            // SC delivery
            foreach (var quota in provData.spacecraftQuota ?? new List<Data.ShipQuotaEntry>())
            {
                if (quota.count <= 0 || quota.networkId != networkId) continue;

                var idleSC = _cachedSpacecraft
                    .Where(sc => sc != null && sc.spacecraftType != null
                        && !sc.spacecraftType.MagneticCatapult
                        && sc.CurrentlyOnThisObject == providerOI
                        && sc.CurrentPhase == Spacecraft.EPhase.None
                        && Data.LogisticsNetwork.QuotaMatches(quota, sc.spacecraftType.ID, sc.spacecraftType.NameRocketType ?? "SC")
                        && cm.GetCycleMission(sc) == null
                        && CanSolarShipReach(sc, requester, player)
                        && (WouldGetStuckOnSurface(sc, requester)
                              ? HasFuelForReturnOrbit(sc, requester, player)
                              : HasFuelForReturn(sc, requester, player))
                        && !WouldGetStuckOnSurface(sc, providerOI)
                        && !WouldGetStuckOnReturn(sc, providerOI, requester))
                    .OrderByDescending(sc => sc.spacecraftType.GetCargoCapacity(player))
                    .ToList();

                if (idleSC.Count == 0)
                {
                    Log($"[TCD] {reqName}/{rdName}: SC quota type={quota.typeName} quota={quota.count} — NO idle SC at {provName}");
                    continue;
                }

                var bestSingle = idleSC
                    .Where(sc => sc.spacecraftType.GetCargoCapacity(player) >= req.requestedAmount)
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
                        if (totalCapacity >= req.requestedAmount) break;
                    }
                }

                double actualAmount = Math.Min(targetAmount, totalCapacity);
                Log($"[TCD] {reqName}/{rdName}: SC delivery via {provName} type={quota.typeName} ships={selectedShips.Count} targetAmount={targetAmount} actual={actualAmount}");
                SetupCycleMission(req, selectedShips, rd, actualAmount, requester, providerOI, networkId);
                var scKey = ActiveKey(networkId, quota.typeName);
                scActive.TryGetValue(scKey, out var cur);
                scActive[scKey] = cur + selectedShips.Count;
                remaining -= actualAmount;
                delivered = true;
                if (remaining <= 0) return;
                break;
            }

            if (delivered) continue;

            // LV delivery — enabled LV types only (quota > 0 means enabled)
            var enabledLVTypes = (provData.launchVehicleQuota ?? new List<Data.ShipQuotaEntry>())
                .Where(q => q.count > 0 && q.networkId == networkId)
                .Select(q => q.typeName)
                .ToHashSet();

            if (enabledLVTypes.Count == 0)
            {
                Log($"[TCD] {reqName}/{rdName}: SKIP {provName} — no enabled LV types");
                continue;
            }

            var allLVAtProvider = _cachedLaunchVehicles
                .Where(lv => lv != null && lv.launchVehicleType != null
                    && lv.objectInfo == providerOI
                    && lv.IsReadyToLaunchReusable()
                    && lv.launchVehicleType.FakeForFacility
                    && enabledLVTypes.Contains(Data.LogisticsNetwork.TypeKey(lv.launchVehicleType.ID, lv.launchVehicleType.Name ?? "LV")))
                .ToList();

            Log($"[TCD] {reqName}/{rdName}: {allLVAtProvider.Count} LVs at {provName}, usedLvIds count={usedLvIds.Count}, content=[{string.Join(",", usedLvIds)}]");

            var availableLV = allLVAtProvider
                .Where(lv => !usedLvIds.Contains(lv.GetHashCode()) && !_busyLvIds.Contains(lv.GetHashCode()))
                .ToList();

            if (availableLV.Count == 0)
            {
                Log($"[TCD] {reqName}/{rdName}: NO ready LV at {provName} (filtered by usedLvIds={usedLvIds.Count} busyLvIds={_busyLvIds.Count})");
                Log($"[TCD] {reqName}/{rdName}: All LV at provider: {string.Join(", ", allLVAtProvider.Select(lv => $"{lv.launchVehicleType.Name} id={lv.GetHashCode()}"))}");
                continue;
            }

            Log($"[TCD] {reqName}/{rdName}: {availableLV.Count} LVs available at {provName}");

            // Best-fit LV selection (same two-phase logic as SC)
            var sortedLV = availableLV
                .OrderByDescending(lv => lv.launchVehicleType.MaxPayloadOnThisObject(providerOI, player))
                .ToList();

            var bestLV = sortedLV
                .Where(lv => lv.launchVehicleType.MaxPayloadOnThisObject(providerOI, player) >= targetAmount)
                .OrderBy(lv => lv.launchVehicleType.MaxPayloadOnThisObject(providerOI, player))
                .FirstOrDefault() ?? sortedLV[0];

            var lvType = bestLV.launchVehicleType;
            Log($"[TCD] {reqName}/{rdName}: selected LV type={lvType.Name} payload={lvType.MaxPayloadOnThisObject(providerOI, player)} FakeForFacility={lvType.FakeForFacility}");

            if (bestLV.launchVehicleType.FakeForFacility)
            {
                Log($"[TCD] {reqName}/{rdName}: attempting catapult delivery, usedCatapultIds=[{string.Join(",", usedCatapultIds)}]");
                var allMagScs = GetAllAvailableCatapultsFromFacility(providerOI, player, usedCatapultIds);
                if (allMagScs.Count > 0)
                {
                    double lvCapacity = bestLV.launchVehicleType.MaxPayloadOnThisObject(providerOI, player);
                    int needed = Math.Max(1, (int)Math.Ceiling(Math.Min(targetAmount, remaining) / lvCapacity));
                    var magScs = allMagScs.Take(needed).ToList();
                    double totalMagCapacity = magScs.Sum(sc => sc.spacecraftType?.GetCargoCapacity(player) ?? 0);
                    double lvActualAmount = Math.Min(targetAmount, Math.Min(lvCapacity, totalMagCapacity));
                    Log($"[TCD] {reqName}/{rdName}: CATAPULT all={allMagScs.Count} needed={needed} selected={magScs.Count} lvCapacity={lvCapacity} totalMagCapacity={totalMagCapacity} lvActualAmount={lvActualAmount}");

                    foreach (var magSc in magScs)
                    {
                        usedCatapultIds.Add(magSc.GetHashCode());
                        _busyCatapultIds.Add(magSc.GetHashCode());
                    }
                    usedLvIds.Add(bestLV.GetHashCode());
                    _busyLvIds.Add(bestLV.GetHashCode());
                    _busyLvTick[bestLV.GetHashCode()] = _tickCount;

                    CreateOneShotCatapultMission(req, magScs, rd, lvActualAmount, requester, providerOI, bestLV, networkId);
                    var lvKey = Data.LogisticsNetwork.TypeKey(lvType.ID, lvType.Name ?? "LV");
                    var lvActiveKey = ActiveKey(networkId, lvKey);
                    lvActive.TryGetValue(lvActiveKey, out var lvCur);
                    lvActive[lvActiveKey] = lvCur + 1;
                    remaining -= lvActualAmount;
                    if (remaining <= 0) return;
                }
                else
                {
                    Log($"[TCD] {reqName}/{rdName}: no MagneticCatapult at {provName} — skip");
                }
            }
        }
    }

    private static void SetupCycleMission(Data.LogisticsRequest req, List<Spacecraft> scs,
        ResourceDefinition rd, double amount, ObjectInfo requesterOI, ObjectInfo providerOI,
        string networkId, LaunchVehicleType lvTypeA = null)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance.Player;
        var firstSc = scs[0];
        if (firstSc == null) return;

        var realProvider = firstSc.CurrentlyOnThisObject;
        if (realProvider == null) return;

        Log($"[SCM] SC mission: {realProvider.ObjectName}->{requesterOI.ObjectName} rd={rd?.Name} amount={amount} ships={scs.Count} lvType={(lvTypeA?.Name ?? "none")}");

        var scList = scs.Select(s => s as ISpacecraftInfo).ToList();
        var cargoToB = new CargoAll();
        var item = new Cargo(cargoToB) { resourceType = rd, cargoMass = amount, resourceTypeType = EResourceTypeType.resorces };
        cargoToB.listCargo.Add(item);

        var cmdData = new CycleMissionsDataData
        {
            A = realProvider, B = requesterOI, Company = player,
            CargoStart = ECargoStart.FlyWithWhatIsAvailable, CargoEnd = ECargoStart.FlyWithWhatIsAvailable,
            CargoAllStart = cargoToB, CargoAllEnd = new CargoAll(),
            LvTypeA = lvTypeA, LvTypeB = null, TransferType = ETransferType.Fastest,
            Ends = EEnds.ThisManyTimes, EndsObjectThisManyTimes = 1, ListSC = scList
        };
        var cmd = new CycleMissionsData(cmdData);
        MonoBehaviourSingleton<CycleMissionManager>.Instance.AddCycleMission(firstSc, cmd, scList);

        req.status = Data.LogisticsRequestStatus.InProgress;

        cmd.customNameFromPlanMission = $"[LOGI][{networkId}] {realProvider.ObjectName} → {requesterOI.ObjectName}";

        var ctrl = firstSc.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
        if (ctrl == null)
            ctrl = firstSc.gameObject.AddComponent<SpaceCraftCyclicalMissionController>();
        ctrl.CycleMissionPlanFlyWas = false;
        ctrl.SetSC(firstSc);
        ctrl.TryPlanCycleMission();
    }

    private static void SetupCycleMission(Data.LogisticsRequest req, Spacecraft container,
        ResourceDefinition rd, double amount, ObjectInfo requesterOI, ObjectInfo providerOI,
        LaunchVehicleType lvTypeA, string networkId)
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
            LvTypeA = lvTypeA, LvTypeB = null, TransferType = ETransferType.Fastest,
            Ends = EEnds.ThisManyTimes, EndsObjectThisManyTimes = 1, ListSC = scList
        };
        var cmd = new CycleMissionsData(cmdData);
        MonoBehaviourSingleton<CycleMissionManager>.Instance.AddCycleMission(container, cmd, scList);

        req.status = Data.LogisticsRequestStatus.InProgress;

        cmd.customNameFromPlanMission = $"[LOGI][{networkId}] {realProvider.ObjectName} → {requesterOI.ObjectName}";

        var ctrl = container.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
        if (ctrl == null)
            ctrl = container.gameObject.AddComponent<SpaceCraftCyclicalMissionController>();
        ctrl.CycleMissionPlanFlyWas = false;
        ctrl.SetSC(container);
        ctrl.TryPlanCycleMission(loadLimit2: amount);
    }

    private static void CreateOneShotCatapultMission(Data.LogisticsRequest req, List<Spacecraft> catapults,
        ResourceDefinition rd, double amount, ObjectInfo requesterOI, ObjectInfo providerOI,
        LaunchVehicle lv, string networkId)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance.Player;
        if (catapults == null || catapults.Count == 0 || player == null)
        {
            LogWarning($"[OCM] ABORT: catapults.Count={(catapults?.Count ?? 0)}, player={(player != null)}");
            return;
        }

        var firstCatapult = catapults[0];

        Log($"[OCM] creating one-shot mission: {providerOI.ObjectName}->{requesterOI.ObjectName} rd={rd?.Name} amount={amount} lv={lv?.launchVehicleType?.Name} catapults={catapults.Count} firstType={firstCatapult?.spacecraftType?.NameRocketType}");

        // Dedup: check if in-transit cargo already covers the request
        {
            var mimDedup = MonoBehaviourSingleton<MissionInfoManager>.Instance;
            double alreadyInTransit = 0;
            int totalLogiMissions = 0;
            int matchedTargetMissions = 0;
            int matchedCargoMissions = 0;
            if (mimDedup != null)
            {
                foreach (var mi in mimDedup.ListMissionInfo)
                {
                    if (mi.complete || mi.cancel) continue;
                    if (mi.company != player) continue;
                    if (!mi.missionName.StartsWith("[LOGI]")) continue;
                    totalLogiMissions++;
                    if (mi.target != requesterOI) continue;
                    matchedTargetMissions++;
                    if (mi.cargoAll?.listCargo == null) continue;
                    bool foundCargo = false;
                    foreach (var c in mi.cargoAll.listCargo)
                    {
                        if (c.resourceType == rd)
                        {
                            alreadyInTransit += c.cargoMass;
                            foundCargo = true;
                        }
                    }
                    if (foundCargo) matchedCargoMissions++;
                }
            }
            Log($"[OCM] dedup: totalLOGI={totalLogiMissions} matchedTarget={matchedTargetMissions} matchedCargo={matchedCargoMissions} inTransit={alreadyInTransit:F2} requesterHash={requesterOI?.GetHashCode()}");
            double alreadyThere = requesterOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
            double totalCovered = alreadyThere + alreadyInTransit;
            if (totalCovered >= req.requestedAmount)
            {
                Log($"[OCM] SKIP — totalCovered={totalCovered:F2} >= requested={req.requestedAmount:F2} (onSite={alreadyThere:F2} inTransit={alreadyInTransit:F2})");
                req.status = Data.LogisticsRequestStatus.Satisfied;
                return;
            }
            Log($"[OCM] totalCovered={totalCovered:F2} < requested={req.requestedAmount:F2} — proceeding with mission");
        }
        var scList = catapults.Select(c => c as ISpacecraftInfo).ToList();
        var lvList = new List<ILaunchVehicleInfo> { lv };

        var cargoToB = new CargoAll();
        var item = new Cargo(cargoToB) { resourceType = rd, cargoMass = amount, resourceTypeType = EResourceTypeType.resorces };
        cargoToB.listCargo.Add(item);

        var gm = MonoBehaviourSingleton<GameManager>.Instance;

        var ppm = new PMMissionParameter();
        ppm.ForCyclicalMission = true;
        ppm.ReduceFuelToMinimum = false;
        ppm.SetMissionOrigin(MissionInfo.EMissionCreator.Manual);
        ppm.TryFastAsPossible = true;
        ppm.ClickFastestButton = true;
        ppm.SetCompany(player);
        ppm.SetTabDestination(providerOI, requesterOI);
        ppm.SetTabSC(scList, catapults.Count);
        ppm.ChangeMissionName($"[LOGI][{networkId}] {providerOI.ObjectName} → {requesterOI.ObjectName}", _manualChangeName: true);
        ppm.SetTabCargo(cargoToB);
        ppm.SetTabLV(lvList, 1);
        ppm.ChangeStage(PlanMissionWindow.EStageWindow.Schedule);
        ppm.TrajectoryColor = Color.blue;

        Log($"[OCM] PMMissionParameter ready: player={player.ID} start={providerOI?.ObjectName} target={requesterOI?.ObjectName} cargo={rd?.Name}={amount} SCs={catapults.Count} firstType={firstCatapult?.spacecraftType?.NameRocketType} LV={lv?.launchVehicleType?.Name} ForCyclicalMission={ppm.ForCyclicalMission}");

        var checkResult = ppm.CheckCanPlanMission();
        Log($"[OCM] CheckCanPlanMission: result={checkResult.planMissionResult} fuelNeed={checkResult.allFuelNeed} cost={checkResult.allCostDollars} start={checkResult.dateStart} end={checkResult.dateEnd}");
        if (checkResult.planMissionResult != PMMissionParameter.EPlanMissionResult.AllOk)
            LogWarning($"[OCM] CheckCanPlanMission NOT AllOk: {checkResult.planMissionResult} — forcing PlanFlyCode anyway");

        gm.PlanFlyCode(ppm, silenceModeOn: true);

        Log($"[OCM] PlanFlyCode called, waiting for async porkchop...");
        req.status = Data.LogisticsRequestStatus.InProgress;
    }

    private static void TryCreateSurfaceToOrbitDelivery(Data.LogisticsRequest req, ObjectInfo requester,
        ResourceDefinition rd, double remaining, Company player,
        HashSet<int> usedLvIds, HashSet<int> usedCatapultIds, string networkId)
    {
        if (requester.objectTypes != EObjectTypes.Orbit) return;
        var surfaceBody = requester.parentObjectInfo;
        if (surfaceBody == null || surfaceBody.objectTypes == EObjectTypes.Orbit) return;
        var surfaceData = Data.LogisticsNetwork.Get(surfaceBody);
        if (surfaceData == null) return;

        // Check for active provider on the surface body
        if (!surfaceData.providers.Any(p => p.isActive && p.ResourceDefinition == rd && p.networkId == networkId))
        {
            Log($"[STO] {surfaceBody.ObjectName} -> {requester.ObjectName}: no active provider for {rd?.Name}");
            return;
        }

        var oid = surfaceBody.GetObjectInfoData(player);
        if (oid == null) return;

        var available = oid.CheckResources(rd);
        var minKeep = surfaceData.providers
            .Where(p => p.isActive && p.ResourceDefinition == rd && p.networkId == networkId)
            .Sum(p => p.minimumKeep);
        available -= minKeep;
        if (available <= req.requestedAmount)
        {
            Log($"[STO] {surfaceBody.ObjectName}: available={available} <= requestedAmount={req.requestedAmount} -> skip");
            return;
        }

        double toDeliver = Math.Min(remaining, available);
        double usableExcess = available - req.requestedAmount;
        double maxTake25 = Math.Floor(usableExcess * 0.25);
        double maxDeliver500 = req.requestedAmount * 5.0;
        double extraCap = Math.Min(maxTake25, maxDeliver500);
        double targetAmount = Math.Min(toDeliver, req.requestedAmount + extraCap);
        if (targetAmount <= 0) return;

        // Find enabled LV types on the surface body (any LV = quota > 0)
        var enabledLVTypes = (surfaceData.launchVehicleQuota ?? new List<Data.ShipQuotaEntry>())
            .Where(q => q.count > 0 && q.networkId == networkId)
            .Select(q => q.typeName)
            .ToHashSet();

        if (enabledLVTypes.Count == 0)
        {
            Log($"[STO] {surfaceBody.ObjectName}: no enabled LV types for network '{networkId}'");
            return;
        }

        var availableLVs = _cachedLaunchVehicles
            .Where(lv => lv != null && lv.launchVehicleType != null
                && lv.objectInfo == surfaceBody
                && lv.IsReadyToLaunchReusable()
                && enabledLVTypes.Contains(Data.LogisticsNetwork.TypeKey(lv.launchVehicleType.ID, lv.launchVehicleType.Name ?? "LV"))
                && !usedLvIds.Contains(lv.GetHashCode())
                && !_busyLvIds.Contains(lv.GetHashCode()))
            .ToList();

        if (availableLVs.Count == 0)
        {
            Log($"[STO] {surfaceBody.ObjectName}: no ready LVs available (filtered by used={usedLvIds.Count} busy={_busyLvIds.Count})");
            return;
        }

        // Get a LowOrbitContainer as payload
        var shipManager = MonoBehaviourSingleton<ShipManager>.Instance;
        var lowOrbitContainer = shipManager?.GetFreeToUseLowOrbitContainerForPlayer();
        if (lowOrbitContainer == null)
        {
            LogWarning($"[STO] {surfaceBody.ObjectName}: no LowOrbitContainer available");
            return;
        }

        // Best-fit LV selection — only LVs of a single type per mission
        // Phase 1: find a type where a single LV covers targetAmount (closest from above)
        var bestSingleLV = availableLVs
            .Where(lv => lv.launchVehicleType.MaxPayloadOnThisObject(surfaceBody, player) >= targetAmount)
            .OrderBy(lv => lv.launchVehicleType.MaxPayloadOnThisObject(surfaceBody, player))
            .FirstOrDefault();

        List<LaunchVehicle> selectedLVs;
        double totalCapacity;

        if (bestSingleLV != null)
        {
            selectedLVs = new List<LaunchVehicle> { bestSingleLV };
            totalCapacity = bestSingleLV.launchVehicleType.MaxPayloadOnThisObject(surfaceBody, player);
        }
        else
        {
            // Phase 2: pick the type with the largest per-LV capacity, combine multiple of that type
            var groupedByType = availableLVs
                .GroupBy(lv => Data.LogisticsNetwork.TypeKey(lv.launchVehicleType.ID, lv.launchVehicleType.Name ?? "LV"))
                .OrderByDescending(g => g.First().launchVehicleType.MaxPayloadOnThisObject(surfaceBody, player))
                .ToList();

            var bestTypeGroup = groupedByType.First();
            var sortedOfType = bestTypeGroup
                .OrderByDescending(lv => lv.launchVehicleType.MaxPayloadOnThisObject(surfaceBody, player))
                .ToList();

            selectedLVs = new List<LaunchVehicle>();
            totalCapacity = 0;
            foreach (var lv in sortedOfType)
            {
                selectedLVs.Add(lv);
                totalCapacity += lv.launchVehicleType.MaxPayloadOnThisObject(surfaceBody, player);
                if (totalCapacity >= targetAmount) break;
            }
        }

        if (selectedLVs.Count == 0)
        {
            Log($"[STO] {surfaceBody.ObjectName}: no LV selected");
            return;
        }

        double actualAmount = Math.Min(targetAmount, totalCapacity);
        Log($"[STO] {surfaceBody.ObjectName} -> {requester.ObjectName}: LVs={selectedLVs.Count} totalCapacity={totalCapacity} target={targetAmount} actual={actualAmount}");

        // Mark selected LVs as used
        var lvList = new List<ILaunchVehicleInfo>();
        foreach (var slv in selectedLVs)
        {
            usedLvIds.Add(slv.GetHashCode());
            _busyLvIds.Add(slv.GetHashCode());
            _busyLvTick[slv.GetHashCode()] = _tickCount;
            lvList.Add(slv);
        }
        var primaryLVType = selectedLVs[0].launchVehicleType;
        Log($"[STO] using LVs: {string.Join(", ", selectedLVs.Select(lv => $"{lv.launchVehicleType.Name} id={lv.GetHashCode()}"))}");

        // Dedup check
        {
            var mim = MonoBehaviourSingleton<MissionInfoManager>.Instance;
            double inTransit = 0;
            if (mim != null)
            {
                foreach (var mi in mim.ListMissionInfo)
                {
                    if (mi.complete || mi.cancel) continue;
                    if (mi.company != player) continue;
                    if (mi.target != requester) continue;
                    if (!mi.missionName.StartsWith("[LOGI]")) continue;
                    if (mi.cargoAll?.listCargo == null) continue;
                    foreach (var c in mi.cargoAll.listCargo)
                    {
                        if (c.resourceType == rd)
                            inTransit += c.cargoMass;
                    }
                }
            }
            double alreadyThere = requester.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
            if (alreadyThere + inTransit >= req.requestedAmount)
            {
                Log($"[STO] SKIP — totalCovered >= requested");
                req.status = Data.LogisticsRequestStatus.Satisfied;
                return;
            }
        }

        // Create one-shot mission: surface body -> requester (orbit) with LV(s) + LowOrbitContainer
        var gm = MonoBehaviourSingleton<GameManager>.Instance;
        var scList = new List<ISpacecraftInfo> { lowOrbitContainer };

        var cargoToB = new CargoAll();
        var item = new Cargo(cargoToB) { resourceType = rd, cargoMass = actualAmount, resourceTypeType = EResourceTypeType.resorces };
        cargoToB.listCargo.Add(item);

        var ppm = new PMMissionParameter();
        ppm.ForCyclicalMission = true;
        ppm.ReduceFuelToMinimum = false;
        ppm.SetMissionOrigin(MissionInfo.EMissionCreator.Manual);
        ppm.TryFastAsPossible = true;
        ppm.ClickFastestButton = true;
        ppm.SetCompany(player);
        ppm.SetTabDestination(surfaceBody, requester);
        ppm.SetTabSC(scList, 1);
        ppm.ChangeMissionName($"[LOGI][{networkId}] {surfaceBody.ObjectName} → {requester.ObjectName}", _manualChangeName: true);
        ppm.SetTabCargo(cargoToB);
        ppm.SetTabLV(lvList, lvList.Count);
        ppm.ChangeStage(PlanMissionWindow.EStageWindow.Schedule);
        ppm.TrajectoryColor = Color.blue;

        Log($"[STO] PMMissionParameter ready: start={surfaceBody.ObjectName} target={requester.ObjectName} cargo={rd?.Name}={actualAmount} LVs={lvList.Count} primaryType={primaryLVType.Name} container={lowOrbitContainer?.name}");

        var checkResult = ppm.CheckCanPlanMission();
        Log($"[STO] CheckCanPlanMission: result={checkResult.planMissionResult}");
        if (checkResult.planMissionResult != PMMissionParameter.EPlanMissionResult.AllOk)
            LogWarning($"[STO] CheckCanPlanMission NOT AllOk: {checkResult.planMissionResult} — forcing PlanFlyCode anyway");

        gm.PlanFlyCode(ppm, silenceModeOn: true);
        Log($"[STO] PlanFlyCode called, waiting for async porkchop...");
        req.status = Data.LogisticsRequestStatus.InProgress;
    }

    private static void CleanupStuckMissions(Company player, CycleMissionManager cm)
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

            bool hasPastDate = mi.DateLaunch != default && mi.DateLaunch <= currentTime;
            bool hasNoDate = mi.DateLaunch == default;
            if (!hasPastDate && !hasNoDate) continue;

            if (hasPastDate && sc.CurrentPhase != Spacecraft.EPhase.None && sc.CurrentPhase != Spacecraft.EPhase.PlanedMission) continue;

            // For one-shot LOGI missions: if the SC is idle but cargo hasn't arrived,
            // the payload is in flight — don't cancel.
            if (!mi.fromCyclicalMission && hasPastDate && sc.CurrentPhase == Spacecraft.EPhase.None)
            {
                bool cargoNeeded = false;
                if (mi.target != null && mi.cargoAll?.listCargo != null)
                {
                    var oid = mi.target.GetObjectInfoData(mi.company);
                    if (oid != null)
                    {
                        foreach (var c in mi.cargoAll.listCargo)
                        {
                            if (c.resourceType != null && oid.CheckResources(c.resourceType) < c.cargoMass)
                                { cargoNeeded = true; break; }
                        }
                    }
                }
                if (cargoNeeded)
                {
                    Log($"[CSM] SKIP cancel — one-shot LOGI mission \"{mi.missionName}\" is in flight (cargo not yet delivered)");
                    continue;
                }
            }

            Log($"[CSM] stuck mission: \"{mi.missionName}\" target={mi.target?.ObjectName} cyclical={mi.fromCyclicalMission} dateLaunch={mi.DateLaunch} current={currentTime} phase={sc.CurrentPhase}");

            if (mi.fromCyclicalMission)
            {
                var cmd = cm.GetCycleMission(sc);
                if (cmd == null || !cmd.customNameFromPlanMission.StartsWith("[LOGI]")) continue;

                if (sc.CurrentlyOnThisObject == cmd.B)
                {
                    Log($"[CSM] SKIP — SC at destination (B=\"{cmd.B?.ObjectName}\"), waiting for return");
                    continue;
                }

                cm.RemoveCycleMission(sc);
                ResetCycleRequests(cmd);
                logiStuck++;
                Log($"[CSM] removed cyclical cycle \"{cmd.customNameFromPlanMission}\"");
            }
            else
            {
                sc.CancelMission(mi);
                Log($"[CSM] cancelled one-shot mission \"{mi.missionName}\"");

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
                                Log($"[CSM] reset request {firstCargo.resourceType?.Name} on {requester.ObjectName} to Pending");
                            }
                        }
                    }
                }

                logiStuck++;
            }
        }

        if (logiStuck > 0)
            Log($"[CSM] total stuck cleaned: {logiStuck}");
    }

    private static void TryCreateBatchedToOrbitDeliveries(Company player,
        HashSet<int> usedLvIds, HashSet<int> usedCatapultIds, string networkId)
    {
        foreach (var oi in Data.LogisticsNetwork.GetAllObjects())
        {
            var data = Data.LogisticsNetwork.Get(oi);
            if (data == null) continue;

            var toOrbitProvs = data.providers
                .Where(p => p.isActive && p.toOrbit && p.networkId == networkId && p.ResourceDefinition != null)
                .ToList();
            if (toOrbitProvs.Count == 0) continue;

            if (oi.objectTypes == EObjectTypes.Orbit) continue;
            var orbitOI = Data.LogisticsNetwork.GetLowOrbitOf(oi);
            if (orbitOI == null) continue;

            var oid = oi.GetObjectInfoData(player);
            if (oid == null) continue;

            // Collect excesses from all to-orbit providers on this object
            var batchEntries = new List<(ResourceDefinition rd, double excess)>();
            double totalExcess = 0;
            foreach (var prov in toOrbitProvs)
            {
                var rd = prov.ResourceDefinition;
                var available = oid.CheckResources(rd);
                double excess = available - prov.minimumKeep;
                if (excess <= 0) continue;
                batchEntries.Add((rd, excess));
                totalExcess += excess;
            }

            if (batchEntries.Count == 0 || totalExcess <= 0) continue;

            // Find enabled LV types on this object
            var enabledLVTypes = (data.launchVehicleQuota ?? new List<Data.ShipQuotaEntry>())
                .Where(q => q.count > 0 && q.networkId == networkId)
                .Select(q => q.typeName)
                .ToHashSet();
            if (enabledLVTypes.Count == 0) continue;

            if (enabledLVTypes.Count == 0) continue;

            var shipManager = MonoBehaviourSingleton<ShipManager>.Instance;
            var gm = MonoBehaviourSingleton<GameManager>.Instance;

            // Skip if there's already an active to-orbit mission for this body
            if (HasActiveToOrbitMission(oi, player, orbitOI))
            {
                Log($"[BTO] {oi.ObjectName}: active to-orbit mission exists, skip");
                continue;
            }

            // One LV per mission, re-query each iteration to respect usedLvIds/MIM
            double reservedFuel = 0;
            while (true)
            {
                // DIAG: log all candidate LVs on this body and why rejected
                if (_logDiag)
                {
                    foreach (var cand in _cachedLaunchVehicles)
                    {
                        if (cand == null || cand.objectInfo != oi) continue;
                        var why = new List<string>();
                        if (!enabledLVTypes.Contains(Data.LogisticsNetwork.TypeKey(cand.launchVehicleType?.ID ?? "", cand.launchVehicleType?.Name ?? "LV")))
                            why.Add("typeDisabled");
                        if (usedLvIds.Contains(cand.GetHashCode()))
                            why.Add("usedLvIds");
                        if (_busyLvIds.Contains(cand.GetHashCode()))
                            why.Add("busy");
                        if (HasLVActiveMission(cand, player))
                            why.Add("HasMission");
                        if (!cand.IsReadyToLaunchReusable())
                            why.Add("notReady");
                        string decision = why.Count == 0 ? "SELECT" : string.Join("+", why);
                        Log($"[BTO] DIAG LV={cand.launchVehicleType?.Name}({cand.GetHashCode()}) on {oi.ObjectName}: {decision}");
                    }
                }
                var lv = _cachedLaunchVehicles
                    .Where(lv => lv != null && lv.launchVehicleType != null
                        && lv.objectInfo == oi
                        && lv.IsReadyToLaunchReusable()
                        && enabledLVTypes.Contains(Data.LogisticsNetwork.TypeKey(lv.launchVehicleType.ID, lv.launchVehicleType.Name ?? "LV"))
                        && !usedLvIds.Contains(lv.GetHashCode())
                        && !_busyLvIds.Contains(lv.GetHashCode())
                        && !HasLVActiveMission(lv, player))
                    .OrderByDescending(lv => lv.launchVehicleType.MaxPayloadOnThisObject(oi, player))
                    .FirstOrDefault();
                if (lv == null) break;

                double lvPayload = lv.launchVehicleType.MaxPayloadOnThisObject(oi, player);
                if (lvPayload <= 0 || totalExcess < lvPayload) break;

                // Fuel check: ensure the surface body has enough fuel for this LV
                var fuelType = lv.launchVehicleType.FuelTypeOnStart;
                double fuelCapacity = lv.launchVehicleType.maxFuelLoad;
                if (fuelType != null && fuelCapacity > 0)
                {
                    double fuelAvailable = oid.CheckResources(fuelType);
                    if (fuelAvailable < fuelCapacity + reservedFuel)
                    {
                        Log($"[BTO] {oi.ObjectName}: {lv.launchVehicleType.Name} needs {fuelCapacity} {fuelType.Name}, only {fuelAvailable} available ({reservedFuel} reserved) — skip");
                        break;
                    }
                    reservedFuel += fuelCapacity;
                }

                // Proportional distribution of one LV load across all resources
                var allocations = new List<(ResourceDefinition rd, double amount)>();
                double sumAllocated = 0;
                int largestIdx = 0;
                double largestExcess = 0;

                for (int i = 0; i < batchEntries.Count; i++)
                {
                    var (rd, excess) = batchEntries[i];
                    double share = Math.Floor((excess / totalExcess) * lvPayload);
                    if (share > 0)
                    {
                        allocations.Add((rd, share));
                        sumAllocated += share;
                    }
                    if (excess > largestExcess)
                    {
                        largestExcess = excess;
                        largestIdx = i;
                    }
                }

                if (allocations.Count == 0) break;

                // Distribute remainder to largest excess
                double remainder = lvPayload - sumAllocated;
                if (remainder > 0)
                {
                    var idx = allocations.FindIndex(a => a.rd == batchEntries[largestIdx].rd);
                    if (idx >= 0)
                    {
                        var old = allocations[idx];
                        allocations[idx] = (old.rd, old.amount + remainder);
                    }
                    else
                    {
                        allocations.Add((batchEntries[largestIdx].rd, remainder));
                    }
                }

                // Get LowOrbitContainer
                var lowOrbitContainer = shipManager?.GetFreeToUseLowOrbitContainerForPlayer();
                if (lowOrbitContainer == null)
                {
                    LogWarning($"[BTO] {oi.ObjectName}: no LowOrbitContainer available, stopping");
                    break;
                }

                // Build mixed cargo
                var cargoToB = new CargoAll();
                foreach (var (rd, amount) in allocations)
                {
                    var item = new Cargo(cargoToB) { resourceType = rd, cargoMass = amount, resourceTypeType = EResourceTypeType.resorces };
                    cargoToB.listCargo.Add(item);
                    Log($"[BTO] {oi.ObjectName}: to-orbit cargo {rd.Name}={amount}");
                }

                // Create one-shot mission: 1 LV + LowOrbitContainer
                var scList = new List<ISpacecraftInfo> { lowOrbitContainer };
                var lvList = new List<ILaunchVehicleInfo> { lv };

                usedLvIds.Add(lv.GetHashCode());
                _busyLvIds.Add(lv.GetHashCode());
                _busyLvTick[lv.GetHashCode()] = _tickCount;

                var ppm = new PMMissionParameter();
                ppm.ForCyclicalMission = true;
                ppm.ReduceFuelToMinimum = false;
                ppm.SetMissionOrigin(MissionInfo.EMissionCreator.Manual);
                ppm.TryFastAsPossible = true;
                ppm.ClickFastestButton = true;
                ppm.SetCompany(player);
                ppm.SetTabDestination(oi, orbitOI);
                ppm.SetTabSC(scList, 1);
                ppm.ChangeMissionName($"[LOGI][{networkId}] ↑{oi.ObjectName} orbit", _manualChangeName: true);
                ppm.SetTabCargo(cargoToB);
                ppm.SetTabLV(lvList, 1);
                ppm.ChangeStage(PlanMissionWindow.EStageWindow.Schedule);
                ppm.TrajectoryColor = Color.blue;

                Log($"[BTO] {oi.ObjectName} -> {orbitOI.ObjectName}: LV payload={lvPayload} resources={allocations.Count}");

                var checkResult = ppm.CheckCanPlanMission();
                if (checkResult.planMissionResult != PMMissionParameter.EPlanMissionResult.AllOk)
                    LogWarning($"[BTO] CheckCanPlanMission NOT AllOk: {checkResult.planMissionResult} — forcing PlanFlyCode anyway");

                gm.PlanFlyCode(ppm, silenceModeOn: true);
                Log($"[BTO] PlanFlyCode called for {oi.ObjectName} orbit mission with {lv.launchVehicleType.Name}");

                // Deduct from excesses
                for (int i = 0; i < batchEntries.Count; i++)
                {
                    var (rd, excess) = batchEntries[i];
                    var alloc = allocations.Find(a => a.rd == rd);
                    if (alloc.rd != null)
                    {
                        double deducted = Math.Min(excess, alloc.amount);
                        batchEntries[i] = (rd, excess - deducted);
                        totalExcess -= deducted;
                    }
                }

                // Remove exhausted entries
                batchEntries.RemoveAll(e => e.excess <= 0);
            }
        }
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
                    req.status = Data.LogisticsRequestStatus.Pending;
            }
        }
    }
}
