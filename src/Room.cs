using System.Runtime.CompilerServices;

using UnityEngine;

namespace JumpSlug;

static class RoomCWT {
    public class RoomExtension {
        public Pathfinding.SharedGraph? sharedGraph;
        public Pathfinding.SharedGraphVisualizer? visualizer;
        public RoomExtension() {
        }
    }
    public static ConditionalWeakTable<Room, RoomExtension> cwt = new();
    public static RoomExtension GetCWT(this Room room) => cwt.GetValue(room, _ => new());
}

static class RoomHooks {
    public static void RegisterHooks() {
        On.RoomPreparer.Update += RoomPreparer_Update;
        On.Room.Update += Room_Update;
    }

    public static void UnregisterHooks() {
        On.RoomPreparer.Update += RoomPreparer_Update;
        On.Room.Update -= Room_Update;
    }

    private static void RoomPreparer_Update(On.RoomPreparer.orig_Update orig, RoomPreparer self) {
        orig(self);
        if (self.threadFinished) {
            var roomExt = self.room.GetCWT();
            roomExt.sharedGraph = new Pathfinding.SharedGraph(self.room);
            roomExt.visualizer = new Pathfinding.SharedGraphVisualizer(self.room);
        }
    }

    private static void Room_Update(On.Room.orig_Update orig, Room self) {
        orig(self);
        if (InputHelper.JustPressed(KeyCode.N)) {
            self.GetCWT().visualizer!.ToggleNodes();
        }
        if (InputHelper.JustPressed(KeyCode.C)) {
            self.GetCWT().visualizer!.ToggleConnections();
        }
    }
}