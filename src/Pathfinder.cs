
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using MoreSlugcats;
using RWCustom;
using UnityEngine;

namespace JumpSlug;

class Pathfinder
{
    // linked list of path nodes with cursor pointing to the current path node
    public class Path
    {
        public PathNode start;
        public PathNode cursor;
        public Path(PathNode start)
        {
            this.start = start;
            cursor = start;
        }
    }

    public class PathNode
    {
        public IntVector2 gridPos;
        public PathConnection? connection;
        public float pathCost;
        public float heuristic;
        public PathNode(IntVector2 gridPos, IntVector2 goalPos, float cost)
        {
            this.gridPos = gridPos;
            pathCost = cost;
            heuristic = Mathf.Sqrt((gridPos.x - goalPos.x) * (gridPos.x - goalPos.x) + (gridPos.y - goalPos.y) * (gridPos.y - goalPos.y));
        }
    }
    public struct PathConnection
    {
        public ConnectionType type;
        public PathNode next;
        public PathConnection(ConnectionType type, PathNode next)
        {
            this.type = type;
            this.next = next;
        }
    }
    public class Node
    {
        public NodeType type;
        public bool verticalBeam;
        public bool horizontalBeam;
        public bool hasPlatform;
        public List<NodeConnection> connections;
        public List<NodeConnection> dynamicConnections;
        public IntVector2 gridPos;

        public Node(NodeType type, int x, int y)
        {
            this.type = type;
            gridPos = new IntVector2(x, y);
            connections = new();
            dynamicConnections = new();
        }
    }
    public record NodeType
    {
        public record Air() : NodeType();
        public record Floor() : NodeType();
        public record Slope() : NodeType();
        public record Corridor() : NodeType();
        public record ShortcutEntrance(int Index) : NodeType();
        public record Wall(int Direction) : NodeType();

        private NodeType() { }
    }

    public class NodeConnection
    {
        public ConnectionType type;
        public Node next;
        public float weight;

        public NodeConnection(ConnectionType type, Node next, float weight = 1)
        {
            if (next is null)
            {
                throw new NoNullAllowedException();
            }
            this.next = next;
            this.weight = weight;
            this.type = type;
        }
    }

    public record ConnectionType
    {
        public record Walk(int Direction) : ConnectionType();
        public record Climb(IntVector2 Direction) : ConnectionType();
        public record Crawl(IntVector2 Direction) : ConnectionType();
        public record Jump(int Direction) : ConnectionType();
        public record WalkOffEdge(int Direction) : ConnectionType();
        public record Pounce(int Direction) : ConnectionType();
        public record Shortcut() : ConnectionType();
        public record Drop() : ConnectionType();

        private ConnectionType() {}
    }

    public Player player;
    public Node?[,]? graph;
    private bool justPressedG;
    public Pathfinder(Player player)
    {
        this.player = player;
        graph = new Node[0,0];
    }

    public Node? GetNode(int x, int y)
    {
        if (graph is null || x < 0 || y < 0 || x >= graph.GetLength(0) || y >= graph.GetLength(1))
        {
            return null;
        }
        return graph[x, y];
    }

    public Node? GetNode(IntVector2 pos)
    {
        return GetNode(pos.x, pos.y);
    }

    public IntVector2? CurrentNodePos()
    {
        var pos = player.room.GetTilePosition(player.bodyChunks[player.standing ? 1 : 0].pos);
        if (graph is null)
        {
            return null;
        }
        if (0 < pos.x && pos.x < graph.GetLength(0) && 0 < pos.y && pos.y < graph.GetLength(1))
        {
            return pos;
        }
        return null;
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
    }

    public void NewRoom()
    {
        if (player.room is null)
        {
            return;
        }
        Room room = player.room;
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
                        || room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.ShortcutEntrance
                        // pretend invalid slope is solid
                        || room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Slope
                        && room.Tiles[x - 1, y - 1].Terrain == Room.Tile.TerrainType.Solid
                        && room.Tiles[x + 1, y - 1].Terrain == Room.Tile.TerrainType.Solid)
                    {
                        graph[x, y] = new Node(new NodeType.Floor(), x, y);
                    }
                    else if (room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Floor)
                    {
                        graph[x, y] = new Node(new NodeType.Floor(), x, y);
                    }
                    else if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Air
                        && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid)
                    {
                        graph[x, y] = new Node(new NodeType.Wall(1), x, y);
                    }
                    else if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid
                        && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Air)
                    {
                        graph[x, y] = new Node(new NodeType.Wall(-1), x, y);
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

                if (room.Tiles[x, y].verticalBeam)
                {
                    if (graph[x, y] is null)
                    {
                        graph[x, y] = new Node(new NodeType.Air(), x, y);
                    }
                    graph[x, y]!.verticalBeam = true;
                }

                if (room.Tiles[x, y].horizontalBeam)
                {
                    if (graph[x, y] is null)
                    {
                        graph[x, y] = new Node(new NodeType.Air(), x, y);
                    }
                    graph[x, y]!.horizontalBeam = true;
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
                if (graph[x, y]!.type is NodeType.Floor)
                {
                    if (GetNode(x + 1, y)?.type is NodeType.Floor or NodeType.Slope)
                    {
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Walk(1), graph[x + 1, y]!));
                        graph[x + 1, y]!.connections.Add(new NodeConnection(new ConnectionType.Walk(-1), graph[x, y]!));
                    }
                    else if (GetNode(x + 1, y)?.type is NodeType.Corridor)
                    {
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(-1, 0)), graph[x + 1, y]!));
                        graph[x + 1, y]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(-1, 0)), graph[x, y]!));
                    }
                    if (GetNode(x + 1, y - 1)?.type is NodeType.Slope or NodeType.Corridor)
                    {
                        // these need to have higher weights than normal movement so the pathfinding algorithm doesn't prefer them
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(1, -1)), graph[x + 1, y - 1]!, 2));
                        graph[x + 1, y - 1]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(-1, 1)), graph[x, y]!, 2));
                    }
                    if (GetNode(x + 1, y + 1)?.type is NodeType.Corridor)
                    {
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Walk(1), graph[x + 1, y + 1]!));
                    }
                }

                if (graph[x, y]!.type is NodeType.Slope)
                {
                    if (GetNode(x + 1, y)?.type is NodeType.Floor or NodeType.Slope)
                    {
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Walk(1), graph[x + 1, y]!));
                        graph[x + 1, y]!.connections.Add(new NodeConnection(new ConnectionType.Walk(-1), graph[x, y]!));
                    }
                    else if (GetNode(x + 1, y - 1)?.type is NodeType.Slope)
                    {
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Walk(1), graph[x + 1, y - 1]!));
                        graph[x + 1, y - 1]!.connections.Add(new NodeConnection(new ConnectionType.Walk(-1), graph[x, y]!));
                    }
                    else if (GetNode(x + 1, y + 1)?.type is NodeType.Slope or NodeType.Floor)
                    {
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Walk(1), graph[x + 1, y + 1]!));
                        graph[x + 1, y + 1]!.connections.Add(new NodeConnection(new ConnectionType.Walk(-1), graph[x, y]!));
                    }
                }

                // TODO: climbing is slower than walking, the connection weights should be adjusted accordingly
                if (graph[x, y]!.horizontalBeam)
                {
                    if (GetNode(x + 1, y)?.horizontalBeam == true)
                    {
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Climb(new IntVector2(1, 0)), graph[x + 1, y]!));
                        graph[x + 1, y]!.connections.Add(new NodeConnection(new ConnectionType.Climb(new IntVector2(-1, 0)), graph[x, y]!));
                    }
                }
                if (graph[x, y]!.verticalBeam)
                {
                    if (GetNode(x, y + 1)?.verticalBeam == true)
                    {
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Climb(new IntVector2(0, 1)), graph[x, y + 1]!));
                        graph[x, y + 1]!.connections.Add(new NodeConnection(new ConnectionType.Climb(new IntVector2(0, -1)), graph[x, y]!));
                    }
                }
                // TODO: weights, again
                if (graph[x, y]!.type is NodeType.Corridor)
                {
                    if (GetNode(x + 1, y)?.type is NodeType.Corridor or NodeType.Floor or NodeType.ShortcutEntrance)
                    {
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(1, 0)), graph[x + 1, y]!));
                        graph[x + 1, y]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(-1, 0)), graph[x, y]!));
                    }
                    if (GetNode(x + 1, y + 1)?.type is NodeType.Floor)
                    {
                        // these need to have higher weights than normal movement so the pathfinding algorithm doesn't prefer them
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(1, 1)), graph[x + 1, y + 1]!, 2));
                        graph[x + 1, y + 1]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(-1, -1)), graph[x, y]!, 2));
                    }
                    if (GetNode(x + 1, y - 1)?.type is NodeType.Floor)
                    {
                        graph[x + 1, y - 1]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(1, -1)), graph[x, y]!));
                    }
                    if (GetNode(x, y + 1)?.type is NodeType.Corridor or NodeType.Floor or NodeType.ShortcutEntrance)
                    {
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(0, 1)), graph[x, y + 1]!));
                        graph[x, y + 1]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(0, -1)), graph[x, y]!));
                    }
                }
                if (graph[x, y]!.type is NodeType.ShortcutEntrance)
                {
                    var entrance = graph[x, y]!.type as NodeType.ShortcutEntrance;
                    var shortcutData = room.shortcuts[entrance!.Index];
                    var destNode = graph[shortcutData.destinationCoord.x, shortcutData.destinationCoord.y];
                    if (destNode is null || destNode.type is not NodeType.ShortcutEntrance)
                    {
                        Plugin.Logger!.LogError($"Shortcut entrance has no valid exit, pos: ({x}, {y}), index: {entrance.Index}");
                        return;
                    }
                    graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Shortcut(), destNode, shortcutData.length));
                    if (GetNode(x + 1, y)?.type is NodeType.Corridor)
                    {
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(1, 0)), graph[x + 1, y]!));
                        graph[x + 1, y]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(-1, 0)), graph[x, y]!));
                    }
                    if (GetNode(x, y + 1)?.type is NodeType.Corridor)
                    {
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(0, 1)), graph[x, y + 1]!));
                        graph[x, y + 1]!.connections.Add(new NodeConnection(new ConnectionType.Crawl(new IntVector2(0, -1)), graph[x, y]!));
                    }
                }
            }
        }
    }

    private void TraceDrop(int x, int y)
    {
        if (graph is null || graph[x, y]?.type is NodeType.Floor or NodeType.Slope)
        {
            return;
        }
        for (int i = y - 1; i >= 0; i--)
        {
            if (graph[x, i] is null)
            {
                continue;
            }
            if (graph[x, i]!.type is NodeType.Floor or NodeType.Slope)
            {
                // t = sqrt(2 * d / g)
                // weight might have inaccurate units
                graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Drop(), graph[x, i]!, Mathf.Sqrt(2 * 20 * (y - i) / player.room.gravity) * 4.2f / 20));
                break;
            }
            else if (graph[x, i]!.horizontalBeam)
            {
                graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Drop(), graph[x, i]!, Mathf.Sqrt(2 * 20 * (y - i) / player.room.gravity)));
            }
        }
    }

    private float Parabola(float yOffset, Vector2 v0, float t) => v0.y * t - 0.5f * player.gravity * t * t + yOffset;

    // start refers to the head position
    private void TraceJump(Node startNode, IntVector2 start, Vector2 v0, ConnectionType type)
    {
        var x = start.x;
        var y = start.y;
        var width = player.room.Tiles.GetLength(0);
        var height = player.room.Tiles.GetLength(1);
        if (x < 0 || y < 0 || x >= width || y >= height || graph is null)
        {
            return;
        }
        int direction = v0.x switch
        {
            > 0 => 1,
            < 0 => -1,
            0 or float.NaN => throw new ArgumentOutOfRangeException(),
        };
        int xOffset = (direction + 1) / 2;
        var pathOffset = player.room.MiddleOfTile(start);

        while (true)
        {
            float t = (20 * (x + xOffset) - pathOffset.x) / v0.x;
            float result = Parabola(pathOffset.y, v0, t) / 20;
            if (result > y + 1)
            {
                y++;
            }
            else if (result < y)
            {
                if (y - 2 < 0)
                {
                    break;
                }
                if (graph[x, y - 1]?.type is NodeType.Floor or NodeType.Slope)
                {
                    startNode.dynamicConnections.Add(new NodeConnection(type, graph[x, y - 1]!, t * 20 / 4.2f + 1));
                }
                if (player.room.Tiles[x, y - 2].Terrain == Room.Tile.TerrainType.Solid)
                {
                    break;
                }
                y--;
            }
            else
            {
                x += direction;
            }

            if (x < 0 || y < 0 || x >= width || y >= height
                || player.room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Solid
                || player.room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Slope)
            {
                break;
            }

            if (graph[x, y] is null)
            {
                continue;
            }
            if (graph[x, y]!.type is NodeType.Corridor)
            {
                break;
            }
            if (graph[x, y]!.type is NodeType.Wall wall && wall.Direction == direction)
            {
                startNode.dynamicConnections.Add(new NodeConnection(type, graph[x, y]!, t * 4.2f / 20));
                break;
            }
            else if (graph[x, y]!.verticalBeam)
            {
                float poleResult = Parabola(pathOffset.y, v0, (20 * x + 10 - pathOffset.x) / v0.x) / 20;
                if (poleResult > y && poleResult < y + 1)
                {
                    startNode.dynamicConnections.Add(new NodeConnection(type, graph[x, y]!, t * 4.2f / 20 + 5));
                }
            }
            else if (graph[x, y]!.horizontalBeam)
            {
                startNode.dynamicConnections.Add(new NodeConnection(type, graph[x, y]!, t * 4.2f / 20 + 10));
            }
        }
    }

    private static float JumpBoost(float boost)
    {
        float t = Mathf.Ceil(boost / 1.5f);
        return 0.3f * ((boost - 0.5f) * t - 0.75f * t * t);
    }

    public Path? FindPath(IntVector2 start, IntVector2 destination)
    {
        // TODO: optimize this entire function, it's probably really inefficient
        if (graph is null)
        {
            return null;
        }
        if (GetNode(start) is null)
        {
            Plugin.Logger!.LogDebug($"no node at start ({start.x}, {start.y})");
            return null;
        }
        if (GetNode(destination) is null)
        {
            Plugin.Logger!.LogDebug($"no node at destination ({destination.x}, {destination.y})");
            return null;
        }
        var openNodes = new List<PathNode>()
        {
            new(start, destination, 0),
        };
        var closedNodes = new List<PathNode>();
        while (openNodes.Count > 0)
        {
            var currentF = float.MaxValue;
            PathNode? currentNode = null;
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
                Plugin.Logger!.LogError($"current node was null");
                return null;
            }

            var currentPos = currentNode.gridPos;

            if (currentPos == destination)
            {
                PathNode? previousNode = null;
                PathNode? nextNode = null;
                ConnectionType? previousType = null;
                ConnectionType? currentType;
                while (currentNode is not null)
                {
                    nextNode = currentNode.connection?.next;
                    currentType = currentNode.connection?.type;
                    currentNode.connection = previousType is null ? null : new PathConnection(previousType, previousNode!);
                    previousType = currentType;
                    previousNode = currentNode;
                    currentNode = nextNode;
                }
                return previousNode is null ? null : new Path(previousNode);
            }

            openNodes.RemoveAt(currentIndex);
            closedNodes.Add(currentNode);

            var graphNode = graph[currentPos.x, currentPos.y];
            graphNode!.dynamicConnections.Clear();

            bool goLeft = true;
            bool goRight = true;
            if (graphNode.type is NodeType.Wall wall)
            {
                if (wall.Direction == -1)
                {
                    goLeft = false;
                }
                else
                {
                    goRight = false;
                }
            }
            if (GetNode(currentPos.x, currentPos.y - 1)?.type is NodeType.Wall footWall)
            {
                if (footWall.Direction == -1)
                {
                    goLeft = false;
                }
                else
                {
                    goRight = false;
                }
            }

            if (graphNode.verticalBeam && !graphNode.horizontalBeam
                && graphNode.type is not (NodeType.Corridor or NodeType.Floor or NodeType.Slope)
                && GetNode(currentPos.x, currentPos.y - 1)?.type is not (NodeType.Corridor or NodeType.Floor or NodeType.Slope))
            {
                Vector2 v0;
                if (player.isRivulet)
                {
                    v0 = new Vector2(9f, 9f) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                }
                else if (player.isSlugpup)
                {
                    v0 = new Vector2(5f, 7f) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                }
                else
                {
                    v0 = new Vector2(6f, 8f) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                }
                if (goRight)
                {
                    TraceJump(graphNode, currentPos, v0, new ConnectionType.Jump(1));
                }
                if (goLeft)
                {
                    v0.x = -v0.x;
                    TraceJump(graphNode, currentPos, v0, new ConnectionType.Jump(-1));
                }
                if (!graphNode.dynamicConnections.Any(c => c.type == new ConnectionType.Drop()))
                {
                    TraceDrop(currentPos.x, currentPos.y);
                }
            }
            if (graphNode.horizontalBeam && graphNode.type is not (NodeType.Corridor or NodeType.Floor or NodeType.Slope))
            {
                var headPos = new IntVector2(currentPos.x, currentPos.y + 1);
                var v0 = new Vector2(
                    4.2f * player.slugcatStats.runspeedFac * Mathf.Lerp(1, 1.5f, player.Adrenaline),
                    (player.isRivulet ? 6f : 4f) * Mathf.Lerp(1, 1.15f, player.Adrenaline) + JumpBoost(player.isSlugpup ? 7 : 8));
                if (goRight)
                {
                    TraceJump(graphNode, headPos, v0, new ConnectionType.Jump(1));
                }
                if (goLeft)
                {
                    v0.x = -v0.x;
                    TraceJump(graphNode, headPos, v0, new ConnectionType.Jump(-1));
                }

                if (!graphNode.dynamicConnections.Any(c => c.type == new ConnectionType.Drop()))
                {
                    TraceDrop(currentPos.x, currentPos.y);
                }
            }
            if (graphNode.type is NodeType.Floor)
            {
                var headPos = new IntVector2(currentPos.x, currentPos.y + 1);
                var v0 = new Vector2(
                    4.2f * player.slugcatStats.runspeedFac * Mathf.Lerp(1, 1.5f, player.Adrenaline),
                    (player.isRivulet ? 6f : 4f) * Mathf.Lerp(1, 1.15f, player.Adrenaline) + JumpBoost(player.isSlugpup ? 7 : 8));
                if (goRight)
                {
                    TraceJump(graphNode, headPos, v0, new ConnectionType.Jump(1));
                    if (headPos.x + 1 < graph.GetLength(0) && graph[currentPos.x + 1, currentPos.y - 1]?.type is NodeType.Wall)
                    {
                        v0.y = 0f;
                        TraceJump(graphNode, headPos, v0, new ConnectionType.WalkOffEdge(1));
                    }
                }
                if (goLeft)
                {
                    v0.x = -v0.x;
                    TraceJump(graphNode, headPos, v0, new ConnectionType.Jump(-1));
                    if (currentPos.x - 1 > 0 && graph[currentPos.x - 1, currentPos.y - 1]?.type is NodeType.Wall)
                    {
                        v0.y = 0f;
                        TraceJump(graphNode, headPos, v0, new ConnectionType.WalkOffEdge(-1));
                    }
                }

            }
            else if (graphNode.type is NodeType.Corridor)
            {
                var v0 = new Vector2(
                    4.2f * player.slugcatStats.runspeedFac * Mathf.Lerp(1, 1.5f, player.Adrenaline),
                    0);
                // v0.x might be too large
                if (currentPos.x + 1 < graph.GetLength(0) && graph[currentPos.x + 1, currentPos.y] is null)
                {
                    TraceJump(graphNode, currentPos, v0, new ConnectionType.WalkOffEdge(1));
                }
                if (currentPos.x - 1 > 0 && graph[currentPos.x - 1, currentPos.y] is null)
                {
                    v0.x = -v0.x;
                    TraceJump(graphNode, currentPos, v0, new ConnectionType.WalkOffEdge(-1));
                }
                if (currentPos.y - 1 > 0
                    && graph[currentPos.x, currentPos.y - 1] is null
                    && player.room.Tiles[currentPos.x, currentPos.y - 1].Terrain == Room.Tile.TerrainType.Air)
                {
                    TraceDrop(currentPos.x, currentPos.y);
                }
            }
            else if (graphNode.type is NodeType.Wall jumpWall)
            {
                Vector2 v0;
                if (player.isRivulet)
                {
                    v0 = new Vector2(-jumpWall.Direction * 9, 10) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                }
                else if (player.isSlugpup)
                {
                    v0 = new Vector2(-jumpWall.Direction * 5, 6) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                }
                else
                {
                    v0 = new Vector2(-jumpWall.Direction * 6, 8) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                }
                TraceJump(graphNode, currentPos, v0, new ConnectionType.Jump(-jumpWall.Direction));
            }

            void CheckConnection(NodeConnection connection)
            {
                PathNode? currentNeighbour = null;
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
                            return;
                        }
                    }
                    currentNeighbour = new PathNode(connection.next.gridPos, destination, currentNode.pathCost + connection.weight)
                    {
                        connection = new PathConnection(connection.type, currentNode),
                    };
                    openNodes.Add(currentNeighbour);
                }
                if (currentNode.pathCost + connection.weight < currentNeighbour.pathCost)
                {
                    currentNeighbour.pathCost = currentNode.pathCost + connection.weight;
                    currentNeighbour.connection = new PathConnection(connection.type, currentNode);
                }
            }

            foreach (var connection in graphNode.connections)
            {
                CheckConnection(connection);
            }
            foreach (var connection in graphNode.dynamicConnections)
            {
                CheckConnection(connection);
            }
        }
        return null;
    }

    public class Visualizer
    {
        public Pathfinder pathfinder;
        public Player player => pathfinder.player;
        public bool visualizingNodes { get; private set; }
        public bool visualizingConnections { get; private set; }
        public bool visualizingPath { get; private set; }
        private List<DebugSprite> nodeSprites;
        private List<DebugSprite> connectionSprites;
        private List<DebugSprite> pathSprites;

        public Visualizer(Pathfinder pathfinder)
        {
            this.pathfinder = pathfinder;
            nodeSprites = new();
            connectionSprites = new();
            pathSprites = new();
        }

        public void ToggleNodes()
        {
            if (visualizingNodes || pathfinder is null || pathfinder.graph is null)
            {
                foreach (var sprite in nodeSprites)
                {
                    sprite.Destroy();
                }
                nodeSprites.Clear();
                visualizingNodes = false;
                return;
            }
            visualizingNodes = true;
            foreach (var node in pathfinder.graph)
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
                    NodeType.Wall => Color.grey,
                    _ => throw new ArgumentOutOfRangeException("unsupported NodeType variant"),
                };

                var pos = pathfinder.player.room.MiddleOfTile(node.gridPos);
                var fs = new FSprite("pixel")
                {
                    color = color,
                    scale = 5f,
                };
                var sprite = new DebugSprite(pos, fs, pathfinder.player.room);
                pathfinder.player.room.AddObject(sprite);
                nodeSprites.Add(sprite);
            }
        }

        public void ToggleConnections()
        {
            if (visualizingConnections || pathfinder is null || pathfinder.graph is null)
            {
                foreach (var sprite in connectionSprites)
                {
                    sprite.Destroy();
                }
                connectionSprites.Clear();
                visualizingConnections = false;
                return;
            }
            visualizingConnections = true;
            foreach (var node in pathfinder.graph)
            {
                if (node is null)
                {
                    continue;
                }

                var start = pathfinder.player.room.MiddleOfTile(node.gridPos);
                foreach (var connection in node.connections)
                {
                    var end = pathfinder.player.room.MiddleOfTile(connection.next.gridPos);
                    var mesh = LineHelper.MakeLine(start, end, Color.white);
                    var line = new DebugSprite(start, mesh, pathfinder.player.room);
                    pathfinder.player.room.AddObject(line);
                    connectionSprites.Add(line);
                }
            }
        }

        public void TogglePath(PathNode? path)
        {
            if (visualizingPath || pathfinder?.graph is null || path is null)
            {
                foreach (var sprite in pathSprites)
                {
                    sprite.Destroy();
                }
                pathSprites.Clear();
                visualizingPath = false;
                return;
            }
            visualizingPath = true;
            PathNode node = path;
            while (node.connection is not null)
            {
                var connection = node.connection.Value;
                var startTile = node.gridPos;
                var endTile = connection.next.gridPos;
                var start = pathfinder.player.room.MiddleOfTile(startTile);
                var end = pathfinder.player.room.MiddleOfTile(endTile);
                var color = connection.type switch
                {
                    ConnectionType.Jump or ConnectionType.WalkOffEdge => Color.blue,
                    ConnectionType.Pounce => Color.green,
                    ConnectionType.Drop => Color.red,
                    ConnectionType.Shortcut => Color.cyan,
                    ConnectionType.Walk or ConnectionType.Climb or ConnectionType.Crawl => Color.white,
                    _ => throw new ArgumentOutOfRangeException(),
                };
                int direction = startTile.x < endTile.x ? 1 : -1;
                if (connection.type is ConnectionType.Jump)
                {
                    // this node can be null only if the path is constructed incorrectly so this should throw
                    Node graphNode = pathfinder.graph[startTile.x, startTile.y]!;
                    if (graphNode.verticalBeam && !graphNode.horizontalBeam)
                    {
                        Vector2 v0;
                        if (player.isRivulet)
                        {
                            v0 = new Vector2(9f * direction, 9f) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                        }
                        else if (player.isSlugpup)
                        {
                            v0 = new Vector2(5f * direction, 7f) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                        }
                        else
                        {
                            v0 = new Vector2(6f * direction, 8f) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                        }
                        VisualizeJump(v0, startTile, endTile);
                    }
                    else if (graphNode.horizontalBeam || graphNode.type is NodeType.Floor)
                    {
                        var headPos = new IntVector2(startTile.x, startTile.y + 1);
                        var v0 = new Vector2(
                            4.2f * direction * player.slugcatStats.runspeedFac * Mathf.Lerp(1, 1.5f, player.Adrenaline),
                            (player.isRivulet ? 6f : 4f) * Mathf.Lerp(1, 1.15f, pathfinder.player.Adrenaline) + JumpBoost(player.isSlugpup ? 7 : 8));
                        VisualizeJump(v0, headPos, endTile);
                        var preLine = LineHelper.MakeLine(start, pathfinder.player.room.MiddleOfTile(headPos), Color.white);
                        var preSprite = new DebugSprite(start, preLine, pathfinder.player.room);
                        pathSprites.Add(preSprite);
                        player.room.AddObject(preSprite);
                    }
                    else if (graphNode.type is NodeType.Wall wall)
                    {
                        Vector2 v0;
                        if (player.isRivulet)
                        {
                            v0 = new Vector2(-wall.Direction * 9, 10) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                        }
                        else if (pathfinder.player.isSlugpup)
                        {
                            v0 = new Vector2(-wall.Direction * 5, 6) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                        }
                        else
                        {
                            v0 = new Vector2(-wall.Direction * 6, 8) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                        }
                        VisualizeJump(v0, startTile, endTile);
                    }
                }
                else if (connection.type is ConnectionType.WalkOffEdge)
                {
                    var headPos = new IntVector2(startTile.x, startTile.y + 1);
                    var v0 = new Vector2(
                        4.2f * direction * player.slugcatStats.runspeedFac * Mathf.Lerp(1, 1.5f, player.Adrenaline),
                        0);
                    VisualizeJump(v0, headPos, endTile);
                    var preLine = LineHelper.MakeLine(start, player.room.MiddleOfTile(headPos), Color.white);
                    var preSprite = new DebugSprite(start, preLine, player.room);
                    pathSprites.Add(preSprite);
                    player.room.AddObject(preSprite);
                }
                var mesh = LineHelper.MakeLine(start, end, color);
                var sprite = new DebugSprite(start, mesh, player.room);
                player.room.AddObject(sprite);
                pathSprites.Add(sprite);
                node = connection.next;
            }
        }

        private void VisualizeJump(Vector2 v0, IntVector2 startTile, IntVector2 endTile)
        {
            Vector2 pathOffset = player.room.MiddleOfTile(startTile);
            Vector2 lastPos = pathOffset;
            float maxT = 20 * (endTile.x - startTile.x) / v0.x;
            for (float t = 0; t < maxT; t += 2f)
            {
                var nextPos = new Vector2(pathOffset.x + v0.x * t, pathfinder.Parabola(pathOffset.y, v0, t));
                var sprite = new DebugSprite(lastPos, LineHelper.MakeLine(lastPos, nextPos, Color.white), player.room);
                pathSprites.Add(sprite);
                player.room.AddObject(sprite);
                lastPos = nextPos;
            }
            var postLine = LineHelper.MakeLine(lastPos, player.room.MiddleOfTile(endTile), Color.white);
            var postSprite = new DebugSprite(lastPos, postLine, player.room);
            pathSprites.Add(postSprite);
            player.room.AddObject(postSprite);
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

    public static void UnregisterHooks()
    {
        On.Player.ctor -= Player_ctor;
        On.Player.Update -= Player_Update;
    }

    private static void Player_ctor(On.Player.orig_ctor orig, Player self, AbstractCreature abstractCreature, World world)
    {
        orig(self, abstractCreature, world);
        self.GetCWT().pathfinder = new Pathfinder(self);
    }

    private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
    {
        // horrible hack because creature creation is pain
        var mousePos = (Vector2)Input.mousePosition + self.room.game.cameras[0].pos;
        switch ((Input.GetMouseButton(1), self.GetCWT().justPressedRight))
        {
            case (true, false):
                self.GetCWT().justPressedRight = true;
                var scugTemplate = StaticWorld.GetCreatureTemplate(MoreSlugcatsEnums.CreatureTemplateType.SlugNPC);
                var abstractScug = new AbstractCreature(
                    self.room.world,
                    scugTemplate,
                    null,
                    self.room.ToWorldCoordinate(mousePos),
                    self.room.game.GetNewID());
                abstractScug.state = new PlayerNPCState(abstractScug, 0)
                {
                    forceFullGrown = true,
                };
                self.room.abstractRoom.AddEntity(abstractScug);
                abstractScug.RealizeInRoom();
                abstractScug.abstractAI = new JumpSlugAbstractAI(abstractScug, self.room.world)
                {
                    RealAI = new JumpSlugAI(abstractScug, self.room.world),
                };
                var realizedScug = (abstractScug.realizedCreature as Player)!;
                realizedScug.controller = null;
                break;
            case (false, true):
                self.GetCWT().justPressedRight = false;
                break;
            default:
                break;
        }
        orig(self, eu);
        self.GetCWT().pathfinder?.Update();
    }
}