using System;
using System.Collections.Generic;
using Game.Info;
using ScriptableObjectScripts;
using UnityEngine;

namespace LogisticsMod.Data;

[Serializable]
public class LogisticsRequest
{
    public ResourceDefinitionIDSave resourceDef;
    public double requestedAmount;
    public LogisticsRequestStatus status;
    public string networkId = "";

    [NonSerialized]
    public ResourceDefinition ResourceDefinition;

    [NonSerialized]
    public string statusNote;
}

public enum LogisticsRequestStatus
{
    Pending,
    InProgress,
    Satisfied,
    Failed
}

[Serializable]
public class LogisticsProvider
{
    public ResourceDefinitionIDSave resourceDef;
    public double minimumKeep;
    public bool isActive;
    public bool toOrbit;
    public string networkId = "";

    [NonSerialized]
    public ResourceDefinition ResourceDefinition;
}

[Serializable]
public class ShipQuotaEntry
{
    public string typeName;
    public int count;
    public string networkId = "";
}

[Serializable]
public class LogisticsObjectData
{
    public string objectInfoSaveId;
    public List<LogisticsRequest> requests = new List<LogisticsRequest>();
    public List<LogisticsProvider> providers = new List<LogisticsProvider>();
    public List<ShipQuotaEntry> spacecraftQuota = new List<ShipQuotaEntry>();
    public List<ShipQuotaEntry> launchVehicleQuota = new List<ShipQuotaEntry>();

    [NonSerialized]
    public bool IsFrozen;

    [NonSerialized]
    public object ObjectInfo;
}
