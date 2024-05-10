using System;
using System.Diagnostics;

using UnityEngine;

namespace JumpSlug;

class FunctionTimer {
    public string name;
    public Stopwatch watch;
    public int invocations;
    public FunctionTimer(string name) {
        this.name = name;
        watch = new Stopwatch();
        invocations = 0;
    }

    public void Start() {
        watch.Start();
        invocations += 1;
    }
    public void Stop() {
        watch.Stop();
    }

    public void Report() {
        double totalSeconds = (watch.ElapsedTicks / Stopwatch.Frequency);
        Plugin.Logger!.LogInfo(
            String.Format(
                "name: {0}, invocations: {1}, total time: {2} ms, average time: {3} μs",
                name,
                invocations,
                totalSeconds * 1000,
                (totalSeconds * 1000_000) / invocations
            )
        );
    }
}

static class Timers {
    public static bool active = false;
    public static FunctionTimer findPath = new FunctionTimer("Pathfinder.FindPath");
    public static FunctionTimer followPath = new FunctionTimer("JumpSlugAI.FollowPath");
}

static class TimerHooks {
    private static bool justPressedR = false;
    public static void RegisterHooks() {
        On.Player.Update += Player_Update;
    }

    private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu) {
        switch ((Input.GetKey(KeyCode.R), justPressedR)) {
            case (true, false):
                justPressedR = true;
                if (Timers.active) {
                    Timers.findPath.Report();
                    Timers.followPath.Report();
                }
                break;
            case (false, true):
                justPressedR = false;
                break;
            default:
                break;
        }
        orig(self, eu);
    }
}