
using System;
using System.Collections.Generic;
using RWCustom;
using UnityEngine;

namespace AIMod;

class Pathfinder
{
    public class PathNode
    {
        public IntVector2 gridPos;
        public PathConnection invertedConnection;
        public float pathCost;
        public float heuristic;
        public PathNode(IntVector2 gridPos, IntVector2 goalPos, float cost)
        {
            this.gridPos = gridPos;
            pathCost = cost;
            heuristic = Mathf.Sqrt((gridPos.x - goalPos.x) * (gridPos.x - goalPos.x) + (gridPos.y - goalPos.y) * (gridPos.y - goalPos.y));
        }
    }
    public class PathConnection
    {
        public ConnectionType type;
        public PathNode previous;
        public PathConnection(ConnectionType type, PathNode previous)
        {
            this.type = type;
            this.previous = previous;
        }
    }
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
    public List<PathNode> path;
    public JumpTracer jumpTracer;
    public Node[,] graph;
    private bool justPressedG;
    private bool justPressedN;
    private bool justPressedC;
    private bool justPressedLeft;
    private bool visualizingNodes;
    private bool visualizingConnections;
    private List<DebugSprite> nodeSprites;
    private List<DebugSprite> connectionSprites;
    private List<DebugSprite> pathSprites;
    public Pathfinder(Creature creature)
    {
        this.creature = creature;
        path = new();
        nodeSprites = new();
        connectionSprites = new();
        pathSprites = new();
    }

    public void Update()
    {
        switch ((Input.GetKey(KeyCode.G), justPressedG))
        {
            case (true, false):
                justPressedG = true;
                if (graph is null)
                {
                    NewRoom();
                }
                else
                {
                    graph = null;
                }
                break;
            case (false, true):
                justPressedG = false;
                break;
            default:
                break;
        }
        switch ((Input.GetKey(KeyCode.N), justPressedN))
        {
            case (true, false):
                justPressedN = true;
                if (visualizingNodes)
                {
                    foreach (var sprite in nodeSprites)
                    {
                        sprite.Destroy();
                    }
                    nodeSprites.Clear();
                    visualizingNodes = false;
                }
                else
                {
                    VisualizeNodes();
                    visualizingNodes = true;
                }
                break;
            case (false, true):
                justPressedN = false;
                break;
            default:
                break;
        }
        switch ((Input.GetKey(KeyCode.C), justPressedC))
        {
            case (true, false):
                justPressedC = true;
                if (visualizingConnections)
                {
                    foreach (var sprite in connectionSprites)
                    {
                        sprite.Destroy();
                    }
                    connectionSprites.Clear();
                    visualizingConnections = false;
                }
                else
                {
                    VisualizeConnections();
                    visualizingConnections = true;
                }
                break;
            case (false, true):
                justPressedC = false;
                break;
            default:
                break;
        }
        switch ((Input.GetMouseButton(0), justPressedLeft))
        {
            case (true, false):
                justPressedLeft = true;
                FindPath();
                foreach (var sprite in pathSprites)
                {
                    sprite.Destroy();
                }
                pathSprites.Clear();
                VisualizePath();
                break;
            case (false, true):
                justPressedLeft = false;
                break;
            default:
                break;
        }
        var mousePos = (Vector2)Input.mousePosition + creature.room.game.cameras[0].pos;
        destination = creature.room.ToWorldCoordinate(mousePos);
    }

    private void VisualizeNodes()
    {
        foreach (var node in graph)
        {
            if (node is null)
            {
                continue;
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
            nodeSprites.Add(sprite);
        }
    }

    private void VisualizeConnections()
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
                connectionSprites.Add(line);
            }
        }
    }

    private void VisualizePath()
    {
        foreach (var node in path)
        {
            var start = creature.room.MiddleOfTile(node.gridPos);
            var end = creature.room.MiddleOfTile(node.invertedConnection.previous.gridPos);
            var mesh = Visualizer.MakeLine(start, end);
            var line = new DebugSprite(start, mesh, creature.room);
            creature.room.AddObject(line);
            pathSprites.Add(line);
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
                            // these need to have higher weights than normal movement so the pathfinding algorithm doesn't prefer them
                            graph[x, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x + 1, y - 1], 2));
                            graph[x + 1, y - 1].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y], 2));
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
                            // these need to have higher weights than normal movement so the pathfinding algorithm doesn't prefer them
                            graph[x, y].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x + 1, y + 1], 2));
                            graph[x + 1, y + 1].connections.Add(new NodeConnection(ConnectionType.Standard, graph[x, y], 2));
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
        // TODO: optimize this entire function, it's probably really inefficient
        var startPos = creature.room.GetTilePosition(creature.mainBodyChunk.pos);
        if (graph[startPos.x, startPos.y] is null)
        {
            Plugin.Logger.LogDebug($"no node at start ({startPos.x}, {startPos.y})");
            return;
        }
        path.Clear();
        var goalPos = new IntVector2(destination.x, destination.y);
        if (graph[goalPos.x, goalPos.y] is null)
        {
            Plugin.Logger.LogDebug($"no node at destination ({goalPos.x}, {goalPos.y})");
            return;
        }
        var openNodes = new List<PathNode>()
        {
            new(startPos, goalPos, 0),
        };
        var closedNodes = new List<PathNode>();
        while (openNodes.Count > 0)
        {
            var currentF = float.MaxValue;
            PathNode currentNode = null;
            int currentIndex = 0;
            for (int i = 0; i < openNodes.Count; i++)
            {
                if (openNodes[i].pathCost + openNodes[i].heuristic < currentF)
                {
                    currentNode = openNodes[i];
                    currentIndex = i;
                    currentF = openNodes[i].pathCost + openNodes[i].heuristic;
                }
            }

            // might be redundant
            if (currentNode is null)
            {
                Plugin.Logger.LogError($"current node was null");
                return;
            }

            if (currentNode.gridPos == goalPos)
            {
                while (currentNode.gridPos != startPos)
                {
                    path.Add(currentNode);
                    if (currentNode.invertedConnection.previous is null)
                    {
                        break;
                    }
                    currentNode = currentNode.invertedConnection.previous;
                }
                return;
            }

            openNodes.RemoveAt(currentIndex);
            closedNodes.Add(currentNode);
            foreach (var connection in graph[currentNode.gridPos.x, currentNode.gridPos.y].connections)
            {
                PathNode currentNeighbour = null;
                foreach (var node in openNodes)
                {
                    if (connection.next.gridPos == node.gridPos)
                    {
                        currentNeighbour = node;
                    }
                }

                if (currentNeighbour is null)
                {
                    foreach (var node in closedNodes)
                    {
                        if (connection.next.gridPos == node.gridPos)
                        {
                            goto next_connection;
                        }
                    }
                    var newNode = new PathNode(connection.next.gridPos, goalPos, currentNode.pathCost + connection.weight)
                    {
                        invertedConnection = new PathConnection(connection.type, currentNode),
                    };
                    openNodes.Add(newNode);
                }
                else if (currentNode.pathCost + connection.weight < currentNeighbour.invertedConnection.previous.pathCost)
                {
                    currentNeighbour.pathCost = currentNode.pathCost + connection.weight;
                    currentNeighbour.invertedConnection = new PathConnection(connection.type, currentNode);
                }
            next_connection:;
            }
        }
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