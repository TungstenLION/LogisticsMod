using System.Collections.Generic;
using System.Linq;
using CustomUpdate;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using LogisticsMod.Logic;
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

    public static LogisticsRequest AddRequest(ObjectInfo oi, ResourceDefinition rd, double amount)
    {
        var data = GetOrCreate(oi);
        var req = new LogisticsRequest
        {
            resourceDef = rd,
            ResourceDefinition = rd,
            requestedAmount = amount,
            status = LogisticsRequestStatus.Pending
        };
        data.requests.Add(req);
        LogisticsObserver.Log($"Added request: {rd.ID} x{amount} on {oi.ObjectName}");
        return req;
    }

    public static LogisticsProvider AddProvider(ObjectInfo oi, ResourceDefinition rd, double minimumKeep)
    {
        var data = GetOrCreate(oi);
        var prov = new LogisticsProvider
        {
            resourceDef = rd,
            ResourceDefinition = rd,
            minimumKeep = minimumKeep,
            isActive = true
        };
        data.providers.Add(prov);
        LogisticsObserver.Log($"Added provider: {rd.ID} min={minimumKeep} on {oi.ObjectName}");
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

    public static List<ShipQuotaEntry> GetQuotas(ObjectInfo oi, bool isSpacecraft)
    {
        var data = GetOrCreate(oi);
        return isSpacecraft ? data.spacecraftQuota : data.launchVehicleQuota;
    }

    public static int GetQuota(ObjectInfo oi, string typeName, bool isSpacecraft)
    {
        var quotas = GetQuotas(oi, isSpacecraft);
        var entry = quotas.Find(q => q.typeName == typeName);
        return entry?.count ?? 0;
    }

    public static void SetQuota(ObjectInfo oi, string typeName, int count, bool isSpacecraft)
    {
        var quotas = GetQuotas(oi, isSpacecraft);
        var entry = quotas.Find(q => q.typeName == typeName);
        if (entry != null)
            entry.count = count;
        else if (count > 0)
            quotas.Add(new ShipQuotaEntry { typeName = typeName, count = count });
    }

    public static void RemoveQuota(ObjectInfo oi, string typeName, bool isSpacecraft)
    {
        var quotas = GetQuotas(oi, isSpacecraft);
        quotas.RemoveAll(q => q.typeName == typeName);
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
            if (oid.CheckResources(rd) > 0)
                result.Add(rd);
        }
        return result;
    }

    public static Dictionary<string, int> GetShipTypeCountsOnObject(ObjectInfo oi, bool isSpacecraft)
    {
        var result = new Dictionary<string, int>();
        if (oi == null) return result;

        if (isSpacecraft)
        {
            foreach (var sc in Object.FindObjectsOfType<Spacecraft>())
            {
                if (sc == null || sc.spacecraftType == null) continue;
                if (sc.spacecraftType.MagneticCatapult) continue;
                if (sc.CurrentlyOnThisObject != oi) continue;
                var tn = sc.spacecraftType.NameRocketType ?? "SC";
                if (!result.ContainsKey(tn)) result[tn] = 0;
                result[tn]++;
            }
        }
        else
        {
            foreach (var lv in Object.FindObjectsOfType<LaunchVehicle>())
            {
                if (lv == null || lv.launchVehicleType == null) continue;
                if (lv.objectInfo != oi) continue;
                if (!lv.IsReadyToLaunchReusable()) continue;
                var tn = lv.launchVehicleType.Name ?? "LV";
                if (!result.ContainsKey(tn)) result[tn] = 0;
                result[tn]++;
            }
        }
        return result;
    }



    public static HashSet<ResourceDefinition> GetNetworkResourcesSet(Company player)
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
                var rd = prov.ResourceDefinition;
                if (rd == null) continue;

                if (oid.CheckResources(rd) > prov.minimumKeep)
                    result.Add(rd);
            }
        }
        return result;
    }
}
