using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;

using MoreSlugcats;

using UnityEngine;

using IVec2 = RWCustom.IntVector2;

namespace JumpSlug.Pathfinding;

public class Path {
    // these aren't private because it's more straightforward for the visualizer to use manual indexing
    // and the accessors can't be made const correct
    public int Cursor;
    public readonly List<IVec2> Nodes;
    public readonly List<ConnectionType> Connections;

    public int NodeCount => Nodes.Count;
    public int ConnectionCount => Connections.Count;

    public Path(PathNode startNode, SharedGraph staticGraph) {
        Nodes = new();
        Connections = new();
        var currentNode = startNode;
        while (currentNode is not null) {
            Nodes.Add(currentNode.GridPos);
            if (currentNode.Connection is not null) {
                // this is necessary because the pathfinder can't account for the slugcat taking up more than one tile
                // and the AI movement code can't account for missing sections of the path
                // any non-hacky way of dealing with this requires sweeping code changes that complicate everything
                var nextConnection = Connections.Count > 0 ? Connections[Connections.Count - 1] : null;
                var previousConnection = currentNode.Connection.Value.Type;
                if (previousConnection is ConnectionType.Climb(IVec2 dir)) {
                    if (currentNode.Connection.Value.Next.Connection?.Type is ConnectionType.Walk) {
                        Connections.Add(new ConnectionType.GrabPole());
                    } else if (nextConnection is ConnectionType.Walk) {
                        if (dir.y == 1) {
                            Connections.Add(new ConnectionType.Drop(new IgnoreList()));
                            var abovePos = currentNode.GridPos;
                            abovePos.y += 1;
                            if (staticGraph.GetNode(abovePos)?.VerticalBeam == true) {
                                Nodes.Add(abovePos);
                            } else {
                                Plugin.Logger!.LogError("climbing up from a pole onto a platform is currently only supported when there a two poles above the platform");
                                return;
                            }
                            Connections.Add(new ConnectionType.Climb(new IVec2(0, 1)));
                            Nodes.Add(currentNode.GridPos);
                            Connections.Add(previousConnection);
                        } else if (dir.y == -1) {
                            Connections.Add(new ConnectionType.Drop(new IgnoreList()));
                        } else {
                            Plugin.Logger!.LogError("invalid path: climb connection leading to walk connection");
                            return;
                        }
                    } else {
                        Connections.Add(previousConnection);
                    }
                } else {
                    Connections.Add(previousConnection);
                }
            }
            currentNode = currentNode.Connection?.Next;
        }
        Cursor = Nodes.Count - 1;
    }

    public IVec2? CurrentNode() {
        if (Cursor < 0) {
            return null;
        }
        return Nodes[Cursor];
    }
    public IVec2? PeekNode(int offset) {
        if (Cursor - offset < 0) {
            return null;
        }
        return Nodes[Cursor - offset];
    }
    public ConnectionType? CurrentConnection() {
        if (Cursor < 1) {
            return null;
        }
        return Connections[Cursor - 1];
    }
    public ConnectionType? PeekConnection(int offset) {
        if (Cursor - offset < 1) {
            return null;
        }
        return Connections[Cursor - offset - 1];
    }
    public void Advance() {
        Cursor -= 1;
    }
}

public class PathNode {
    public IVec2 GridPos { get; }
    public PathConnection? Connection;
    public float PathCost;

    public float Heuristic { get; private set; }
    public float FCost => PathCost + Heuristic;

    public PathNode(int x, int y) {
        GridPos = new IVec2(x, y);
        PathCost = 0;
        Heuristic = 0;
    }

    public void Reset(IVec2 destination, PathConnection? connection, float cost) {
        PathCost = cost;
        Connection = connection;
        IVec2 distance = GridPos - destination;
        Heuristic = Mathf.Sqrt(
            distance.x * distance.x
            + distance.y * distance.y
        );
    }
}

public struct PathConnection {
    public ConnectionType Type;
    public PathNode Next;
    public PathConnection(ConnectionType type, PathNode next) {
        Type = type;
        Next = next;
    }
}

public class Node {
    public NodeType Type;
    public bool VerticalBeam;
    public bool HorizontalBeam;
    public bool HasPlatform;
    public List<NodeConnection> Connections;
    public IVec2 GridPos;

    public Node(NodeType type, int x, int y) {
        Type = type;
        GridPos = new IVec2(x, y);
        Connections = new();
    }

    public bool HasBeam => VerticalBeam || HorizontalBeam;
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
    public ConnectionType Type;
    public Node Next;
    public float Weight;

    public NodeConnection(ConnectionType type, Node next, float weight = 1f) {
        if (next is null) {
            throw new NoNullAllowedException();
        }
        Next = next;
        Weight = weight;
        Type = type;
    }
}

public class IgnoreList {
    private int _cursor;
    private List<IVec2> _ignoreList;

    public IgnoreList() {
        _cursor = 0;
        _ignoreList = new();
    }

    public void Add(IVec2 node) {
        _ignoreList.Add(node);
    }

    public bool ShouldIgnore(IVec2 node) {
        while (_cursor < _ignoreList.Count) {
            if (node == _ignoreList[_cursor]) {
                return true;
            }
            _cursor++;
        }
        return false;
    }

    public IgnoreList Clone() {
        var cloneList = new List<IVec2>();
        cloneList.AddRange(_ignoreList);
        var clone = new IgnoreList() {
            _cursor = _cursor,
            _ignoreList = cloneList,
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
    public record Drop(IgnoreList IgnoreList) : ConnectionType();

    private ConnectionType() { }
}

/// <summary>
/// Stores nodes and connections for a given room.
/// The connections are not dependent on slugcat specific data,
/// slugcat-specific connections like jumps should be stored in <see cref="DynamicGraph">DynamicGraphs</see> instead.
/// </summary>
public class SharedGraph {
    public Node?[,] Nodes;
    public int Width;
    public int Height;

    public SharedGraph(Room room) {
        Width = room.Tiles.GetLength(0);
        Height = room.Tiles.GetLength(1);
        Nodes = new Node[Width, Height];
        GenerateNodes(room);
        GenerateConnections(room);
    }

    public Node? GetNode(int x, int y) {
        if (Nodes is null || x < 0 || y < 0 || x >= Width || y >= Height) {
            return null;
        }
        return Nodes[x, y];
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
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                if (x - 1 < 0 || x + 1 >= Width || y - 1 < 0 || y + 1 >= Height) {
                    continue;
                }
                if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Solid) {
                    continue;
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Floor) {
                    if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid) {
                        Nodes[x, y] = new Node(new NodeType.Corridor(), x, y) {
                            HasPlatform = true,
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
                        Nodes[x, y] = new Node(new NodeType.Corridor(), x, y);
                    } else if (
                          room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Solid
                          || room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.ShortcutEntrance
                          // pretend invalid slope is solid
                          || room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Slope
                          && room.Tiles[x - 1, y - 1].Terrain == Room.Tile.TerrainType.Solid
                          && room.Tiles[x + 1, y - 1].Terrain == Room.Tile.TerrainType.Solid
                    ) {
                        Nodes[x, y] = new Node(new NodeType.Floor(), x, y);
                    } else if (room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Floor) {
                        Nodes[x, y] = new Node(new NodeType.Floor(), x, y);
                    } else if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Air
                          && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid) {
                        Nodes[x, y] = new Node(new NodeType.Wall(1), x, y);
                    } else if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid
                          && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Air) {
                        Nodes[x, y] = new Node(new NodeType.Wall(-1), x, y);
                    }
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Slope
                      && room.Tiles[x, y + 1].Terrain == Room.Tile.TerrainType.Air
                      && !(room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid
                          && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid)) {
                    Nodes[x, y] = new Node(new NodeType.Slope(), x, y);
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.ShortcutEntrance) {
                    int index = Array.IndexOf(room.shortcutsIndex, new IVec2(x, y));
                    if (index > -1 && room.shortcuts[index].shortCutType == ShortcutData.Type.Normal) {
                        Nodes[x, y] = new Node(new NodeType.ShortcutEntrance(index), x, y);
                    }
                }

                if (room.Tiles[x, y].verticalBeam) {
                    if (Nodes[x, y] is null) {
                        Nodes[x, y] = new Node(new NodeType.Air(), x, y);
                    }
                    Nodes[x, y]!.VerticalBeam = true;
                }

                if (room.Tiles[x, y].horizontalBeam) {
                    if (Nodes[x, y] is null) {
                        Nodes[x, y] = new Node(new NodeType.Air(), x, y);
                    }
                    Nodes[x, y]!.HorizontalBeam = true;
                }
            }
        }
    }

    private void GenerateConnections(Room room) {
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                if (Nodes[x, y] is null) {
                    continue;
                }
                if (Nodes[x, y]!.Type is NodeType.Floor) {
                    if (GetNode(x + 1, y)?.Type is NodeType.Floor or NodeType.Slope) {
                        ConnectNodes(
                            Nodes[x, y]!,
                            Nodes[x + 1, y]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    } else if (GetNode(x + 1, y)?.Type is NodeType.Corridor) {
                        ConnectNodes(
                            Nodes[x, y]!,
                            Nodes[x + 1, y]!,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    }
                    if (GetNode(x + 1, y - 1)?.Type is NodeType.Slope) {
                        ConnectNodes(
                            Nodes[x, y]!,
                            Nodes[x + 1, y - 1]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    }
                    if (GetNode(x + 1, y + 1)?.Type is NodeType.Corridor) {
                        Nodes[x, y]!.Connections.Add(new NodeConnection(new ConnectionType.Walk(1), Nodes[x + 1, y + 1]!));
                    }
                    if (GetNode(x, y + 1)?.HasBeam == true) {
                        Nodes[x, y]!.Connections.Add(new NodeConnection(new ConnectionType.GrabPole(), Nodes[x, y + 1]!, 1.5f));
                    }
                }

                if (Nodes[x, y]!.Type is NodeType.Slope) {
                    if (GetNode(x + 1, y)?.Type is NodeType.Floor or NodeType.Slope) {
                        ConnectNodes(
                            Nodes[x, y]!,
                            Nodes[x + 1, y]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    } else if (GetNode(x + 1, y - 1)?.Type is NodeType.Slope) {
                        ConnectNodes(
                            Nodes[x, y]!,
                            Nodes[x + 1, y - 1]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    } else if (GetNode(x + 1, y + 1)?.Type is NodeType.Slope or NodeType.Floor) {
                        ConnectNodes(
                            Nodes[x, y]!,
                            Nodes[x + 1, y + 1]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    }
                    if (GetNode(x, y + 1)?.HasBeam == true) {
                        Nodes[x, y]!.Connections.Add(new NodeConnection(new ConnectionType.GrabPole(), Nodes[x, y + 1]!, 1.5f));
                    }
                }

                var rightNode = GetNode(x + 1, y);
                var aboveNode = GetNode(x, y + 1);
                if (Nodes[x, y]!.Type is NodeType.Corridor) {
                    if (rightNode?.Type is NodeType.Corridor or NodeType.Floor or NodeType.ShortcutEntrance) {
                        ConnectNodes(
                            Nodes[x, y]!,
                            rightNode,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    } else if (rightNode?.HorizontalBeam == true) {
                        ConnectNodes(
                            Nodes[x, y]!,
                            rightNode,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            rightNode.HorizontalBeam
                                ? new ConnectionType.Climb(new IVec2(-1, 0))
                                : new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    }
                    if (GetNode(x + 1, y - 1)?.Type is NodeType.Floor) {
                        Nodes[x + 1, y - 1]!.Connections.Add(new NodeConnection(new ConnectionType.Walk(-1), Nodes[x, y]!));
                    }
                    if (aboveNode?.Type is NodeType.Corridor or NodeType.Floor or NodeType.ShortcutEntrance) {
                        ConnectNodes(
                            Nodes[x, y]!,
                            aboveNode,
                            new ConnectionType.Crawl(new IVec2(0, 1)),
                            new ConnectionType.Crawl(new IVec2(0, -1))
                        );
                    }
                } else {
                    if (Nodes[x, y]!.HorizontalBeam
                        && rightNode?.HorizontalBeam == true
                    ) {
                        ConnectNodes(
                            Nodes[x, y]!,
                            Nodes[x + 1, y]!,
                            new ConnectionType.Climb(new IVec2(1, 0)),
                            rightNode.Type is NodeType.Corridor
                                ? new ConnectionType.Crawl(new IVec2(-1, 0))
                                : new ConnectionType.Climb(new IVec2(-1, 0))
                        );
                    }
                    if (Nodes[x, y]!.VerticalBeam
                        && aboveNode?.VerticalBeam == true
                    ) {
                        ConnectNodes(
                            Nodes[x, y]!,
                            aboveNode,
                            new ConnectionType.Climb(new IVec2(0, 1)),
                            aboveNode.Type is NodeType.Corridor
                                ? new ConnectionType.Crawl(new IVec2(0, -1))
                                : new ConnectionType.Climb(new IVec2(0, -1))
                        );

                    }
                }
                if (Nodes[x, y]!.Type is NodeType.ShortcutEntrance) {
                    var entrance = Nodes[x, y]!.Type as NodeType.ShortcutEntrance;
                    var shortcutData = room.shortcuts[entrance!.Index];
                    var destNode = Nodes[shortcutData.destinationCoord.x, shortcutData.destinationCoord.y];
                    if (destNode is null || destNode.Type is not NodeType.ShortcutEntrance) {
                        Plugin.Logger!.LogError($"Shortcut entrance has no valid exit, pos: ({x}, {y}), index: {entrance.Index}");
                        return;
                    }
                    Nodes[x, y]!.Connections.Add(new NodeConnection(new ConnectionType.Shortcut(), destNode, shortcutData.length));
                    if (GetNode(x + 1, y)?.Type is NodeType.Corridor) {
                        ConnectNodes(
                            Nodes[x, y]!,
                            Nodes[x + 1, y]!,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    }
                    if (GetNode(x, y + 1)?.Type is NodeType.Corridor) {
                        ConnectNodes(
                            Nodes[x, y]!,
                            Nodes[x, y + 1]!,
                            new ConnectionType.Crawl(new IVec2(0, 1)),
                            new ConnectionType.Crawl(new IVec2(0, -1))
                        );
                    }
                }
            }
        }
    }

    private void ConnectNodes(Node start, Node end, ConnectionType startToEndType, ConnectionType endToStartType, float weight = 1f) {
        start.Connections.Add(new NodeConnection(startToEndType, end, weight));
        end.Connections.Add(new NodeConnection(endToStartType, start, weight));
    }
}

public class DynamicGraph {
    private Room _room;

    public List<NodeConnection>[,] AdjacencyLists;
    public int Width;
    public int Height;

    public DynamicGraph(Room room) {
        _room = room;
        var sharedGraph = room.GetCWT().sharedGraph!;
        Width = sharedGraph.Width;
        Height = sharedGraph.Height;
        AdjacencyLists = new List<NodeConnection>[Width, Height];
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                if (sharedGraph.Nodes[x, y] is not null) {
                    AdjacencyLists[x, y] = new();
                }
            }
        }
    }

    public void NewRoom(Room room) {
        if (room != _room) {
            _room = room;
            var sharedGraph = room.GetCWT().sharedGraph!;
            Width = sharedGraph.Width;
            Height = sharedGraph.Height;
            AdjacencyLists = new List<NodeConnection>[Width, Height];
            for (int y = 0; y < Height; y++) {
                for (int x = 0; x < Width; x++) {
                    if (sharedGraph.Nodes[x, y] is not null) {
                        AdjacencyLists[x, y] = new();
                    }
                }
            }
        }
    }

    public void TraceFromNode(IVec2 pos, SlugcatDescriptor slugcat) {
        if (Timers.active) {
            Timers.traceFromNode.Start();
        }
        var sharedGraph = _room.GetCWT().sharedGraph!;
        var graphNode = sharedGraph.GetNode(pos);
        if (graphNode is null) {
            if (Timers.active) {
                Timers.traceFromNode.Stop();
            }
            return;
        }
        bool goLeft = true;
        bool goRight = true;
        if (graphNode.Type is NodeType.Wall(int direction)) {
            if (direction == -1) {
                goLeft = false;
            } else {
                goRight = false;
            }
        }
        if (sharedGraph.GetNode(pos.x, pos.y - 1)?.Type is NodeType.Wall footWall) {
            if (footWall.Direction == -1) {
                goLeft = false;
            } else {
                goRight = false;
            }
        }
        if (graphNode.VerticalBeam && !graphNode.HorizontalBeam
            && graphNode.Type is not (NodeType.Corridor or NodeType.Floor or NodeType.Slope)
            && sharedGraph.GetNode(pos.x, pos.y - 1)?.Type is not (NodeType.Corridor or NodeType.Floor or NodeType.Slope)) {
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
        if (graphNode.HorizontalBeam && graphNode.Type is not (NodeType.Corridor or NodeType.Floor or NodeType.Slope)) {
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
        if (graphNode.Type is NodeType.Floor) {
            var headPos = new IVec2(pos.x, pos.y + 1);
            var v0 = slugcat.FloorJumpVector();
            if (goRight) {
                TraceJump(pos, headPos, v0, new ConnectionType.Jump(1));
                if (sharedGraph.GetNode(pos.x + 1, pos.y - 1)?.Type is NodeType.Wall) {
                    v0.y = 0f;
                    TraceJump(pos, headPos, v0, new ConnectionType.WalkOffEdge(1));
                }
            }
            if (goLeft) {
                v0.x = -v0.x;
                TraceJump(pos, headPos, v0, new ConnectionType.Jump(-1));
                if (sharedGraph.GetNode(pos.x - 1, pos.y - 1)?.Type is NodeType.Wall) {
                    v0.y = 0f;
                    TraceJump(pos, headPos, v0, new ConnectionType.WalkOffEdge(-1));
                }
            }

        } else if (graphNode.Type is NodeType.Corridor) {
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
                && _room.Tiles[pos.x, pos.y - 1].Terrain == Room.Tile.TerrainType.Air
            ) {
                TraceDrop(pos.x, pos.y);
            }
        } else if (graphNode.Type is NodeType.Wall jumpWall) {
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
        var staticGraph = _room.GetCWT().sharedGraph;
        if (x < 0 || y < 0 || x >= Width || y >= Height || staticGraph is null) {
            return;
        }
        int direction = v0.x switch {
            > 0 => 1,
            < 0 => -1,
            0 or float.NaN => throw new ArgumentOutOfRangeException(),
        };
        int xOffset = (direction + 1) / 2;
        var pathOffset = RoomHelper.MiddleOfTile(headPos);

        var startConnectionList = AdjacencyLists[startPos.x, startPos.y];
        while (true) {
            float t = (20 * (x + xOffset) - pathOffset.x) / v0.x;
            float result = Parabola(pathOffset.y, v0, _room.gravity, t) / 20;
            if (result > y + 1) {
                y++;
            } else if (result < y) {
                if (y - 2 < 0) {
                    break;
                }
                var currentNode = staticGraph.Nodes[x, upright ? y - 1 : y];
                if (currentNode?.Type is NodeType.Floor or NodeType.Slope) {
                    startConnectionList.Add(new NodeConnection(type, currentNode, t * 20 / 4.2f + 1));
                }
                if (_room.Tiles[x, upright ? y - 2 : y - 1].Terrain == Room.Tile.TerrainType.Solid) {
                    break;
                }
                y--;
            } else {
                x += direction;
            }

            if (x < 0 || y < 0 || x >= Width || y >= Height
                || _room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Solid
                || _room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Slope) {
                break;
            }

            var shiftedNode = staticGraph.Nodes[x, y];
            if (shiftedNode is null) {
                continue;
            }
            if (shiftedNode.Type is NodeType.Corridor) {
                break;
            }
            if (shiftedNode.Type is NodeType.Wall wall && wall.Direction == direction) {
                startConnectionList.Add(new NodeConnection(type, shiftedNode, t * 4.2f / 20));
                break;
            } else if (shiftedNode.VerticalBeam) {
                float poleResult = Parabola(pathOffset.y, v0, _room.gravity, (20 * x + 10 - pathOffset.x) / v0.x) / 20;
                if (poleResult > y && poleResult < y + 1) {
                    startConnectionList.Add(new NodeConnection(type, shiftedNode, t * 4.2f / 20 + 5));
                }
            } else if (shiftedNode.HorizontalBeam) {
                startConnectionList.Add(new NodeConnection(type, shiftedNode, t * 4.2f / 20 + 10));
            }
        }
    }

    private void TraceDrop(int x, int y) {
        var sharedGraph = _room.GetCWT().sharedGraph!;
        if (sharedGraph.GetNode(x, y)?.Type is NodeType.Floor or NodeType.Slope
            || y >= sharedGraph.Height
        ) {
            return;
        }
        var adjacencyList = AdjacencyLists[x, y]!;
        var ignoreList = new IgnoreList();
        for (int i = y - 1; i >= 0; i--) {
            if (sharedGraph.Nodes[x, i] is null) {
                continue;
            }
            var currentNode = sharedGraph.Nodes[x, i]!;
            if (sharedGraph.Nodes[x, i]!.Type is NodeType.Floor) {
                // t = sqrt(2 * d / g)
                // weight might have inaccurate units
                adjacencyList.Add(
                    new NodeConnection(
                        new ConnectionType.Drop(ignoreList),
                        currentNode,
                        Mathf.Sqrt(2 * 20 * (y - i) / _room.gravity) * 4.2f / 20
                    )
                );
                if (sharedGraph.GetNode(x, i - 1)?.HasPlatform == false) {
                    break;
                }
            } else if (currentNode.Type is NodeType.Slope) {
                adjacencyList.Add(
                    new NodeConnection(
                        new ConnectionType.Drop(ignoreList),
                        currentNode,
                        Mathf.Sqrt(2 * 20 * (y - i) / _room.gravity) * 4.2f / 20
                    )
                );
            } else if (currentNode.HorizontalBeam) {
                adjacencyList.Add(
                    new NodeConnection(
                        new ConnectionType.Drop(ignoreList.Clone()),
                        currentNode,
                        Mathf.Sqrt(2 * 20 * (y - i) / _room.gravity)
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
    public readonly bool IsRivulet;
    public readonly bool IsPup;
    public readonly float Adrenaline;
    public readonly float Runspeed;
    public SlugcatDescriptor(Player player) {
        IsRivulet = player.isRivulet;
        IsPup = player.isSlugpup;
        Adrenaline = player.Adrenaline;
        Runspeed = player.slugcatStats.runspeedFac;
    }

    public override readonly bool Equals(object obj) {
        return base.Equals(obj);
    }

    public override readonly int GetHashCode() {
        return base.GetHashCode();
    }

    public static bool operator ==(SlugcatDescriptor self, SlugcatDescriptor other) {
        return self.IsRivulet == other.IsRivulet
            && self.IsPup == other.IsPup
            && self.Adrenaline == other.Adrenaline
            && self.Runspeed == other.Runspeed;
    }

    public static bool operator !=(SlugcatDescriptor self, SlugcatDescriptor other) {
        return self.IsRivulet != other.IsRivulet
            || self.IsPup != other.IsPup
            || self.Adrenaline != other.Adrenaline
            || self.Runspeed != other.Runspeed;
    }

    public static float JumpBoost(float boost) {
        float t = Mathf.Ceil(boost / 1.5f);
        return 0.3f * ((boost - 0.5f) * t - 0.75f * t * t);
    }

    public readonly Vector2 FloorJumpVector() {
        return new Vector2(
            4.2f * Runspeed * Mathf.Lerp(1, 1.5f, Adrenaline),
            (IsRivulet ? 6f : 4f) * Mathf.Lerp(1, 1.15f, Adrenaline) + JumpBoost(IsPup ? 7 : 8));
    }

    public readonly Vector2 HorizontalPoleJumpVector() {
        Vector2 v0;
        if (IsRivulet) {
            v0 = new Vector2(9f, 9f) * Mathf.Lerp(1, 1.15f, Adrenaline);
        } else if (IsPup) {
            v0 = new Vector2(5f, 7f) * Mathf.Lerp(1, 1.15f, Adrenaline);
        } else {
            v0 = new Vector2(6f, 8f) * Mathf.Lerp(1, 1.15f, Adrenaline);
        }
        return v0;
    }

    public readonly Vector2 VerticalPoleJumpVector() {
        return new Vector2(
            4.2f * Runspeed * Mathf.Lerp(1, 1.5f, Adrenaline),
            (IsRivulet ? 6f : 4f) * Mathf.Lerp(1, 1.15f, Adrenaline) + JumpBoost(IsPup ? 7 : 8)
        );
    }

    public readonly Vector2 HorizontalCorridorFallVector() {
        return new Vector2(
            4.2f * Runspeed * Mathf.Lerp(1, 1.5f, Adrenaline),
            0);
    }

    public readonly Vector2 WallJumpVector(int wallDirection) {
        Vector2 v0;
        if (IsRivulet) {
            v0 = new Vector2(-wallDirection * 9, 10) * Mathf.Lerp(1, 1.15f, Adrenaline);
        } else if (IsPup) {
            v0 = new Vector2(-wallDirection * 5, 6) * Mathf.Lerp(1, 1.15f, Adrenaline);
        } else {
            v0 = new Vector2(-wallDirection * 6, 8) * Mathf.Lerp(1, 1.15f, Adrenaline);
        }
        return v0;
    }
}

public class BitGrid {
    private readonly BitArray _array;

    public int Width { get; }
    public int Height { get; }

    public BitGrid(int width, int height) {
        Width = width;
        Height = height;
        _array = new BitArray(width * height);
    }

    public bool this[int x, int y] {
        get => _array[y * Width + x];
        set {
            _array[y * Width + x] = value;
        }
    }

    public bool this[IVec2 pos] {
        get => _array[pos.y * Width + pos.x];
        set {
            _array[pos.y * Width + pos.x] = value;
        }
    }

    public void Reset() {
        _array.SetAll(false);
    }
}

public readonly struct PathNodePool {
    private readonly PathNode?[,] _array;

    public readonly int Width => _array.GetLength(0);
    public readonly int Height => _array.GetLength(1);

    public PathNodePool(SharedGraph graph) {
        _array = new PathNode[graph.Width, graph.Height];
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                if (graph.Nodes[x, y] is not null) {
                    _array[x, y] = new PathNode(x, y);
                }
            }
        }
    }

    public readonly PathNode? this[int x, int y] => _array[x, y];
    public readonly PathNode? this[IVec2 pos] => _array[pos.x, pos.y];
}

public class Pathfinder {
    private Room _room;
    private SlugcatDescriptor _lastDescriptor;
    private readonly DynamicGraph _dynamicGraph;

    public Pathfinder(Room room, SlugcatDescriptor descriptor) {
        _room = room;
        _lastDescriptor = descriptor;
        _dynamicGraph = new DynamicGraph(room);
    }

    public void NewRoom(Room room) {
        if (room != _room) {
            _room = room;
            _dynamicGraph.NewRoom(room);
        }
    }

    public Path? FindPath(IVec2 start, IVec2 destination, SlugcatDescriptor descriptor) {
        var sharedGraph = _room.GetCWT().sharedGraph!;
        if (sharedGraph.GetNode(start) is null) {
            Plugin.Logger!.LogDebug($"no node at start ({start.x}, {start.y})");
            _lastDescriptor = descriptor;
            return null;
        }
        if (sharedGraph.GetNode(destination) is null) {
            Plugin.Logger!.LogDebug($"no node at destination ({destination.x}, {destination.y})");
            _lastDescriptor = descriptor;
            return null;
        }
        if (Timers.active) {
            Timers.findPath.Start();
        }
        var pathNodePool = _room.GetCWT().pathNodePool!.Value;
        var openNodes = _room.GetCWT().openNodes!;
        openNodes.Reset();
        var closedNodes = _room.GetCWT().closedNodes!;
        closedNodes.Reset();
        var startNode = pathNodePool[start]!;
        startNode.Reset(destination, null, 0);
        var nodeHeap = new List<PathNode> {
            startNode
        };
        openNodes[start.x, start.y] = true;
        while (nodeHeap.Count > 0) {
            PathNode currentNode = nodeHeap[0];
            var currentPos = currentNode.GridPos;
            if (currentPos == destination) {
                if (Timers.active) {
                    Timers.findPath.Stop();
                }
                _lastDescriptor = descriptor;
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

            var graphNode = sharedGraph.Nodes[currentPos.x, currentPos.y]!;
            var adjacencyList = _dynamicGraph.AdjacencyLists[currentPos.x, currentPos.y]!;

            if (adjacencyList.Count == 0) {
                _dynamicGraph.TraceFromNode(currentPos, descriptor);
            } else if (_lastDescriptor != descriptor) {
                adjacencyList.Clear();
                _dynamicGraph.TraceFromNode(currentPos, descriptor);
            }

            void CheckConnection(NodeConnection connection) {
                IVec2 neighbourPos = connection.Next.GridPos;
                PathNode currentNeighbour = pathNodePool[neighbourPos]!;
                if (closedNodes[neighbourPos]) {
                    return;
                }
                if (!openNodes[neighbourPos]) {
                    currentNeighbour.Reset(
                        destination,
                        new PathConnection(connection.Type, currentNode),
                        currentNode.PathCost + connection.Weight
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
                if (currentNode.PathCost + connection.Weight < currentNeighbour.PathCost) {
                    currentNeighbour.PathCost = currentNode.PathCost + connection.Weight;
                    currentNeighbour.Connection = new PathConnection(connection.Type, currentNode);
                }
            }

            foreach (var connection in graphNode.Connections) {
                CheckConnection(connection);
            }
            foreach (var connection in adjacencyList) {
                CheckConnection(connection);
            }
        }
        if (Timers.active) {
            Timers.findPath.Stop();
        }
        _lastDescriptor = descriptor;
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