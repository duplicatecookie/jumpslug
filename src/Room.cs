using System.Runtime.CompilerServices;

using UnityEngine;

namespace JumpSlug;

static class RoomCWT {
    public class RoomExtension {
        public Pathfinding.SharedGraph? SharedGraph;
        public Pathfinding.SharedGraphVisualizer? Visualizer;
        public Pathfinding.PathNodePool? PathNodePool;
        public Pathfinding.BitGrid? OpenNodes;
        public Pathfinding.BitGrid? ClosedNodes;
        public RoomExtension() {
        }
    }
    public static ConditionalWeakTable<Room, RoomExtension> CWT = new();
    public static RoomExtension GetCWT(this Room room) => CWT.GetValue(room, _ => new());
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
            roomExt.SharedGraph = new Pathfinding.SharedGraph(self.room);
            roomExt.Visualizer = new Pathfinding.SharedGraphVisualizer(self.room);
            int width = roomExt.SharedGraph.Width;
            int height = roomExt.SharedGraph.Height;
            roomExt.OpenNodes = new Pathfinding.BitGrid(width, height);
            roomExt.ClosedNodes = new Pathfinding.BitGrid(width, height);
            roomExt.PathNodePool = new Pathfinding.PathNodePool(roomExt.SharedGraph);
        }
    }

    private static void Room_Update(On.Room.orig_Update orig, Room self) {
        orig(self);
        if (InputHelper.JustPressed(KeyCode.N)) {
            self.GetCWT().Visualizer!.ToggleNodes();
        }
        if (InputHelper.JustPressed(KeyCode.C)) {
            self.GetCWT().Visualizer!.ToggleConnections();
        }
    }
}