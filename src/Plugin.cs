﻿using BepInEx;
using BepInEx.Logging;

namespace JumpSlug;

[BepInPlugin("doppelkeks.jumpslug", "JumpSlug", "0.1.0")]
class Plugin : BaseUnityPlugin
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

        //TemplateType.RegisterValues();
        //TemplateHooks.RegisterHooks();

        PathfinderHooks.RegisterHooks();
        AIHooks.RegisterHooks();
    }

    public void OnDisable()
    {
        On.RainWorld.OnModsInit -= Extras.WrapInit(LoadResources);

        //TemplateType.UnregisterValues();
        //TemplateHooks.UnregisterHooks();
        
        PathfinderHooks.UnregisterHooks();
        AIHooks.UnregisterHooks();
    }

    // Load any resources, such as sprites or sounds
    private void LoadResources(RainWorld rainWorld)
    {
    }
}