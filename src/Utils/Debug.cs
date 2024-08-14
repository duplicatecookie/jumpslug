using UnityEngine;

namespace JumpSlug;

static class DebugHooks {
    public static void RegisterHooks() {
        On.RainWorld.Update += RainWorld_Update;
    }

    public static void UnregisterHooks() {
        On.RainWorld.Update -= RainWorld_Update;
    }

    private static bool s_frameStep = false;

    private static void RainWorld_Update(On.RainWorld.orig_Update orig, RainWorld self) {
        if (InputHelper.JustPressed(KeyCode.U)) {
            Plugin.Logger!.LogDebug("toggled frame stepping");
            s_frameStep = !s_frameStep;
        }
        if (!s_frameStep || InputHelper.JustPressed(KeyCode.I)) {
            orig(self);
        }
    }
}