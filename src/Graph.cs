using System;
using System.Collections.Generic;
using System.Data;

using UnityEngine;

using IVec2 = RWCustom.IntVector2;

namespace JumpSlug.Pathfinding;

/// <summary>
/// Graph node representing all tiles the slugcat is able to actively perform movement on.
/// </summary>
public class GraphNode {
    public NodeType Type;
    public bool VerticalBeam;
    public bool HorizontalBeam;
    public bool HasPlatform;
    public List<NodeConnection> Connections;
    public IVec2 GridPos;

    public GraphNode(NodeType type, int x, int y) {
        Type = type;
        GridPos = new IVec2(x, y);
        Connections = new();
    }

    public bool HasBeam => VerticalBeam || HorizontalBeam;
}

/// <summary>
/// Tagged union representing different variants of <see cref="GraphNode">graph nodes.</see>
/// </summary>
public record NodeType {
    public record Air() : NodeType();
    public record Floor() : NodeType();
    public record Slope() : NodeType();
    public record Corridor() : NodeType();
    public record ShortcutEntrance(int Index) : NodeType();
    public record Wall(int Direction) : NodeType();

    private NodeType() { }
}

/// <summary>
/// connection between <see cref="GraphNode">graph nodes</see>.
/// </summary>
public class NodeConnection {
    public ConnectionType Type;
    public GraphNode Next;
    public float Weight;

    public NodeConnection(ConnectionType type, GraphNode next, float weight = 1f) {
        if (next is null) {
            throw new NoNullAllowedException();
        }
        Next = next;
        Weight = weight;
        Type = type;
    }
}

/// <summary>
/// List of node positions the slugcat should allow itself to pass through without taking action, providing easy iteration across ticks.
/// </summary>
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

    /// <summary>
    /// Iterate through the list to check if it contains the requested tile position.
    /// entries checked during previous calls to this method are not checked again on repeated invocations.
    /// </summary>
    public bool FindNode(IVec2 node) {
        while (_cursor < _ignoreList.Count) {
            if (node == _ignoreList[_cursor]) {
                return true;
            }
            _cursor++;
        }
        return false;
    }

    public bool FindEitherNode(IVec2 primary, IVec2 secondary) {
        while (_cursor < _ignoreList.Count) {
            if (primary == _ignoreList[_cursor] || secondary == _ignoreList[_cursor]) {
                return true;
            }
            _cursor++;
        }
        return false;
    }

    /// <summary>
    /// Make a deep copy of the list.
    /// </summary>
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

/// <summary>
/// Tagged union representing different variants of connections between <see cref="GraphNode">graph nodes.</see>
/// </summary>
public record ConnectionType {
    public record Walk(int Direction) : ConnectionType();
    public record Climb(IVec2 Direction) : ConnectionType();
    public record Crawl(IVec2 Direction) : ConnectionType();
    public record Jump(int Direction) : ConnectionType();
    public record WalkOffEdge(int Direction) : ConnectionType();
    public record Pounce(int Direction) : ConnectionType();
    public record Shortcut() : ConnectionType();
    public record Drop(IgnoreList IgnoreList) : ConnectionType();

    private ConnectionType() { }
}

/// <summary>
/// Stores nodes and connections for a given room. The connections are not dependent on slugcat specific data,
/// slugcat-specific connections like jumps should be stored in <see cref="DynamicGraph">dynamic graphs</see> instead.
/// </summary>
public class SharedGraph {
    public GraphNode?[,] Nodes;
    public int Width;
    public int Height;

    /// <summary>
    /// Create a new shared graph for a given rooms geometry.
    /// </summary>
    public SharedGraph(Room room) {
        Width = room.Tiles.GetLength(0);
        Height = room.Tiles.GetLength(1);
        Nodes = new GraphNode[Width, Height];
        GenerateNodes(room);
        GenerateConnections(room);
    }

    /// <summary>
    /// Bounds checked access to the graph.
    /// </summary>
    /// <returns>
    /// Null if the requested coordinates are out of bounds or the graph does not contain a node at that position.
    /// </returns>
    public GraphNode? GetNode(int x, int y) {
        if (Nodes is null || x < 0 || y < 0 || x >= Width || y >= Height) {
            return null;
        }
        return Nodes[x, y];
    }

    public GraphNode? GetNode(IVec2 pos) {
        return GetNode(pos.x, pos.y);
    }

    /// <summary>
    /// Generate the nodes making up the graph.
    /// </summary>
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
                        Nodes[x, y] = new GraphNode(new NodeType.Corridor(), x, y) {
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
                        Nodes[x, y] = new GraphNode(new NodeType.Corridor(), x, y);
                    } else if (
                          room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Solid
                          || room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.ShortcutEntrance
                          // pretend invalid slope is solid
                          || room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Slope
                          && room.Tiles[x - 1, y - 1].Terrain == Room.Tile.TerrainType.Solid
                          && room.Tiles[x + 1, y - 1].Terrain == Room.Tile.TerrainType.Solid
                    ) {
                        Nodes[x, y] = new GraphNode(new NodeType.Floor(), x, y);
                    } else if (room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Floor) {
                        Nodes[x, y] = new GraphNode(new NodeType.Floor(), x, y);
                    } else if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Air
                          && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid) {
                        Nodes[x, y] = new GraphNode(new NodeType.Wall(1), x, y);
                    } else if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid
                          && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Air) {
                        Nodes[x, y] = new GraphNode(new NodeType.Wall(-1), x, y);
                    }
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Slope
                      && room.Tiles[x, y + 1].Terrain == Room.Tile.TerrainType.Air
                      && !(room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid
                          && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid)) {
                    Nodes[x, y] = new GraphNode(new NodeType.Slope(), x, y);
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.ShortcutEntrance) {
                    int index = Array.IndexOf(room.shortcutsIndex, new IVec2(x, y));
                    if (index > -1 && room.shortcuts[index].shortCutType == ShortcutData.Type.Normal) {
                        Nodes[x, y] = new GraphNode(new NodeType.ShortcutEntrance(index), x, y);
                    }
                }

                if (room.Tiles[x, y].verticalBeam) {
                    if (Nodes[x, y] is null) {
                        Nodes[x, y] = new GraphNode(new NodeType.Air(), x, y);
                    }
                    Nodes[x, y]!.VerticalBeam = true;
                }

                if (room.Tiles[x, y].horizontalBeam) {
                    if (Nodes[x, y] is null) {
                        Nodes[x, y] = new GraphNode(new NodeType.Air(), x, y);
                    }
                    Nodes[x, y]!.HorizontalBeam = true;
                }
            }
        }
    }

    /// <summary>
    /// Generate connections between nodes in the graph.
    /// </summary>
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
                    if (aboveNode?.Type is NodeType.Corridor or NodeType.ShortcutEntrance or NodeType.Floor) {
                        ConnectNodes(
                            Nodes[x, y]!,
                            aboveNode,
                            Nodes[x, y]!.VerticalBeam
                                ? new ConnectionType.Climb(new IVec2(0, 1))
                                : new ConnectionType.Crawl(new IVec2(0, 1)),
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

    /// <summary>
    /// Make a bidirectional connection between two nodes in the graph.
    /// </summary>
    private void ConnectNodes(GraphNode start, GraphNode end, ConnectionType startToEndType, ConnectionType endToStartType, float weight = 1f) {
        start.Connections.Add(new NodeConnection(startToEndType, end, weight));
        end.Connections.Add(new NodeConnection(endToStartType, start, weight));
    }
}

/// <summary>
/// Stores connections dependent on slugcat-specific values like jumps.
/// </summary>
public class DynamicGraph {
    private Room _room;

    public List<NodeConnection>[,] AdjacencyLists;
    public int Width;
    public int Height;

    /// <summary>
    /// Create empty adjacencly lists at positions with a corresponding <see cref="GraphNode">graph node</see>.
    /// </summary>
    public DynamicGraph(Room room) {
        _room = room;
        var sharedGraph = room.GetCWT().SharedGraph!;
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

    /// <summary>
    /// Reinitialize the graph for the new room. Does nothing if the room hasn't actually changed.
    /// </summary>
    public void NewRoom(Room room) {
        if (room != _room) {
            _room = room;
            var sharedGraph = room.GetCWT().SharedGraph!;
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

    /// <summary>
    /// Calculate dynamic connections starting at the specified tile position.
    /// </summary>
    /// <param name="pos">
    /// the tile position to trace from.
    /// </param>
    /// <param name="descriptor">
    /// slugcat-specifc values to use.
    /// </param>
    public void TraceFromNode(IVec2 pos, SlugcatDescriptor descriptor) {
        if (Timers.Active) {
            Timers.TraceFromNode.Start();
        }
        var sharedGraph = _room.GetCWT().SharedGraph!;
        var graphNode = sharedGraph.GetNode(pos);
        if (graphNode is null) {
            if (Timers.Active) {
                Timers.TraceFromNode.Stop();
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
            Vector2 v0 = descriptor.VerticalPoleJumpVector();
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
            var v0 = descriptor.HorizontalPoleJumpVector();
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
            var v0 = descriptor.FloorJumpVector();
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
            var v0 = descriptor.HorizontalCorridorFallVector();
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
            var v0 = descriptor.WallJumpVector(jumpWall.Direction);
            TraceJump(pos, pos, v0, new ConnectionType.Jump(-jumpWall.Direction));
        }
        if (Timers.Active) {
            Timers.TraceFromNode.Stop();
        }
    }

    /// <summary>
    /// Calculate the altitude along the traced trajectory at a specified point in time.
    /// </summary>
    /// <param name="yOffset">
    /// the starting position of the trajectory.
    /// </param>
    /// <param name="v0">
    /// the velocity vector at t = 0.
    /// </param>
    /// <param name="g">
    /// the acceleration due to gravity.
    /// </param>
    public static float Parabola(float yOffset, Vector2 v0, float g, float t) => v0.y * t - 0.5f * g * t * t + yOffset;

    /// <summary>
    /// Trace parabolic trajectory through the shared graph and add any found connections to the dynamic graph.
    /// </summary>
    /// <param name="startPos">
    /// the tile position of the node generated connections should start from.
    /// </param>
    /// <param name="headPos">
    /// the tile position of the first bodychunk at the start of the jump, corresponding roughly to the head position.
    /// </param>
    /// <param name="v0">
    /// the velocity vector at the start of the jump.
    /// </param>
    /// <param name="type">
    /// the type assigned to traced connections.
    /// </param>
    /// <param name="upright">
    /// whether the slugcat should be treated as falling upright or head first during.
    /// </param>
    private void TraceJump(
        IVec2 startPos,
        IVec2 headPos,
        Vector2 v0,
        ConnectionType type,
        bool upright = true
    ) {
        int x = headPos.x;
        int y = headPos.y;
        var sharedGraph = _room.GetCWT().SharedGraph;
        if (x < 0 || y < 0 || x >= Width || y >= Height || sharedGraph is null) {
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
                var currentNode = sharedGraph.Nodes[x, upright ? y - 1 : y];
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

            var shiftedNode = sharedGraph.Nodes[x, y];
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

    /// <summary>
    /// Trace vertical drop from the specified tile coordinates.
    /// </summary>
    private void TraceDrop(int x, int y) {
        var sharedGraph = _room.GetCWT().SharedGraph!;
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
                if (sharedGraph.GetNode(x, i - 1)?.HasPlatform is false or null) {
                    break;
                }
            } else if (currentNode.Type is not NodeType.Air or NodeType.Wall) {
                adjacencyList.Add(
                    new NodeConnection(
                        new ConnectionType.Drop(ignoreList.Clone()),
                        currentNode,
                        Mathf.Sqrt(2 * 20 * (y - i) / _room.gravity) * 4.2f / 20
                    )
                );
                break;
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
                break;
            }
        }
    }
}