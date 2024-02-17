
using System;
using System.Collections.Generic;
using RWCustom;
using UnityEngine;

namespace AIMod;

class Pathfinder
{
    public class Node
    {
        public NodeType type;
        public bool hasBeam;
        public bool hasPlatform;
        public List<NodeConnection> connections;
        public IntVector2 gridPos;

        public Node(NodeType type, int x, int y)
        {
            this.type = type;
            gridPos = new IntVector2(x, y);
            connections = new();
        }
    }
    // stupid hack because C# doesn't have tagged unions
    public record NodeType
    {
        public record Air() : NodeType();
        public record Floor() : NodeType();
        public record Slope() : NodeType();
        public record Corridor() : NodeType();
        public record ShortcutEntrance(int Index) : NodeType();

        private NodeType() { }
    }

    public class NodeConnection
    {
        public ConnectionType type;
        public Node next;
        public float weight;

        public NodeConnection(ConnectionType type, Node next, float weight = 1)
        {
            this.next = next;
            this.weight = weight;
            this.type = type;
        }
    }

    public enum ConnectionType
    {
        Standard,
        Jump,
        Pounce,
        Shortcut,
        Drop,
    }
    public Creature creature;
    public WorldCoordinate destination;
    public List<NodeConnection> path;
    public JumpTracer jumpTracer;
    public Node[,] graph;
    private bool justPressed;
    public bool visualize;
    private List<DebugSprite> visualizationSprites;
    public Pathfinder(Creature creature)
    {
        this.creature = creature;
        visualizationSprites = new();
    }

    public void Update()
    {
        switch ((Input.GetKey(KeyCode.V), justPressed))
        {
            case (true, false):
                justPressed = true;
                if (visualize)
                {
                    foreach (var sprite in visualizationSprites)
                    {
                        sprite.Destroy();
                    }
                    visualizationSprites.Clear();
                    visualize = false;
                    graph = null;
                }
                else
                {
                    NewRoom();
                    Visualize();
                    visualize = true;
                }
                break;
            case (false, true):
                justPressed = false;
                break;
            default:
                break;
        }
    }

    private void Visualize()
    {
        foreach (var node in graph)
        {
            if (node is null)
            {
                continue;
            }

            var start = creature.room.MiddleOfTile(node.gridPos);
            foreach (var connection in node.connections)
            {
                var end = creature.room.MiddleOfTile(connection.next.gridPos);
                var mesh = Visualizer.MakeLine(start, end);
                var line = new DebugSprite(start, mesh, creature.room);
                creature.room.AddObject(line);
                visualizationSprites.Add(line);
            }

            var color = node.type switch
            {
                NodeType.Air => Color.red,
                NodeType.Floor => Color.white,
                NodeType.Slope => Color.green,
                NodeType.Corridor => Color.blue,
                NodeType.ShortcutEntrance => Color.cyan,
                _ => throw new ArgumentOutOfRangeException("unsupported NodeType variant"),
            };

            var pos = creature.room.MiddleOfTile(node.gridPos);
            var fs = new FSprite("pixel")
            {
                color = color,
                scale = 5f,
            };
            var sprite = new DebugSprite(pos, fs, creature.room);
            creature.room.AddObject(sprite);
            visualizationSprites.Add(sprite);
        }
    }

    public void NewRoom()
    {
        if (creature.room is null)
        {
            return;
        }
        Room room = creature.room;
        int width = room.Tiles.GetLength(0);
        int height = room.Tiles.GetLength(1);
        graph = new Node[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x - 1 < 0 || x + 1 >= width || y - 1 < 0 || y + 1 >= height)
                {
                    continue;
                }
                if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Solid)
                {
                    continue;
                }
                else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Floor)
                {
                    if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid)
                    {
                        graph[x, y] = new Node(new NodeType.Corridor(), x, y)
                        {
                            hasPlatform = true,
                        };
                    }
                }
                else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Air)
                {
                    if (!(room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Air
                            && room.Tiles[x - 1, y + 1].Terrain == Room.Tile.TerrainType.Air
                            && room.Tiles[x, y + 1].Terrain == Room.Tile.TerrainType.Air)
                        && !(room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Air
                            && room.Tiles[x + 1, y + 1].Terrain == Room.Tile.TerrainType.Air
                            && room.Tiles[x, y + 1].Terrain == Room.Tile.TerrainType.Air)
                        && !(room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Air
                            && room.Tiles[x - 1, y - 1].Terrain == Room.Tile.TerrainType.Air
                            && room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Air)
                        && !(room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Air
                            && room.Tiles[x + 1, y - 1].Terrain == Room.Tile.TerrainType.Air
                            && room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Air))
                    {
                        graph[x, y] = new Node(new NodeType.Corridor(), x, y);
                    }
                    else if (
                        room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Solid
                        || room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Floor
                        || room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.ShortcutEntrance
                        // pretend invalid slope is solid
                        || room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Slope
                        && room.Tiles[x - 1, y - 1].Terrain == Room.Tile.TerrainType.Solid
                        && room.Tiles[x + 1, y - 1].Terrain == Room.Tile.TerrainType.Solid)
                    {
                        graph[x, y] = new Node(new NodeType.Floor(), x, y);
                    }
                }
                else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Slope
                    && room.Tiles[x, y + 1].Terrain == Room.Tile.TerrainType.Air
                    && !(room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid
                        && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid))
                {
                    graph[x, y] = new Node(new NodeType.Slope(), x, y);
                }
                else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.ShortcutEntrance)
                {
                    int index = Array.IndexOf(room.shortcutsIndex, new IntVector2(x, y));
                    if (index > -1 && room.shortcuts[index].shortCutType == ShortcutData.Type.Normal)
                    {
                        graph[x, y] = new Node(new NodeType.ShortcutEntrance(index), x, y);
                    }
                }

                if (room.Tiles[x, y].AnyBeam)
                {
                    if (graph[x, y] is null)
                    {
                        graph[x, y] = new Node(new NodeType.Air(), x, y);
                    }
                    graph[x, y].hasBeam = true;
                }
            }
        }
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (graph[x, y] is null)
                {
                    continue;
                }
                if (graph[x, y].type is NodeType.Floor)
                {
                    if (x + 1 < width)
                    {
                        if (graph[x + 1, y]?.type is NodeType.Floor or NodeType.Slope or NodeType.Corridor)
                        {
                            graph[x, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x + 1, y]));
                            graph[x + 1, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y]));
                        }
                        if (y - 1 > 0 && graph[x + 1, y - 1]?.type is NodeType.Slope or NodeType.Corridor)
                        {
                            graph[x, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x + 1, y - 1]));
                            graph[x + 1, y - 1].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y]));
                        }
                    }
                }

                if (graph[x, y].type is NodeType.Slope && x + 1 < width)
                {
                    if (graph[x + 1, y]?.type is NodeType.Floor or NodeType.Slope)
                    {
                        graph[x, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x + 1, y]));
                        graph[x + 1, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y]));
                    }
                    else if (y - 1 > 0 && graph[x + 1, y - 1]?.type is NodeType.Slope)
                    {
                        graph[x, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x + 1, y - 1]));
                        graph[x + 1, y - 1].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y]));
                    }
                    else if (y + 1 < height && graph[x + 1, y + 1]?.type is NodeType.Slope or NodeType.Floor)
                    {
                        graph[x, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x + 1, y + 1]));
                        graph[x + 1, y + 1].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y]));
                    }
                }

                // TODO: climbing is slower than walking, the connection weights should be adjusted accordingly
                if (room.Tiles[x, y].horizontalBeam && x + 1 < width && room.Tiles[x + 1, y].horizontalBeam)
                {
                    graph[x, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x + 1, y]));
                    graph[x + 1, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y]));
                }
                if (room.Tiles[x, y].verticalBeam && y + 1 < height && room.Tiles[x, y + 1].verticalBeam)
                {
                    graph[x, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y + 1]));
                    graph[x, y + 1].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y]));
                }
                // TODO: weights, again
                if (graph[x, y].type is NodeType.Corridor)
                {
                    if (x + 1 < width)
                    {
                        if (graph[x + 1, y]?.type is NodeType.Corridor or NodeType.Floor or NodeType.ShortcutEntrance)
                        {
                            graph[x, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x + 1, y]));
                            graph[x + 1, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y]));
                        }
                        if (y + 1 < height && graph[x + 1, y + 1]?.type is NodeType.Floor)
                        {
                            graph[x, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x + 1, y + 1]));
                            graph[x + 1, y + 1].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y]));
                        }
                    }
                    if (y + 1 < height && graph[x, y + 1]?.type is NodeType.Corridor or NodeType.Floor or NodeType.ShortcutEntrance)
                    {
                        graph[x, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y + 1]));
                        graph[x, y + 1].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y]));
                    }
                }
                if (graph[x, y].type is NodeType.ShortcutEntrance)
                {
                    var entrance = graph[x, y].type as NodeType.ShortcutEntrance;
                    var shortcutData = room.shortcuts[entrance.Index];
                    var destNode = graph[shortcutData.destinationCoord.x, shortcutData.destinationCoord.y];
                    if (destNode is null || destNode.type is not NodeType.ShortcutEntrance)
                    {
                        Plugin.Logger.LogError($"Shortcut entrance has no valid exit, pos: ({x}, {y}), index: {entrance.Index}");
                        return;
                    }
                    graph[x, y].connections.Add(new NodeConnection(ConnectionType.Shortcut, destNode, shortcutData.length));
                    if (x + 1 < width && graph[x + 1, y]?.type is NodeType.Corridor)
                    {
                        graph[x, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x + 1, y]));
                        graph[x + 1, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y]));
                    }
                    if (y + 1 < height && graph[x, y + 1]?.type is NodeType.Corridor)
                    {
                        graph[x, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y + 1]));
                        graph[x, y + 1].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y]));
                    }
                }
            }
        }
    }

    public void FindPath()
    {
        path.Clear();
        // do thing
    }
}

static class PathfinderHooks
{
    public static void RegisterHooks()
    {
        On.Player.ctor += Player_ctor;
        On.Player.Update += Player_Update;
    }
    private static void Player_ctor(On.Player.orig_ctor orig, Player self, AbstractCreature abstractCreature, World world)
    {
        orig(self, abstractCreature, world);
        self.GetCWT().pathfinder = new Pathfinder(self);
    }

    private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
    {
        orig(self, eu);
        self.GetCWT().pathfinder?.Update();
    }
}