using System.Collections.Generic;
using System.Linq;
using CustomUpdate;
using Data.ScriptableObject;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.ObjectInfoDataScripts.CustomFacilitiesAndModules;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;

namespace LogisticsMod.Data;

public static class LogisticsNetwork
{
    private static Dictionary<int, LogisticsObjectData> _dataByObject
        = new Dictionary<int, LogisticsObjectData>();

    public static LogisticsObjectData GetOrCreate(ObjectInfo oi)
    {
        if (oi == null) return null;
        if (!_dataByObject.TryGetValue(oi.id, out var data))
        {
            data = new LogisticsObjectData { ObjectInfo = oi, objectInfoSaveId = oi.id.ToString() };
            _dataByObject[oi.id] = data;
        }
        else
        {
            if (data.ObjectInfo == null)
                data.ObjectInfo = oi;
        }
        return data;
    }

    public static LogisticsObjectData Get(ObjectInfo oi)
    {
        if (oi == null) return null;
        _dataByObject.TryGetValue(oi.id, out var data);
        return data;
    }

    public static LogisticsRequest AddRequest(ObjectInfo oi, ResourceDefinition rd, double amount, string networkId = "")
    {
        var data = GetOrCreate(oi);
        var req = new LogisticsRequest
        {
            resourceDef = rd,
            ResourceDefinition = rd,
            requestedAmount = amount,
            status = LogisticsRequestStatus.Pending,
            networkId = networkId
        };
        data.requests.Add(req);
        return req;
    }

    public static LogisticsProvider AddProvider(ObjectInfo oi, ResourceDefinition rd, double minimumKeep, string networkId = "")
    {
        var data = GetOrCreate(oi);
        var prov = new LogisticsProvider
        {
            resourceDef = rd,
            ResourceDefinition = rd,
            minimumKeep = minimumKeep,
            isActive = true,
            networkId = networkId
        };
        data.providers.Add(prov);
        return prov;
    }

    public static void RemoveRequest(ObjectInfo oi, int index)
    {
        var data = Get(oi);
        if (data != null && index >= 0 && index < data.requests.Count)
            data.requests.RemoveAt(index);
    }

    public static void RemoveProvider(ObjectInfo oi, int index)
    {
        var data = Get(oi);
        if (data != null && index >= 0 && index < data.providers.Count)
            data.providers.RemoveAt(index);
    }

    public static List<ShipQuotaEntry> GetQuotas(ObjectInfo oi, bool isSpacecraft, string networkId = "")
    {
        var data = GetOrCreate(oi);
        var all = isSpacecraft ? data.spacecraftQuota : data.launchVehicleQuota;
        return all.Where(q => q.networkId == networkId).ToList();
    }

    public static int GetQuota(ObjectInfo oi, string typeName, bool isSpacecraft, string networkId = "")
    {
        var quotas = GetQuotas(oi, isSpacecraft, networkId);
        var entry = quotas.Find(q => q.typeName == typeName);
        return entry?.count ?? 0;
    }

    public static void SetQuota(ObjectInfo oi, string typeName, int count, bool isSpacecraft, string networkId = "")
    {
        var data = GetOrCreate(oi);
        var all = isSpacecraft ? data.spacecraftQuota : data.launchVehicleQuota;
        var entry = all.Find(q => q.typeName == typeName && q.networkId == networkId);
        if (entry != null)
            entry.count = count;
        else if (count > 0)
            all.Add(new ShipQuotaEntry { typeName = typeName, count = count, networkId = networkId });
    }

    public static void RemoveQuota(ObjectInfo oi, string typeName, bool isSpacecraft, string networkId = "")
    {
        var data = GetOrCreate(oi);
        var all = isSpacecraft ? data.spacecraftQuota : data.launchVehicleQuota;
        all.RemoveAll(q => q.typeName == typeName && q.networkId == networkId);
    }

    public static void RemoveAllForNetwork(string networkId)
    {
        if (string.IsNullOrEmpty(networkId)) return;
        foreach (var kv in _dataByObject)
        {
            kv.Value.requests.RemoveAll(r => r.networkId == networkId);
            kv.Value.providers.RemoveAll(p => p.networkId == networkId);
            kv.Value.spacecraftQuota.RemoveAll(q => q.networkId == networkId);
            kv.Value.launchVehicleQuota.RemoveAll(q => q.networkId == networkId);
        }
    }

    public static void ClearAll()
    {
        _dataByObject.Clear();
    }

    public static void RemoveObject(ObjectInfo oi)
    {
        if (oi != null)
            _dataByObject.Remove(oi.id);
    }

    public static List<ObjectInfo> GetAllObjects()
    {
        var objManager = MonoBehaviourSingleton<ObjectInfoManager>.Instance;
        var result = new List<ObjectInfo>();
        foreach (var kv in _dataByObject)
        {
            var oi = kv.Value.ObjectInfo as ObjectInfo;
            if (oi == null && objManager != null)
            {
                oi = objManager.GetByID(kv.Key);
                if (oi != null)
                    kv.Value.ObjectInfo = oi;
            }
            if (oi != null)
                result.Add(oi);
        }
        return result;
    }

    public static HashSet<ResourceDefinition> GetAvailableResourcesOnObject(ObjectInfo oi, Company player)
    {
        var result = new HashSet<ResourceDefinition>();
        if (oi == null || player == null) return result;

        var oid = oi.GetObjectInfoData(player);
        if (oid == null) return result;

        var am = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance;
        if (am?.AllResourceDefinitions == null) return result;

        foreach (var rd in am.AllResourceDefinitions.ListNotEmpty)
        {
            if (rd.ResourceType == ScriptableObjectScripts.ResourceDefinition.EResourceType.Human) continue;
            if (oid.CheckResources(rd) > 0)
                result.Add(rd);
        }
        return result;
    }

    public static Dictionary<string, int> GetShipTypeCountsOnObject(ObjectInfo oi, bool isSpacecraft, Company player)
    {
        var result = new Dictionary<string, int>();
        if (oi == null || player == null) return result;

        if (isSpacecraft)
        {
            foreach (var sc in Object.FindObjectsOfType<Spacecraft>())
            {
                if (sc == null || sc.spacecraftType == null) continue;
                if (sc.GetCompany() != player) continue;
                if (sc.spacecraftType.MagneticCatapult) continue;
                if (sc.CurrentlyOnThisObject != oi) continue;
                var tn = TypeKey(sc.spacecraftType.ID, sc.spacecraftType.NameRocketType ?? "SC");
                if (!result.ContainsKey(tn)) result[tn] = 0;
                result[tn]++;
            }
        }
        else
        {
            foreach (var lv in Object.FindObjectsOfType<LaunchVehicle>())
            {
                if (lv == null || lv.launchVehicleType == null) continue;
                if (lv.GetCompany() != player) continue;
                if (lv.objectInfo != oi) continue;
                if (!lv.IsReadyToLaunchReusable()) continue;
                if (!lv.launchVehicleType.FakeForFacility) continue;
                if (!IsMagneticCatapultLV(oi, lv, player)) continue;
                var tn = TypeKey(lv.launchVehicleType.ID, lv.launchVehicleType.Name ?? "LV");
                if (!result.ContainsKey(tn)) result[tn] = 0;
                result[tn]++;
            }
        }
        return result;
    }

    public static bool IsObjectFrozen(ObjectInfo oi)
    {
        var data = Get(oi);
        return data != null && data.IsFrozen;
    }

    public static List<string> GetAllNetworkIds()
    {
        var ids = new HashSet<string> { "" };
        foreach (var kv in _dataByObject)
        {
            foreach (var r in kv.Value.requests)
                if (!string.IsNullOrEmpty(r.networkId)) ids.Add(r.networkId);
            foreach (var p in kv.Value.providers)
                if (!string.IsNullOrEmpty(p.networkId)) ids.Add(p.networkId);
            foreach (var q in kv.Value.spacecraftQuota)
                if (!string.IsNullOrEmpty(q.networkId)) ids.Add(q.networkId);
            foreach (var q in kv.Value.launchVehicleQuota)
                if (!string.IsNullOrEmpty(q.networkId)) ids.Add(q.networkId);
        }
        return ids.OrderBy(id => id).ToList();
    }

    public static HashSet<ResourceDefinition> GetNetworkResourcesSet(Company player, string networkId = "")
    {
        var result = new HashSet<ResourceDefinition>();
        if (player == null) return result;

        foreach (var oi in GetAllObjects())
        {
            var data = Get(oi);
            if (data == null) continue;

            var oid = oi.GetObjectInfoData(player);
            if (oid == null) continue;

            foreach (var prov in data.providers)
            {
                if (!prov.isActive) continue;
                if (prov.networkId != networkId) continue;
                var rd = prov.ResourceDefinition;
                if (rd == null) continue;

                if (rd.ResourceType == ScriptableObjectScripts.ResourceDefinition.EResourceType.Human) continue;
                if (oid.CheckResources(rd) > prov.minimumKeep)
                    result.Add(rd);
            }
        }
        return result;
    }

    public static bool ObjectRequiresLVForLaunch(ObjectInfo oi)
    {
        return oi?.NeedVehicleToLaunch() ?? false;
    }

    public static string TypeKey(string id, string fallbackName)
    {
        return !string.IsNullOrEmpty(id) ? id : fallbackName;
    }

    public static bool QuotaMatches(ShipQuotaEntry quota, string id, string fallbackName)
    {
        if (quota == null) return false;
        var key = TypeKey(id, fallbackName);
        return quota.typeName == key || quota.typeName == fallbackName;
    }

    public static int ActiveCountFor(Dictionary<string, int> active, string id, string fallbackName)
    {
        var result = 0;
        if (active == null) return 0;
        active.TryGetValue(TypeKey(id, fallbackName), out result);
        if (!string.IsNullOrEmpty(fallbackName) && active.TryGetValue(fallbackName, out var legacy))
            result += legacy;
        return result;
    }

    private static bool IsMagneticCatapultLV(ObjectInfo oi, LaunchVehicle lv, Company player)
    {
        var oid = oi?.GetObjectInfoData(player);
        if (oid == null) return false;
        var facility = oid.GetFakeLVFromFacilityReverse(lv);
        if (facility == null) return false;
        if (facility is not FacilityBonus fb) return false;
        if (fb.BonusData == null) return false;
        var fakeSCId = fb.BonusData.fakeSCId;
        if (string.IsNullOrEmpty(fakeSCId)) return false;
        var scType = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllSpacecraftType?.GetByID(fakeSCId);
        return scType != null && scType.MagneticCatapult;
    }
}
