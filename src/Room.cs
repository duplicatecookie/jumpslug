using System.Collections.Generic;
using System.Runtime.CompilerServices;

using UnityEngine;

namespace JumpSlug.Pathfinding;

static class RoomCWT {
    public class RoomExtension {
        public SharedGraph? SharedGraph;
        public Dictionary<SlugcatDescriptor, DynamicGraph> DynamicGraphs = new();
        public SharedGraphVisualizer? Visualizer;
        public RoomExtension() {
        }
    }
    public static ConditionalWeakTable<Room, RoomExtension> CWT = new();
    public static RoomExtension GetCWT(this Room room) => CWT.GetValue(room, _ => new());
}

static class RoomHooks {
    public static void RegisterHooks() {
        On.RoomPreparer.Update += RoomPreparer_Update;
        On.RoomCamera.Update += RoomCamera_Update;
    }

    public static void UnregisterHooks() {
        On.RoomPreparer.Update += RoomPreparer_Update;
        On.RoomCamera.Update -= RoomCamera_Update;
    }

    private static void RoomPreparer_Update(On.RoomPreparer.orig_Update orig, RoomPreparer self) {
        orig(self);
        if (self.threadFinished) {
            var roomExt = self.room.GetCWT();
            roomExt.SharedGraph = new SharedGraph(self.room);
            roomExt.Visualizer = new SharedGraphVisualizer(self.room);
        }
    }

    private static void RoomCamera_Update(On.RoomCamera.orig_Update orig, RoomCamera self) {
        orig(self);
        if (InputHelper.JustPressed(KeyCode.N)) {
            self.room.GetCWT().Visualizer!.ToggleNodes();
        }
        if (InputHelper.JustPressed(KeyCode.C)) {
            self.room.GetCWT().Visualizer!.ToggleConnections();
        }
    }
}