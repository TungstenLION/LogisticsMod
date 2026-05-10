using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Info;
using LogisticsMod.Logic;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;
using Newtonsoft.Json;

namespace LogisticsMod.Data;

public static class LogisticsPersistence
{
    private static string GetPath(string saveName)
    {
        var dir = Path.Combine(Application.dataPath, "..", "BepInEx", "saves", saveName);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "LogisticsData.json");
    }

    [Serializable]
    private class SaveData
    {
        public List<SavedObject> objects = new List<SavedObject>();
    }

    [Serializable]
    private class SavedObject
    {
        public int objectId;
        public List<SavedRequest> requests = new List<SavedRequest>();
        public List<SavedProvider> providers = new List<SavedProvider>();
        public List<SavedQuota> spacecraftQuota = new List<SavedQuota>();
        public List<SavedQuota> launchVehicleQuota = new List<SavedQuota>();
    }

    [Serializable]
    private class SavedRequest
    {
        public string resourceId;
        public double amount;
        public int status;
    }

    [Serializable]
    private class SavedProvider
    {
        public string resourceId;
        public double minKeep;
        public bool active;
    }

    [Serializable]
    private class SavedQuota
    {
        public string typeName;
        public int count;
    }

    public static void Save(string saveName)
    {
        try
        {
            var objManager = MonoBehaviourSingleton<ObjectInfoManager>.Instance;
            var allObjectInfos = objManager?.allObjectInfos;
            if (allObjectInfos != null)
            {
                var deadKeys = LogisticsNetwork.GetAllObjects()
                    .Where(oi => oi == null || !allObjectInfos.Contains(oi))
                    .ToList();
                foreach (var deadOi in deadKeys)
                {
                    LogisticsObserver.Log($"Save: removing stale data for object id={deadOi?.id ?? -1}");
                    LogisticsNetwork.RemoveObject(deadOi);
                }
            }

            var data = new SaveData();

            foreach (var oi in LogisticsNetwork.GetAllObjects())
            {
                var ld = LogisticsNetwork.Get(oi);
                if (ld == null) continue;

                var so = new SavedObject { objectId = oi.id };

                foreach (var r in ld.requests)
                {
                    so.requests.Add(new SavedRequest
                    {
                        resourceId = r.ResourceDefinition?.ID ?? r.resourceDef.id,
                        amount = r.requestedAmount,
                        status = (int)r.status
                    });
                }

                foreach (var p in ld.providers)
                {
                    so.providers.Add(new SavedProvider
                    {
                        resourceId = p.ResourceDefinition?.ID ?? p.resourceDef.id,
                        minKeep = p.minimumKeep,
                        active = p.isActive
                    });
                }

                foreach (var q in ld.spacecraftQuota)
                    so.spacecraftQuota.Add(new SavedQuota { typeName = q.typeName, count = q.count });

                foreach (var q in ld.launchVehicleQuota)
                    so.launchVehicleQuota.Add(new SavedQuota { typeName = q.typeName, count = q.count });

                data.objects.Add(so);
            }

            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(GetPath(saveName), json);
            LogisticsObserver.Log($"Saved to {saveName}");
        }
        catch (Exception ex)
        {
            LogisticsObserver.LogError($"Save error: {ex}");
        }
    }

    public static void Load(string saveName)
    {
        try
        {
            var path = GetPath(saveName);
            if (!File.Exists(path))
            {
                LogisticsObserver.Log($"No save for {saveName}");
                return;
            }

            var json = File.ReadAllText(path);
            var data = JsonConvert.DeserializeObject<SaveData>(json);
            if (data == null || data.objects == null) return;

            LogisticsNetwork.ClearAll();

            var allResources = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllResourceDefinitions;
            var objManager = MonoBehaviourSingleton<ObjectInfoManager>.Instance;

            foreach (var so in data.objects)
            {
                var oi = objManager?.GetByID(so.objectId);
                if (oi == null)
                {
                    LogisticsObserver.LogWarning($"Load: object id={so.objectId} not found, skipping");
                    continue;
                }

                var ld = LogisticsNetwork.GetOrCreate(oi);

                foreach (var sr in so.requests)
                {
                    var rd = allResources?.GetByID(sr.resourceId);
                    ld.requests.Add(new LogisticsRequest
                    {
                        resourceDef = (ResourceDefinitionIDSave)rd,
                        ResourceDefinition = rd,
                        requestedAmount = sr.amount,
                        status = (LogisticsRequestStatus)sr.status
                    });
                }

                foreach (var sp in so.providers)
                {
                    var rd = allResources?.GetByID(sp.resourceId);
                    ld.providers.Add(new LogisticsProvider
                    {
                        resourceDef = (ResourceDefinitionIDSave)rd,
                        ResourceDefinition = rd,
                        minimumKeep = sp.minKeep,
                        isActive = sp.active
                    });
                }

                foreach (var sq in so.spacecraftQuota)
                    ld.spacecraftQuota.Add(new ShipQuotaEntry { typeName = sq.typeName, count = sq.count });

                foreach (var sq in so.launchVehicleQuota)
                    ld.launchVehicleQuota.Add(new ShipQuotaEntry { typeName = sq.typeName, count = sq.count });
            }

            LogisticsObserver.Log($"Loaded from {saveName}");
        }
        catch (Exception ex)
        {
            LogisticsObserver.LogError($"Load error: {ex}");
        }
    }
}
