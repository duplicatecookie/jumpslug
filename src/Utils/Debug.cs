using UnityEngine;

namespace JumpSlug;

static class DebugHooks {
    public static void RegisterHooks() {
        On.RainWorld.Update += RainWorld_Update;
    }

    public static void UnregisterHooks() {
        On.RainWorld.Update -= RainWorld_Update;
    }

    private static bool _FrameStep = false;

    private static void RainWorld_Update(On.RainWorld.orig_Update orig, RainWorld self) {
        if (InputHelper.JustPressed(KeyCode.U)) {
            Plugin.Logger!.LogDebug("toggled frame stepping");
            _FrameStep = !_FrameStep;
        }
        if (!_FrameStep || InputHelper.JustPressed(KeyCode.I)) {
            orig(self);
        }
    }
}