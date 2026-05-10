using BepInEx;
using HarmonyLib;
using LogisticsMod.Logic;

namespace LogisticsMod;

[BepInPlugin("com.logisticsmod", "Logistics Tab", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    public static Plugin Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, "com.logisticsmod");
        LogisticsObserver.Log("Plugin loaded!");
    }
}
