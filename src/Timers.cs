using System;
using System.Diagnostics;

using UnityEngine;

namespace JumpSlug;

class FunctionTimer {
    public string Name;
    public Stopwatch Watch;
    public int Invocations;
    public FunctionTimer(string name) {
        this.Name = name;
        Watch = new Stopwatch();
        Invocations = 0;
    }

    public void Start() {
        Watch.Start();
        Invocations += 1;
    }
    public void Stop() {
        Watch.Stop();
    }

    public void Report() {
        double totalSeconds = ((double)Watch.ElapsedTicks / (double)Stopwatch.Frequency);
        Plugin.Logger!.LogInfo(
            String.Format(
                "name: {0}, invocations: {1}, ticks: {2} total time: {3} ms, average time: {4} μs",
                Name,
                Invocations,
                Watch.ElapsedTicks,
                Math.Round(totalSeconds * 1000.0),
                Math.Round((totalSeconds * 1000_000.0) / (double)Invocations)
            )
        );
    }
}

static class Timers {
    public static bool Active = false;
    public static FunctionTimer FindPath = new FunctionTimer("Pathfinder.FindPath");
    public static FunctionTimer FollowPath = new FunctionTimer("JumpSlugAI.FollowPath");
    public static FunctionTimer TraceFromNode = new FunctionTimer("DynamicGraph.TraceFromNode");
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
                if (Timers.Active) {
                    Timers.FindPath.Report();
                    Timers.FollowPath.Report();
                    Timers.TraceFromNode.Report();
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