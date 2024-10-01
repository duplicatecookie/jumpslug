using System.Diagnostics;

using BepInEx;
using BepInEx.Logging;

namespace JumpSlug;

[BepInPlugin("doppelkeks.jumpslug", "JumpSlug", "0.1.0")]
class Plugin : BaseUnityPlugin {
    public static new ManualLogSource? Logger;
    public Plugin() {
        Logger = base.Logger;
    }

    public void OnEnable() {
        On.RainWorld.OnModsInit += Extras.WrapInit(LoadResources);

        if (Stopwatch.IsHighResolution) {
            Logger!.LogInfo("Performance timing enabled");
            Logger!.LogInfo($"Timer Frequency: {Stopwatch.Frequency} Hz");
            Timers.Active = true;
        } else {
            Logger!.LogError("No high resolution timer available, disabling performance timing");
        }

        TemplateType.RegisterValues();
        TemplateHooks.RegisterHooks();

        Pathfinding.VisualizerHooks.RegisterHooks();
        Pathfinding.RoomHooks.RegisterHooks();
        
        AIHooks.RegisterHooks();
        TimerHooks.RegisterHooks();
        DebugHooks.RegisterHooks();
    }

    public void OnDisable() {
        On.RainWorld.OnModsInit -= Extras.WrapInit(LoadResources);

        TemplateType.UnregisterValues();
        TemplateHooks.UnregisterHooks();

        Pathfinding.VisualizerHooks.UnregisterHooks();
        Pathfinding.RoomHooks.UnregisterHooks();

        AIHooks.UnregisterHooks();
        TimerHooks.UnregisterHooks();
        DebugHooks.UnregisterHooks();
    }

    // Load any resources, such as sprites or sounds
    private void LoadResources(RainWorld rainWorld) {
    }
}