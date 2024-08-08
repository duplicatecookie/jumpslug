using System.Runtime.CompilerServices;

using UnityEngine;

namespace JumpSlug.Pathfinding;

static class RoomCWT {
    public class RoomExtension {
        public SharedGraph? SharedGraph;
        public SharedGraphVisualizer? Visualizer;
        public PathNodePool? PathNodePool;
        public BitGrid? OpenNodes;
        public BitGrid? ClosedNodes;
        public PathNodeQueue? NodeQueue;
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
            roomExt.SharedGraph = new SharedGraph(self.room);
            roomExt.Visualizer = new SharedGraphVisualizer(self.room);
            int width = roomExt.SharedGraph.Width;
            int height = roomExt.SharedGraph.Height;
            roomExt.OpenNodes = new BitGrid(width, height);
            roomExt.ClosedNodes = new BitGrid(width, height);
            roomExt.PathNodePool = new PathNodePool(roomExt.SharedGraph);
            roomExt.NodeQueue = new PathNodeQueue(
                roomExt.PathNodePool!.Value.NonNullCount,
                width,
                height
            );
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