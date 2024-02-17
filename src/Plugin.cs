using UnityEngine;
using BepInEx;
using System.Collections.Generic;
using BepInEx.Logging;

namespace AIMod;

[BepInPlugin("doppelkeks.aimod", "AI Mod", "0.1.0")]
partial class Plugin : BaseUnityPlugin
{
    public static new ManualLogSource Logger;
    public Plugin()
    {
        Logger = base.Logger;
    }
    // Add hooks
    public void OnEnable()
    {
        On.RainWorld.OnModsInit += Extras.WrapInit(LoadResources);

        AITestCreatureTemplateType.RegisterValues();

        On.StaticWorld.InitCustomTemplates += InitCustomTemplates;

        VisualizerHooks.RegisterHooks();
        //JumpTracerHooks.RegisterHooks();
        PathfinderHooks.RegisterHooks();
    }

    public void OnDisable()
    {
        AITestCreatureTemplateType.UnregisterValues();
    }

    // Load any resources, such as sprites or sounds
    private void LoadResources(RainWorld rainWorld)
    {
    }
}
