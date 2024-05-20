using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading;

using MoreSlugcats;

using UnityEngine;

using IVec2 = RWCustom.IntVector2;

namespace JumpSlug.Pathfinding;

public class Path {
    // these aren't private because it's more straightforward for the visualizer to use manual indexing
    // and the accessors can't be made const correct
    public int cursor;
    public readonly List<IVec2> nodes;
    public readonly List<ConnectionType> connections;

    public Path(PathNode startNode, SharedGraph staticGraph) {
        nodes = new();
        connections = new();
        var currentNode = startNode;
        while (currentNode is not null) {
            nodes.Add(currentNode.gridPos);
            if (currentNode.connection is not null) {
                // this is necessary because the pathfinder can't account for the slugcat taking up more than one tile
                // and the AI movement code can't account for missing sections of the path
                // any non-hacky way of dealing with this requires sweeping code changes that complicate everything
                var nextConnection = connections.Count > 0 ? connections[connections.Count - 1] : null;
                var previousConnection = currentNode.connection.Value.type;
                if (previousConnection is ConnectionType.Climb(IVec2 dir)) {
                    if (currentNode.connection.Value.next.connection?.type is ConnectionType.Walk) {
                        connections.Add(new ConnectionType.GrabPole());
                    } else if (nextConnection is ConnectionType.Walk) {
                        if (dir.y == 1) {
                            connections.Add(new ConnectionType.Drop(new IgnoreList()));
                            var abovePos = currentNode.gridPos;
                            abovePos.y += 1;
                            if (staticGraph.GetNode(abovePos)?.verticalBeam == true) {
                                nodes.Add(abovePos);
                            } else {
                                Plugin.Logger!.LogError("climbing up from a pole onto a platform is currently only supported when there a two poles above the platform");
                                return;
                            }
                            connections.Add(new ConnectionType.Climb(new IVec2(0, 1)));
                            nodes.Add(currentNode.gridPos);
                            connections.Add(previousConnection);
                        } else if (dir.y == -1) {
                            connections.Add(new ConnectionType.Drop(new IgnoreList()));
                        } else {
                            Plugin.Logger!.LogError("invalid path: climb connection leading to walk connection");
                            return;
                        }
                    } else {
                        connections.Add(previousConnection);
                    }
                } else {
                    connections.Add(previousConnection);
                }
            }
            currentNode = currentNode.connection?.next;
        }
        cursor = nodes.Count - 1;
    }

    public int NodeCount => nodes.Count;
    public int ConnectionCount => connections.Count;

    public IVec2? CurrentNode() {
        if (cursor < 0) {
            return null;
        }
        return nodes[cursor];
    }
    public IVec2? PeekNode(int offset) {
        if (cursor - offset < 0) {
            return null;
        }
        return nodes[cursor - offset];
    }
    public ConnectionType? CurrentConnection() {
        if (cursor < 1) {
            return null;
        }
        return connections[cursor - 1];
    }
    public ConnectionType? PeekConnection(int offset) {
        if (cursor - offset < 1) {
            return null;
        }
        return connections[cursor - offset - 1];
    }
    public void Advance() {
        cursor -= 1;
    }
}

public class PathNode {
    public IVec2 gridPos;
    public PathConnection? connection;
    public float pathCost;

    public float Heuristic { get; private set; }
    public float FCost => pathCost + Heuristic;

    public PathNode(int x, int y) {
        gridPos = new IVec2(x, y);
        pathCost = 0;
        Heuristic = 0;
    }

    public void Reset(IVec2 destination, PathConnection? connection, float cost) {
        pathCost = cost;
        this.connection = connection;
        IVec2 distance = gridPos - destination;
        Heuristic = Mathf.Sqrt(
            distance.x * distance.x
            + distance.y * distance.y
        );
    }
}

public struct PathConnection {
    public ConnectionType type;
    public PathNode next;
    public PathConnection(ConnectionType type, PathNode next) {
        this.type = type;
        this.next = next;
    }
}

public class Node {
    public NodeType type;
    public bool verticalBeam;
    public bool horizontalBeam;
    public bool hasPlatform;
    public List<NodeConnection> connections;
    public IVec2 gridPos;

    public Node(NodeType type, int x, int y) {
        this.type = type;
        gridPos = new IVec2(x, y);
        connections = new();
    }

    public bool HasBeam => verticalBeam || horizontalBeam;
}

public record NodeType {
    public record Air() : NodeType();
    public record Floor() : NodeType();
    public record Slope() : NodeType();
    public record Corridor() : NodeType();
    public record ShortcutEntrance(int Index) : NodeType();
    public record Wall(int Direction) : NodeType();

    private NodeType() { }
}

public class NodeConnection {
    public ConnectionType type;
    public Node next;
    public float weight;

    public NodeConnection(ConnectionType type, Node next, float weight = 1f) {
        if (next is null) {
            throw new NoNullAllowedException();
        }
        this.next = next;
        this.weight = weight;
        this.type = type;
    }
}

public class IgnoreList {
    private int cursor;
    private List<IVec2> ignoreList;

    public IgnoreList() {
        cursor = 0;
        ignoreList = new();
    }

    // builder like thing because it seems silly to implement IEnumerable only for the collection initialiser
    public void Add(IVec2 node) {
        ignoreList.Add(node);
    }

    public bool ShouldIgnore(IVec2 node) {
        while (cursor < ignoreList.Count) {
            if (node == ignoreList[cursor]) {
                return true;
            }
            cursor++;
        }
        return false;
    }

    public IgnoreList Clone() {
        var cloneList = new List<IVec2>();
        cloneList.AddRange(ignoreList);
        var clone = new IgnoreList() {
            cursor = cursor,
            ignoreList = cloneList,
        };
        return clone;
    }
}

public record ConnectionType {
    public record Walk(int Direction) : ConnectionType();
    public record Climb(IVec2 Direction) : ConnectionType();
    public record Crawl(IVec2 Direction) : ConnectionType();
    public record GrabPole() : ConnectionType();
    public record Jump(int Direction) : ConnectionType();
    public record WalkOffEdge(int Direction) : ConnectionType();
    public record Pounce(int Direction) : ConnectionType();
    public record Shortcut() : ConnectionType();
    public record Drop(IgnoreList ignoreList) : ConnectionType();

    private ConnectionType() { }
}

/// <summary>
/// Stores nodes and connections for a given room.
/// The connections are not dependent on slugcat specific data,
/// slugcat-specific connections like jumps should be stored in <see cref="DynamicGraph">DynamicGraphs</see> instead.
/// </summary>
public class SharedGraph {
    public Node?[,] nodes;
    public int width;
    public int height;

    public SharedGraph(Room room) {
        width = room.Tiles.GetLength(0);
        height = room.Tiles.GetLength(1);
        nodes = new Node[width, height];
        GenerateNodes(room);
        GenerateConnections(room);
    }

    public Node? GetNode(int x, int y) {
        if (nodes is null || x < 0 || y < 0 || x >= width || y >= height) {
            return null;
        }
        return nodes[x, y];
    }

    public Node? GetNode(IVec2 pos) {
        return GetNode(pos.x, pos.y);
    }

    public Node? CurrentNode(Player player) {
        IVec2 pos = RoomHelper.TilePosition(player.bodyChunks[0].pos);
        if (player.bodyMode == Player.BodyModeIndex.Stand
            || player.animation == Player.AnimationIndex.StandOnBeam
        ) {
            return GetNode(pos.x, pos.y - 1) is Node node ? node : GetNode(pos.x, pos.y - 2);
        } else if (player.bodyMode == Player.BodyModeIndex.Crawl) {
            return GetNode(pos) is Node node ? node : GetNode(pos.x, pos.y - 1);
        }
        return GetNode(pos);
    }

    private void GenerateNodes(Room room) {
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                if (x - 1 < 0 || x + 1 >= width || y - 1 < 0 || y + 1 >= height) {
                    continue;
                }
                if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Solid) {
                    continue;
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Floor) {
                    if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid) {
                        nodes[x, y] = new Node(new NodeType.Corridor(), x, y) {
                            hasPlatform = true,
                        };
                    }
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Air) {
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
                            && room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Air)
                    ) {
                        nodes[x, y] = new Node(new NodeType.Corridor(), x, y);
                    } else if (
                          room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Solid
                          || room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.ShortcutEntrance
                          // pretend invalid slope is solid
                          || room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Slope
                          && room.Tiles[x - 1, y - 1].Terrain == Room.Tile.TerrainType.Solid
                          && room.Tiles[x + 1, y - 1].Terrain == Room.Tile.TerrainType.Solid
                    ) {
                        nodes[x, y] = new Node(new NodeType.Floor(), x, y);
                    } else if (room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Floor) {
                        nodes[x, y] = new Node(new NodeType.Floor(), x, y);
                    } else if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Air
                          && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid) {
                        nodes[x, y] = new Node(new NodeType.Wall(1), x, y);
                    } else if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid
                          && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Air) {
                        nodes[x, y] = new Node(new NodeType.Wall(-1), x, y);
                    }
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Slope
                      && room.Tiles[x, y + 1].Terrain == Room.Tile.TerrainType.Air
                      && !(room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid
                          && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid)) {
                    nodes[x, y] = new Node(new NodeType.Slope(), x, y);
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.ShortcutEntrance) {
                    int index = Array.IndexOf(room.shortcutsIndex, new IVec2(x, y));
                    if (index > -1 && room.shortcuts[index].shortCutType == ShortcutData.Type.Normal) {
                        nodes[x, y] = new Node(new NodeType.ShortcutEntrance(index), x, y);
                    }
                }

                if (room.Tiles[x, y].verticalBeam) {
                    if (nodes[x, y] is null) {
                        nodes[x, y] = new Node(new NodeType.Air(), x, y);
                    }
                    nodes[x, y]!.verticalBeam = true;
                }

                if (room.Tiles[x, y].horizontalBeam) {
                    if (nodes[x, y] is null) {
                        nodes[x, y] = new Node(new NodeType.Air(), x, y);
                    }
                    nodes[x, y]!.horizontalBeam = true;
                }
            }
        }
    }

    private void GenerateConnections(Room room) {
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                if (nodes[x, y] is null) {
                    continue;
                }
                if (nodes[x, y]!.type is NodeType.Floor) {
                    if (GetNode(x + 1, y)?.type is NodeType.Floor or NodeType.Slope) {
                        ConnectNodes(
                            nodes[x, y]!,
                            nodes[x + 1, y]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    } else if (GetNode(x + 1, y)?.type is NodeType.Corridor) {
                        ConnectNodes(
                            nodes[x, y]!,
                            nodes[x + 1, y]!,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    }
                    if (GetNode(x + 1, y - 1)?.type is NodeType.Slope) {
                        ConnectNodes(
                            nodes[x, y]!,
                            nodes[x + 1, y - 1]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    }
                    if (GetNode(x + 1, y + 1)?.type is NodeType.Corridor) {
                        nodes[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Walk(1), nodes[x + 1, y + 1]!));
                    }
                    if (GetNode(x, y + 1)?.HasBeam == true) {
                        nodes[x, y]!.connections.Add(new NodeConnection(new ConnectionType.GrabPole(), nodes[x, y + 1]!, 1.5f));
                    }
                }

                if (nodes[x, y]!.type is NodeType.Slope) {
                    if (GetNode(x + 1, y)?.type is NodeType.Floor or NodeType.Slope) {
                        ConnectNodes(
                            nodes[x, y]!,
                            nodes[x + 1, y]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    } else if (GetNode(x + 1, y - 1)?.type is NodeType.Slope) {
                        ConnectNodes(
                            nodes[x, y]!,
                            nodes[x + 1, y - 1]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    } else if (GetNode(x + 1, y + 1)?.type is NodeType.Slope or NodeType.Floor) {
                        ConnectNodes(
                            nodes[x, y]!,
                            nodes[x + 1, y + 1]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    }
                    if (GetNode(x, y + 1)?.HasBeam == true) {
                        nodes[x, y]!.connections.Add(new NodeConnection(new ConnectionType.GrabPole(), nodes[x, y + 1]!, 1.5f));
                    }
                }

                var rightNode = GetNode(x + 1, y);
                var aboveNode = GetNode(x, y + 1);
                if (nodes[x, y]!.type is NodeType.Corridor) {
                    if (rightNode?.type is NodeType.Corridor or NodeType.Floor or NodeType.ShortcutEntrance) {
                        ConnectNodes(
                            nodes[x, y]!,
                            rightNode,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    } else if (rightNode?.horizontalBeam == true) {
                        ConnectNodes(
                            nodes[x, y]!,
                            rightNode,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            rightNode.horizontalBeam
                                ? new ConnectionType.Climb(new IVec2(-1, 0))
                                : new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    }
                    if (GetNode(x + 1, y - 1)?.type is NodeType.Floor) {
                        nodes[x + 1, y - 1]!.connections.Add(new NodeConnection(new ConnectionType.Walk(-1), nodes[x, y]!));
                    }
                    if (aboveNode?.type is NodeType.Corridor or NodeType.Floor or NodeType.ShortcutEntrance) {
                        ConnectNodes(
                            nodes[x, y]!,
                            aboveNode,
                            new ConnectionType.Crawl(new IVec2(0, 1)),
                            new ConnectionType.Crawl(new IVec2(0, -1))
                        );
                    }
                } else {
                    if (nodes[x, y]!.horizontalBeam
                        && rightNode?.horizontalBeam == true
                    ) {
                        ConnectNodes(
                            nodes[x, y]!,
                            nodes[x + 1, y]!,
                            new ConnectionType.Climb(new IVec2(1, 0)),
                            rightNode.type is NodeType.Corridor
                                ? new ConnectionType.Crawl(new IVec2(-1, 0))
                                : new ConnectionType.Climb(new IVec2(-1, 0))
                        );
                    }
                    if (nodes[x, y]!.verticalBeam
                        && aboveNode?.verticalBeam == true
                    ) {
                        ConnectNodes(
                            nodes[x, y]!,
                            aboveNode,
                            new ConnectionType.Climb(new IVec2(0, 1)),
                            aboveNode.type is NodeType.Corridor
                                ? new ConnectionType.Crawl(new IVec2(0, -1))
                                : new ConnectionType.Climb(new IVec2(0, -1))
                        );

                    }
                }
                if (nodes[x, y]!.type is NodeType.ShortcutEntrance) {
                    var entrance = nodes[x, y]!.type as NodeType.ShortcutEntrance;
                    var shortcutData = room.shortcuts[entrance!.Index];
                    var destNode = nodes[shortcutData.destinationCoord.x, shortcutData.destinationCoord.y];
                    if (destNode is null || destNode.type is not NodeType.ShortcutEntrance) {
                        Plugin.Logger!.LogError($"Shortcut entrance has no valid exit, pos: ({x}, {y}), index: {entrance.Index}");
                        return;
                    }
                    nodes[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Shortcut(), destNode, shortcutData.length));
                    if (GetNode(x + 1, y)?.type is NodeType.Corridor) {
                        ConnectNodes(
                            nodes[x, y]!,
                            nodes[x + 1, y]!,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    }
                    if (GetNode(x, y + 1)?.type is NodeType.Corridor) {
                        ConnectNodes(
                            nodes[x, y]!,
                            nodes[x, y + 1]!,
                            new ConnectionType.Crawl(new IVec2(0, 1)),
                            new ConnectionType.Crawl(new IVec2(0, -1))
                        );
                    }
                }
            }
        }
    }

    private void ConnectNodes(Node start, Node end, ConnectionType startToEndType, ConnectionType endToStartType, float weight = 1f) {
        start.connections.Add(new NodeConnection(startToEndType, end, weight));
        end.connections.Add(new NodeConnection(endToStartType, start, weight));
    }
}

public class DynamicGraph {
    private Room room;
    public List<NodeConnection>[,] adjacencyLists;
    public int width;
    public int height;

    public DynamicGraph(Room room) {
        this.room = room;
        var sharedGraph = room.GetCWT().sharedGraph!;
        width = sharedGraph.width;
        height = sharedGraph.height;
        adjacencyLists = new List<NodeConnection>[width, height];
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                if (sharedGraph.nodes[x, y] is not null) {
                    adjacencyLists[x, y] = new();
                }
            }
        }
    }

    public void NewRoom(Room room) {
        if (room != this.room) {
            this.room = room;
            var sharedGraph = room.GetCWT().sharedGraph!;
            width = sharedGraph.width;
            height = sharedGraph.height;
            adjacencyLists = new List<NodeConnection>[width, height];
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    if (sharedGraph.nodes[x, y] is not null) {
                        adjacencyLists[x, y] = new();
                    }
                }
            }
        }
    }

    public void TraceFromNode(IVec2 pos, SlugcatDescriptor slugcat) {
        if (Timers.active) {
            Timers.traceFromNode.Start();
        }
        var sharedGraph = room.GetCWT().sharedGraph!;
        var graphNode = sharedGraph.GetNode(pos);
        if (graphNode is null) {
            if (Timers.active) {
                Timers.traceFromNode.Stop();
            }
            return;
        }
        bool goLeft = true;
        bool goRight = true;
        if (graphNode.type is NodeType.Wall(int direction)) {
            if (direction == -1) {
                goLeft = false;
            } else {
                goRight = false;
            }
        }
        if (sharedGraph.GetNode(pos.x, pos.y - 1)?.type is NodeType.Wall footWall) {
            if (footWall.Direction == -1) {
                goLeft = false;
            } else {
                goRight = false;
            }
        }
        if (graphNode.verticalBeam && !graphNode.horizontalBeam
            && graphNode.type is not (NodeType.Corridor or NodeType.Floor or NodeType.Slope)
            && sharedGraph.GetNode(pos.x, pos.y - 1)?.type is not (NodeType.Corridor or NodeType.Floor or NodeType.Slope)) {
            Vector2 v0 = slugcat.VerticalPoleJumpVector();
            if (goRight) {
                TraceJump(pos, pos, v0, new ConnectionType.Jump(1));
            }
            if (goLeft) {
                v0.x = -v0.x;
                TraceJump(pos, pos, v0, new ConnectionType.Jump(-1));
            }
            TraceDrop(pos.x, pos.y);
        }
        if (graphNode.horizontalBeam && graphNode.type is not (NodeType.Corridor or NodeType.Floor or NodeType.Slope)) {
            var headPos = new IVec2(pos.x, pos.y + 1);
            var v0 = slugcat.HorizontalPoleJumpVector();
            if (goRight) {
                TraceJump(pos, headPos, v0, new ConnectionType.Jump(1));
            }
            if (goLeft) {
                v0.x = -v0.x;
                TraceJump(pos, headPos, v0, new ConnectionType.Jump(-1));
            }
            TraceDrop(pos.x, pos.y);
        }
        if (graphNode.type is NodeType.Floor) {
            var headPos = new IVec2(pos.x, pos.y + 1);
            var v0 = slugcat.FloorJumpVector();
            if (goRight) {
                TraceJump(pos, headPos, v0, new ConnectionType.Jump(1));
                if (sharedGraph.GetNode(pos.x + 1, pos.y - 1)?.type is NodeType.Wall) {
                    v0.y = 0f;
                    TraceJump(pos, headPos, v0, new ConnectionType.WalkOffEdge(1));
                }
            }
            if (goLeft) {
                v0.x = -v0.x;
                TraceJump(pos, headPos, v0, new ConnectionType.Jump(-1));
                if (sharedGraph.GetNode(pos.x - 1, pos.y - 1)?.type is NodeType.Wall) {
                    v0.y = 0f;
                    TraceJump(pos, headPos, v0, new ConnectionType.WalkOffEdge(-1));
                }
            }

        } else if (graphNode.type is NodeType.Corridor) {
            var v0 = slugcat.HorizontalCorridorFallVector();
            // v0.x might be too large
            if (sharedGraph.GetNode(pos.x + 1, pos.y) is null) {
                TraceJump(pos, pos, v0, new ConnectionType.WalkOffEdge(1), upright: false);
            }
            if (sharedGraph.GetNode(pos.x - 1, pos.y) is null) {
                v0.x = -v0.x;
                TraceJump(pos, pos, v0, new ConnectionType.WalkOffEdge(-1), upright: false);
            }
            if (sharedGraph.GetNode(pos.x, pos.y - 1) is null
                && room.Tiles[pos.x, pos.y - 1].Terrain == Room.Tile.TerrainType.Air
            ) {
                TraceDrop(pos.x, pos.y);
            }
        } else if (graphNode.type is NodeType.Wall jumpWall) {
            var v0 = slugcat.WallJumpVector(jumpWall.Direction);
            TraceJump(pos, pos, v0, new ConnectionType.Jump(-jumpWall.Direction));
        }
        if (Timers.active) {
            Timers.traceFromNode.Stop();
        }
    }

    public static float Parabola(float yOffset, Vector2 v0, float g, float t) => v0.y * t - 0.5f * g * t * t + yOffset;

    /// <summary>
    /// Trace parabolic trajectory through the shared graph and adds any connections to the dynamic graph.
    /// </summary>
    private void TraceJump(
        IVec2 startPos,
        IVec2 headPos,
        Vector2 v0,
        ConnectionType type,
        bool upright = true
    ) {
        int x = headPos.x;
        int y = headPos.y;
        var staticGraph = room.GetCWT().sharedGraph;
        if (x < 0 || y < 0 || x >= width || y >= height || staticGraph is null) {
            return;
        }
        int direction = v0.x switch {
            > 0 => 1,
            < 0 => -1,
            0 or float.NaN => throw new ArgumentOutOfRangeException(),
        };
        int xOffset = (direction + 1) / 2;
        var pathOffset = RoomHelper.MiddleOfTile(headPos);

        var startConnectionList = adjacencyLists[startPos.x, startPos.y];
        while (true) {
            float t = (20 * (x + xOffset) - pathOffset.x) / v0.x;
            float result = Parabola(pathOffset.y, v0, room.gravity, t) / 20;
            if (result > y + 1) {
                y++;
            } else if (result < y) {
                if (y - 2 < 0) {
                    break;
                }
                var currentNode = staticGraph.nodes[x, upright ? y - 1 : y];
                if (currentNode?.type is NodeType.Floor or NodeType.Slope) {
                    startConnectionList.Add(new NodeConnection(type, currentNode, t * 20 / 4.2f + 1));
                }
                if (room.Tiles[x, upright ? y - 2 : y - 1].Terrain == Room.Tile.TerrainType.Solid) {
                    break;
                }
                y--;
            } else {
                x += direction;
            }

            if (x < 0 || y < 0 || x >= width || y >= height
                || room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Solid
                || room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Slope) {
                break;
            }

            var shiftedNode = staticGraph.nodes[x, y];
            if (shiftedNode is null) {
                continue;
            }
            if (shiftedNode.type is NodeType.Corridor) {
                break;
            }
            if (shiftedNode.type is NodeType.Wall wall && wall.Direction == direction) {
                startConnectionList.Add(new NodeConnection(type, shiftedNode, t * 4.2f / 20));
                break;
            } else if (shiftedNode.verticalBeam) {
                float poleResult = Parabola(pathOffset.y, v0, room.gravity, (20 * x + 10 - pathOffset.x) / v0.x) / 20;
                if (poleResult > y && poleResult < y + 1) {
                    startConnectionList.Add(new NodeConnection(type, shiftedNode, t * 4.2f / 20 + 5));
                }
            } else if (shiftedNode.horizontalBeam) {
                startConnectionList.Add(new NodeConnection(type, shiftedNode, t * 4.2f / 20 + 10));
            }
        }
    }

    private void TraceDrop(int x, int y) {
        var sharedGraph = room.GetCWT().sharedGraph!;
        if (sharedGraph.GetNode(x, y)?.type is NodeType.Floor or NodeType.Slope
            || y >= sharedGraph.height
        ) {
            return;
        }
        var adjacencyList = adjacencyLists[x, y]!;
        var ignoreList = new IgnoreList();
        for (int i = y - 1; i >= 0; i--) {
            if (sharedGraph.nodes[x, i] is null) {
                continue;
            }
            var currentNode = sharedGraph.nodes[x, i]!;
            if (sharedGraph.nodes[x, i]!.type is NodeType.Floor) {
                // t = sqrt(2 * d / g)
                // weight might have inaccurate units
                adjacencyList.Add(
                    new NodeConnection(
                        new ConnectionType.Drop(ignoreList),
                        currentNode,
                        Mathf.Sqrt(2 * 20 * (y - i) / room.gravity) * 4.2f / 20
                    )
                );
                if (sharedGraph.GetNode(x, i - 1)?.hasPlatform == false) {
                    break;
                }
            } else if (currentNode.type is NodeType.Slope) {
                adjacencyList.Add(
                    new NodeConnection(
                        new ConnectionType.Drop(ignoreList),
                        currentNode,
                        Mathf.Sqrt(2 * 20 * (y - i) / room.gravity) * 4.2f / 20
                    )
                );
            } else if (currentNode.horizontalBeam) {
                adjacencyList.Add(
                    new NodeConnection(
                        new ConnectionType.Drop(ignoreList.Clone()),
                        currentNode,
                        Mathf.Sqrt(2 * 20 * (y - i) / room.gravity)
                    )
                );
                ignoreList.Add(new IVec2(x, i));
            } else {
                ignoreList.Add(new IVec2(x, i));
            }
        }
    }
}

public readonly struct SlugcatDescriptor {
    public readonly bool isRivulet;
    public readonly bool isPup;
    public readonly float adrenaline;
    public readonly float runspeed;
    public SlugcatDescriptor(Player player) {
        isRivulet = player.isRivulet;
        isPup = player.isSlugpup;
        adrenaline = player.Adrenaline;
        runspeed = player.slugcatStats.runspeedFac;
    }

    public override readonly bool Equals(object obj) {
        return base.Equals(obj);
    }

    public override readonly int GetHashCode() {
        return base.GetHashCode();
    }

    public static bool operator ==(SlugcatDescriptor self, SlugcatDescriptor other) {
        return self.isRivulet == other.isRivulet
            && self.isPup == other.isPup
            && self.adrenaline == other.adrenaline
            && self.runspeed == other.runspeed;
    }

    public static bool operator !=(SlugcatDescriptor self, SlugcatDescriptor other) {
        return self.isRivulet != other.isRivulet
            || self.isPup != other.isPup
            || self.adrenaline != other.adrenaline
            || self.runspeed != other.runspeed;
    }

    public static float JumpBoost(float boost) {
        float t = Mathf.Ceil(boost / 1.5f);
        return 0.3f * ((boost - 0.5f) * t - 0.75f * t * t);
    }

    public readonly Vector2 FloorJumpVector() {
        return new Vector2(
            4.2f * runspeed * Mathf.Lerp(1, 1.5f, adrenaline),
            (isRivulet ? 6f : 4f) * Mathf.Lerp(1, 1.15f, adrenaline) + JumpBoost(isPup ? 7 : 8));
    }

    public readonly Vector2 HorizontalPoleJumpVector() {
        Vector2 v0;
        if (isRivulet) {
            v0 = new Vector2(9f, 9f) * Mathf.Lerp(1, 1.15f, adrenaline);
        } else if (isPup) {
            v0 = new Vector2(5f, 7f) * Mathf.Lerp(1, 1.15f, adrenaline);
        } else {
            v0 = new Vector2(6f, 8f) * Mathf.Lerp(1, 1.15f, adrenaline);
        }
        return v0;
    }

    public readonly Vector2 VerticalPoleJumpVector() {
        return new Vector2(
            4.2f * runspeed * Mathf.Lerp(1, 1.5f, adrenaline),
            (isRivulet ? 6f : 4f) * Mathf.Lerp(1, 1.15f, adrenaline) + JumpBoost(isPup ? 7 : 8)
        );
    }

    public readonly Vector2 HorizontalCorridorFallVector() {
        return new Vector2(
            4.2f * runspeed * Mathf.Lerp(1, 1.5f, adrenaline),
            0);
    }

    public readonly Vector2 WallJumpVector(int wallDirection) {
        Vector2 v0;
        if (isRivulet) {
            v0 = new Vector2(-wallDirection * 9, 10) * Mathf.Lerp(1, 1.15f, adrenaline);
        } else if (isPup) {
            v0 = new Vector2(-wallDirection * 5, 6) * Mathf.Lerp(1, 1.15f, adrenaline);
        } else {
            v0 = new Vector2(-wallDirection * 6, 8) * Mathf.Lerp(1, 1.15f, adrenaline);
        }
        return v0;
    }
}

public class BitGrid {
    private readonly BitArray array;

    public int Width { get; }
    public int Height { get; }

    public BitGrid(int width, int height) {
        Width = width;
        Height = height;
        array = new BitArray(width * height);
    }

    public bool this[int x, int y] {
        get => array[y * Width + x];
        set {
            array[y * Width + x] = value;
        }
    }

    public bool this[IVec2 pos] {
        get => array[pos.y * Width + pos.x];
        set {
            array[pos.y * Width + pos.x] = value;
        }
    }

    public void Reset() {
        array.SetAll(false);
    }
}

public struct PathNodePool {
    private readonly PathNode?[,] array;
    public readonly int Width => array.GetLength(0);
    public readonly int Height => array.GetLength(1);

    public PathNodePool(SharedGraph graph) {
        array = new PathNode[graph.width, graph.height];
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                if (graph.nodes[x, y] is not null) {
                    array[x, y] = new PathNode(x, y);
                }
            }
        }
    }

    public readonly PathNode? this[int x, int y] => array[x, y];
    public readonly PathNode? this[IVec2 pos] => array[pos.x, pos.y];
}

public class Pathfinder {
    private Room room;
    private SlugcatDescriptor lastDescriptor;
    private readonly DynamicGraph dynamicGraph;

    public Pathfinder(Room room, SlugcatDescriptor descriptor) {
        this.room = room;
        lastDescriptor = descriptor;
        dynamicGraph = new DynamicGraph(room);
    }

    public void NewRoom(Room room) {
        if (room != this.room) {
            this.room = room;
            dynamicGraph.NewRoom(room);
        }
    }

    public Path? FindPath(IVec2 start, IVec2 destination, SlugcatDescriptor descriptor) {
        var sharedGraph = room.GetCWT().sharedGraph!;
        if (sharedGraph.GetNode(start) is null) {
            Plugin.Logger!.LogDebug($"no node at start ({start.x}, {start.y})");
            lastDescriptor = descriptor;
            return null;
        }
        if (sharedGraph.GetNode(destination) is null) {
            Plugin.Logger!.LogDebug($"no node at destination ({destination.x}, {destination.y})");
            lastDescriptor = descriptor;
            return null;
        }
        if (Timers.active) {
            Timers.findPath.Start();
        }
        var pathNodePool = room.GetCWT().pathNodePool!.Value;
        var openNodes = room.GetCWT().openNodes!;
        openNodes.Reset();
        var closedNodes = room.GetCWT().closedNodes!;
        closedNodes.Reset();
        var startNode = pathNodePool[start]!;
        startNode.Reset(destination, null, 0);
        var nodeHeap = new List<PathNode> {
            startNode
        };
        openNodes[start.x, start.y] = true;
        while (nodeHeap.Count > 0) {
            PathNode currentNode = nodeHeap[0];
            var currentPos = currentNode.gridPos;
            if (currentPos == destination) {
                if (Timers.active) {
                    Timers.findPath.Stop();
                }
                lastDescriptor = descriptor;
                return new Path(currentNode, sharedGraph);
            }

            nodeHeap[0] = nodeHeap[nodeHeap.Count - 1];
            nodeHeap.RemoveAt(nodeHeap.Count - 1);
            int index = 0;
            int leftIndex = 2 * index + 1;
            int rightIndex = 2 * index + 2;
            while (true) {
                if (rightIndex >= nodeHeap.Count) {
                    if (leftIndex >= nodeHeap.Count) {
                        break;
                    } else {
                        if (nodeHeap[leftIndex].FCost < nodeHeap[index].FCost) {
                            (nodeHeap[leftIndex], nodeHeap[index]) = (nodeHeap[index], nodeHeap[leftIndex]);
                            index = leftIndex;
                        } else {
                            break;
                        }
                    }
                } else {
                    if (nodeHeap[leftIndex].FCost < nodeHeap[index].FCost
                        || nodeHeap[rightIndex].FCost < nodeHeap[index].FCost
                    ) {
                        if (nodeHeap[leftIndex].FCost < nodeHeap[rightIndex].FCost) {
                            (nodeHeap[leftIndex], nodeHeap[index]) = (nodeHeap[index], nodeHeap[leftIndex]);
                            index = leftIndex;
                        } else {
                            (nodeHeap[rightIndex], nodeHeap[index]) = (nodeHeap[index], nodeHeap[rightIndex]);
                            index = rightIndex;
                        }
                    } else {
                        break;
                    }
                }
                leftIndex = 2 * index + 1;
                rightIndex = 2 * index + 2;
            }
            openNodes[currentPos] = false;
            closedNodes[currentPos] = true;

            var graphNode = sharedGraph.nodes[currentPos.x, currentPos.y]!;
            var adjacencyList = dynamicGraph.adjacencyLists[currentPos.x, currentPos.y]!;

            if (adjacencyList.Count == 0) {
                dynamicGraph.TraceFromNode(currentPos, descriptor);
            } else if (lastDescriptor != descriptor) {
                adjacencyList.Clear();
                dynamicGraph.TraceFromNode(currentPos, descriptor);
            }

            void CheckConnection(NodeConnection connection) {
                IVec2 neighbourPos = connection.next.gridPos;
                PathNode currentNeighbour = pathNodePool[neighbourPos]!;
                if (closedNodes[neighbourPos]) {
                    return;
                }
                if (!openNodes[neighbourPos]) {
                    currentNeighbour.Reset(
                        destination,
                        new PathConnection(connection.type, currentNode),
                        currentNode.pathCost + connection.weight
                    );
                    nodeHeap.Add(currentNeighbour);
                    int index = nodeHeap.Count - 1;
                    while (index > 0 && currentNeighbour.FCost < nodeHeap[(index - 1) / 2].FCost) {
                        PathNode temp = nodeHeap[(index - 1) / 2];
                        nodeHeap[(index - 1) / 2] = currentNeighbour;
                        nodeHeap[index] = temp;
                        index = (index - 1) / 2;
                    }
                    openNodes[neighbourPos] = true;
                }
                if (currentNode.pathCost + connection.weight < currentNeighbour.pathCost) {
                    currentNeighbour.pathCost = currentNode.pathCost + connection.weight;
                    currentNeighbour.connection = new PathConnection(connection.type, currentNode);
                }
            }

            foreach (var connection in graphNode.connections) {
                CheckConnection(connection);
            }
            foreach (var connection in adjacencyList) {
                CheckConnection(connection);
            }
        }
        if (Timers.active) {
            Timers.findPath.Stop();
        }
        lastDescriptor = descriptor;
        return null;
    }
}

static class PathfinderHooks {
    public static void RegisterHooks() {
        On.Player.ctor += Player_ctor;
        On.Player.Update += Player_Update;
    }

    public static void UnregisterHooks() {
        On.Player.ctor -= Player_ctor;
        On.Player.Update -= Player_Update;
    }

    private static void Player_ctor(On.Player.orig_ctor orig, Player self, AbstractCreature abstractCreature, World world) {
        orig(self, abstractCreature, world);
    }

    private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu) {
        // horrible hack because creature creation is pain
        if (self.abstractCreature?.abstractAI is null) {
            var mousePos = (Vector2)Input.mousePosition + self.room.game.cameras[0].pos;
            if (InputHelper.JustPressedMouseButton(1)) {
                var scugTemplate = StaticWorld.GetCreatureTemplate(MoreSlugcatsEnums.CreatureTemplateType.SlugNPC);
                var abstractScug = new AbstractCreature(
                    self.room.world,
                    scugTemplate,
                    null,
                    self.room.ToWorldCoordinate(mousePos),
                    self.room.game.GetNewID());
                abstractScug.state = new PlayerNPCState(abstractScug, 0) {
                    forceFullGrown = true,
                };
                self.room.abstractRoom.AddEntity(abstractScug);
                abstractScug.RealizeInRoom();
                abstractScug.abstractAI = new JumpSlugAbstractAI(abstractScug, self.room.world) {
                    RealAI = new JumpSlugAI(abstractScug, self.room.world),
                };
                var realizedScug = (abstractScug.realizedCreature as Player)!;
                realizedScug.controller = null;
            }
        }
        orig(self, eu);
    }
}