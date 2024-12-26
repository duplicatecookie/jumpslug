using System;
using System.Diagnostics;

using UnityEngine;

namespace JumpSlug;

class FunctionTimer {
    public string Name;
    public Stopwatch Watch;
    public int Invocations;
    public FunctionTimer(string name) {
        Name = name;
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
        double totalSeconds = (double)Watch.ElapsedTicks / (double)Stopwatch.Frequency;
        Plugin.Logger!.LogInfo(
            String.Format(
                "name: {0}, invocations: {1}, ticks: {2} total time: {3} ms, average time: {4} μs",
                Name,
                Invocations,
                Watch.ElapsedTicks,
                Math.Round(totalSeconds * 1000.0),
                Math.Round(totalSeconds * 1000_000.0 / (double)Invocations)
            )
        );
    }
}

static class Timers {
    public static bool Active = false;
    public static FunctionTimer FindPathTo_MainLoop_Individual = new FunctionTimer("Pathfinder.FindPathTo_MainLoop_Individual");
    public static FunctionTimer FindPathFrom_MainLoop_Individual = new FunctionTimer("Pathfinder.FindPathFrom_MainLoop_Individual");
    public static FunctionTimer FindPathTo_MainLoop_Total = new FunctionTimer("Pathfinder.FindPathTo_MainLoop_Total");
    public static FunctionTimer FindPathFrom_MainLoop_Total = new FunctionTimer("Pathfinder.FindPathFrom_MainLoop_Total");
    public static FunctionTimer FindPathTo_Setup = new FunctionTimer("Pathfinder.FindPathTo_Setup");
    public static FunctionTimer FindPathFrom_Setup = new FunctionTimer("Pathfinder.FindPathFrom_Setup");
    public static FunctionTimer TraceFromNode = new FunctionTimer("DynamicGraph.TraceFromNode");
    public static FunctionTimer JumpSlugAI_Update = new FunctionTimer("JumpSlugAI.Update");
}

static class TimerHooks {
    public static void RegisterHooks() {
        On.Player.Update += Player_Update;
    }

    public static void UnregisterHooks() {
        On.Player.Update -= Player_Update;
    }

    private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu) {
        if (InputHelper.JustPressed(KeyCode.R) && Timers.Active) {
            Timers.FindPathTo_MainLoop_Individual.Report();
            Timers.FindPathFrom_MainLoop_Individual.Report();
            Timers.FindPathTo_MainLoop_Total.Report();
            Timers.FindPathFrom_MainLoop_Total.Report();
            Timers.FindPathTo_Setup.Report();
            Timers.FindPathFrom_Setup.Report();
            Timers.TraceFromNode.Report();
            Timers.JumpSlugAI_Update.Report();
        }
        orig(self, eu);
    }
}