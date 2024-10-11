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
    public BeamType Beam;
    public bool HasPlatform;
    public List<NodeConnection> Connections;
    public IVec2 GridPos;

    public GraphNode(NodeType type, int x, int y) {
        Type = type;
        GridPos = new IVec2(x, y);
        Connections = new();
    }

    public bool HasVerticalBeam => Beam == BeamType.Vertical || Beam == BeamType.Cross;
    public bool HasHorizontalBeam => Beam == BeamType.Horizontal || Beam == BeamType.Cross;

    public bool HasBeam => Beam == BeamType.Vertical
        || Beam == BeamType.Horizontal
        || Beam == BeamType.Cross;

    public enum BeamType {
        None = 0,
        Vertical,
        Horizontal,
        Cross,
        Above,
        Below,
    }
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
    public record RoomExit() : NodeType();
    public record Wall(int Direction) : NodeType();

    private NodeType() { }

    public Color VisualizationColor => this switch {
        Air => Color.red,
        Floor => Color.white,
        Slope => Color.green,
        Corridor => Color.blue,
        ShortcutEntrance
        or RoomExit => Color.cyan,
        Wall => Color.grey,
        _ => throw new InvalidUnionVariantException("unsupported NodeType variant"),
    };
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
    public record Drop() : ConnectionType();
    public record SlideOnWall(int Direction) : ConnectionType();

    private ConnectionType() { }

    public Color VisualizationColor => this switch {
        Jump
        or WalkOffEdge
        or Pounce => Color.blue,
        Drop => Color.red,
        Shortcut => Color.cyan,
        Crawl => Color.green,
        Climb => Color.magenta,
        Walk => Color.white,
        SlideOnWall => Color.yellow,
        _ => throw new InvalidUnionVariantException("unsupported NodeType variant"),
    };
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
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Air
                    || room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Floor
                ) {
                    if (room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid
                        && room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid
                        || room.Tiles[x, y + 1].Terrain == Room.Tile.TerrainType.Solid
                        && room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Solid
                        || room.Tiles[x - 1, y + 1].Terrain != Room.Tile.TerrainType.Air
                        && room.Tiles[x + 1, y + 1].Terrain != Room.Tile.TerrainType.Air
                        && room.Tiles[x - 1, y - 1].Terrain != Room.Tile.TerrainType.Air
                        && room.Tiles[x + 1, y - 1].Terrain != Room.Tile.TerrainType.Air
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
                    } else if ((room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Air
                        || room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Floor)
                        && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid
                    ) {
                        Nodes[x, y] = new GraphNode(new NodeType.Wall(1), x, y);
                    } else if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid
                        && (room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Air
                        || room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Floor)
                    ) {
                        Nodes[x, y] = new GraphNode(new NodeType.Wall(-1), x, y);
                    }
                    if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Floor) {
                        if (Nodes[x, y] is null) {
                            Nodes[x, y] = new GraphNode(new NodeType.Air(), x, y);
                        }
                        Nodes[x, y]!.HasPlatform = true;
                    }
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Slope
                      && room.Tiles[x, y + 1].Terrain == Room.Tile.TerrainType.Air
                      && !(room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid
                          && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid)
                ) {
                    Nodes[x, y] = new GraphNode(new NodeType.Slope(), x, y);
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.ShortcutEntrance) {
                    int index = Array.IndexOf(room.shortcutsIndex, new IVec2(x, y));
                    if (index > -1) {
                        var type = room.shortcuts[index].shortCutType;
                        if (type == ShortcutData.Type.Normal) {
                            Nodes[x, y] = new GraphNode(new NodeType.ShortcutEntrance(index), x, y);
                        } else if (type == ShortcutData.Type.RoomExit) {
                            Nodes[x, y] = new GraphNode(new NodeType.RoomExit(), x, y);
                        }
                    }
                }

                if (room.Tiles[x, y].verticalBeam) {
                    if (Nodes[x, y] is null) {
                        Nodes[x, y] = new GraphNode(new NodeType.Air(), x, y);
                    }
                    if (room.Tiles[x, y].horizontalBeam) {
                        Nodes[x, y]!.Beam = GraphNode.BeamType.Cross;
                    } else {
                        Nodes[x, y]!.Beam = GraphNode.BeamType.Vertical;
                    }
                } else if (room.Tiles[x, y].horizontalBeam) {
                    if (Nodes[x, y] is null) {
                        Nodes[x, y] = new GraphNode(new NodeType.Air(), x, y);
                    }
                    Nodes[x, y]!.Beam = GraphNode.BeamType.Horizontal;
                } else if (room.Tiles[x, y - 1].Terrain != Room.Tile.TerrainType.Solid
                    && room.Tiles[x, y - 1].verticalBeam
                ) {
                    if (Nodes[x, y] is null) {
                        Nodes[x, y] = new GraphNode(new NodeType.Air(), x, y);
                    }
                    Nodes[x, y]!.Beam = GraphNode.BeamType.Above;
                } else if (room.Tiles[x, y + 1].Terrain != Room.Tile.TerrainType.Solid
                    && room.Tiles[x, y + 1].verticalBeam
                ) {
                    if (Nodes[x, y] is null) {
                        Nodes[x, y] = new GraphNode(new NodeType.Air(), x, y);
                    }
                    Nodes[x, y]!.Beam = GraphNode.BeamType.Below;
                } else if (Nodes[x, y] is not null) {
                    Nodes[x, y]!.Beam = GraphNode.BeamType.None;
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
                var currentNode = Nodes[x, y];
                if (currentNode is null) {
                    continue;
                }
                var rightNode = GetNode(x + 1, y);
                var aboveNode = GetNode(x, y + 1);

                if (currentNode.Type is NodeType.Floor) {
                    if (rightNode?.Type is NodeType.Floor or NodeType.Slope) {
                        ConnectNodes(
                            currentNode,
                            rightNode,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    } else if (rightNode?.Type is NodeType.Corridor) {
                        ConnectNodes(
                            currentNode,
                            rightNode,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    }
                    if (GetNode(x + 1, y - 1)?.Type is NodeType.Slope) {
                        ConnectNodes(
                            currentNode,
                            Nodes[x + 1, y - 1]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    } else if (GetNode(x + 1, y - 1)?.Type is NodeType.Floor) {
                        currentNode.Connections.Add(
                            new NodeConnection(
                                new ConnectionType.Walk(-1),
                                Nodes[x + 1, y - 1]!
                            )
                        );
                    }
                    if (GetNode(x + 1, y + 1)?.Type is NodeType.Corridor or NodeType.Slope or NodeType.Floor) {
                        currentNode.Connections.Add(
                            new NodeConnection(
                                new ConnectionType.Walk(1),
                                Nodes[x + 1, y + 1]!
                            )
                        );
                    }
                }

                if (currentNode.Type is NodeType.Slope) {
                    if (rightNode?.Type is NodeType.Floor or NodeType.Slope) {
                        ConnectNodes(
                            currentNode,
                            rightNode,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    } else if (GetNode(x + 1, y - 1)?.Type is NodeType.Slope) {
                        ConnectNodes(
                            currentNode,
                            Nodes[x + 1, y - 1]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    } else if (GetNode(x + 1, y - 1)?.Type is NodeType.Floor) {
                        currentNode.Connections.Add(
                            new NodeConnection(
                                new ConnectionType.Walk(-1),
                                Nodes[x + 1, y - 1]!
                            )
                        );
                    } else if (GetNode(x + 1, y + 1)?.Type is NodeType.Slope or NodeType.Floor) {
                        ConnectNodes(
                            currentNode,
                            Nodes[x + 1, y + 1]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    } else if (GetNode(x + 1, y + 1)?.Type is NodeType.Corridor) {
                        ConnectNodes(
                            currentNode,
                            Nodes[x + 1, y + 1]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    }
                }

                if (currentNode.Type is NodeType.Corridor) {
                    if (rightNode?.Type is NodeType.Corridor or NodeType.Floor or NodeType.ShortcutEntrance or NodeType.RoomExit) {
                        ConnectNodes(
                            currentNode,
                            rightNode,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    } else if (rightNode?.HasHorizontalBeam == true) {
                        ConnectNodes(
                            currentNode,
                            rightNode,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            rightNode.HasHorizontalBeam
                                ? new ConnectionType.Climb(new IVec2(-1, 0))
                                : new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    }
                    if (GetNode(x + 1, y - 1)?.Type is NodeType.Floor) {
                        currentNode.Connections.Add(
                            new NodeConnection(
                                new ConnectionType.Walk(-1),
                                Nodes[x + 1, y - 1]!
                            )
                        );
                    } else if (GetNode(x + 1, y - 1)?.Type is NodeType.Slope) {
                        ConnectNodes(
                            currentNode,
                            Nodes[x + 1, y - 1]!,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            new ConnectionType.Walk(-1)
                        );
                    }
                    if (aboveNode?.Type is NodeType.Corridor or NodeType.ShortcutEntrance or NodeType.Floor or NodeType.RoomExit) {
                        ConnectNodes(
                            currentNode,
                            aboveNode,
                            currentNode.HasVerticalBeam
                                ? new ConnectionType.Climb(new IVec2(0, 1))
                                : new ConnectionType.Crawl(new IVec2(0, 1)),
                            new ConnectionType.Crawl(new IVec2(0, -1))
                        );
                    }
                    if (GetNode(x + 1, y + 1)?.Type is NodeType.Wall wallTR) {
                        currentNode.Connections.Add(
                            new NodeConnection(
                                new ConnectionType.SlideOnWall(wallTR.Direction),
                                Nodes[x + 1, y + 1]!,
                                2
                            )
                        );
                    }
                } else {
                    if (currentNode.HasHorizontalBeam
                        && rightNode?.HasHorizontalBeam == true
                    ) {
                        ConnectNodes(
                            currentNode,
                            rightNode,
                            new ConnectionType.Climb(new IVec2(1, 0)),
                            rightNode.Type is NodeType.Corridor
                                ? new ConnectionType.Crawl(new IVec2(-1, 0))
                                : new ConnectionType.Climb(new IVec2(-1, 0))
                        );
                    }
                    if (currentNode.HasVerticalBeam) {
                        if (aboveNode?.HasVerticalBeam == true) {
                            ConnectNodes(
                                currentNode,
                                aboveNode,
                                new ConnectionType.Climb(new IVec2(0, 1)),
                                aboveNode.Type is NodeType.Corridor
                                    ? new ConnectionType.Crawl(new IVec2(0, -1))
                                    : new ConnectionType.Climb(new IVec2(0, -1))
                            );
                        } else if (aboveNode?.Beam == GraphNode.BeamType.Above) {
                            aboveNode.Connections.Add(
                                new NodeConnection(
                                    new ConnectionType.Climb(new IVec2(0, 1)),
                                    currentNode
                                )
                            );
                        }
                    }
                }
                if (currentNode.Type is NodeType.ShortcutEntrance entrance) {
                    var shortcutData = room.shortcuts[entrance.Index];
                    var destNode = Nodes[shortcutData.destinationCoord.x, shortcutData.destinationCoord.y];
                    if (destNode is null || destNode.Type is not NodeType.ShortcutEntrance) {
                        Plugin.Logger!.LogError($"Shortcut entrance has no valid exit, pos: ({x}, {y}), index: {entrance.Index}");
                        return;
                    }
                    destNode.Connections.Add(
                        new NodeConnection(
                            new ConnectionType.Shortcut(),
                            currentNode,
                            shortcutData.length
                        )
                    );
                    if (rightNode?.Type is NodeType.Corridor) {
                        ConnectNodes(
                            currentNode,
                            rightNode,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    }
                    if (aboveNode?.Type is NodeType.Corridor) {
                        ConnectNodes(
                            currentNode,
                            aboveNode,
                            new ConnectionType.Crawl(new IVec2(0, 1)),
                            new ConnectionType.Crawl(new IVec2(0, -1))
                        );
                    }
                } else if (currentNode.Type is NodeType.RoomExit) {
                    if (rightNode?.Type is NodeType.Corridor) {
                        ConnectNodes(
                            currentNode,
                            rightNode,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    }
                    if (aboveNode?.Type is NodeType.Corridor) {
                        ConnectNodes(
                            currentNode,
                            aboveNode,
                            new ConnectionType.Crawl(new IVec2(0, 1)),
                            new ConnectionType.Crawl(new IVec2(0, -1))
                        );
                    }
                }

                if (currentNode.Type is NodeType.Wall wall) {
                    if (aboveNode?.Type is NodeType.Wall) {
                        currentNode.Connections.Add(
                            new NodeConnection(
                                new ConnectionType.SlideOnWall(wall.Direction),
                                aboveNode,
                                2
                            )
                        );
                    }
                    if (GetNode(x + 1, y - 1)?.Type is NodeType.Corridor) {
                        Nodes[x + 1, y - 1]!.Connections.Add(
                            new NodeConnection(
                                new ConnectionType.SlideOnWall(wall.Direction),
                                currentNode,
                                2
                            )
                        );
                    }
                }

                if (currentNode.Beam == GraphNode.BeamType.Below) {
                    ConnectNodes(
                        currentNode,
                        aboveNode!,
                        new ConnectionType.Climb(new IVec2(0, 1)),
                        new ConnectionType.Climb(new IVec2(0, -1))
                    );
                }
            }
        }
    }

    /// <summary>
    /// Make a bidirectional connection between two nodes in the graph.
    /// </summary>
    private void ConnectNodes(GraphNode a, GraphNode b, ConnectionType toBType, ConnectionType toAType, float weight = 1f) {
        a.Connections.Add(new NodeConnection(toAType, b, weight));
        b.Connections.Add(new NodeConnection(toBType, a, weight));
    }
}

/// <summary>
/// Stores connections dependent on slugcat-specific values like jumps.
/// </summary>
public class DynamicGraph {
    private Room _room;
    private SlugcatDescriptor _descriptor;

    public List<NodeConnection>?[,] AdjacencyLists;
    public int Width;
    public int Height;

    /// <summary>
    /// Create empty adjacencly lists at positions with a corresponding <see cref="GraphNode">graph node</see>.
    /// </summary>
    public DynamicGraph(Room room, SlugcatDescriptor descriptor) {
        _room = room;
        _descriptor = descriptor;
        var sharedGraph = room.GetCWT().SharedGraph!;
        Width = sharedGraph.Width;
        Height = sharedGraph.Height;
        AdjacencyLists = new List<NodeConnection>[Width, Height];
        ResetLists();
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
            ResetLists();
        }
    }

    private void ResetLists() {
        var sharedGraph = _room.GetCWT().SharedGraph!;
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                if (sharedGraph.Nodes[x, y] is not null) {
                    AdjacencyLists[x, y] = new();
                }
            }
        }
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                if (AdjacencyLists[x, y] is not null) {
                    TraceFromNode(new IVec2(x, y), _descriptor);
                }
            }
        }
    }

    public void Reset(SlugcatDescriptor descriptor) {
        _descriptor = descriptor;
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                var list = AdjacencyLists[x, y];
                if (list is not null) {
                    list.Clear();
                    TraceFromNode(new IVec2(x, y), _descriptor);
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
    private void TraceFromNode(IVec2 pos, SlugcatDescriptor descriptor) {
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
        if (graphNode.Beam == GraphNode.BeamType.Vertical
            && graphNode.Type is NodeType.Air or NodeType.Wall
            && sharedGraph.GetNode(pos.x, pos.y - 1)?.Type is NodeType.Air or NodeType.Wall
        ) {
            Vector2 v0 = descriptor.VerticalPoleJumpVector(1);
            if (goRight) {
                TraceJump(graphNode, pos, v0, new ConnectionType.Jump(1));
            }
            if (goLeft) {
                v0.x = -v0.x;
                TraceJump(graphNode, pos, v0, new ConnectionType.Jump(-1));
            }
        }
        if (graphNode.Beam == GraphNode.BeamType.Horizontal && graphNode.Type is NodeType.Air or NodeType.Wall) {
            var headPos = new IVec2(pos.x, pos.y + 1);
            var v0 = descriptor.HorizontalPoleJumpVector(1);
            if (goRight) {
                TraceJump(graphNode, headPos, v0, new ConnectionType.Jump(1));
            }
            if (goLeft) {
                v0.x = -v0.x;
                TraceJump(graphNode, headPos, v0, new ConnectionType.Jump(-1));
            }
            TraceDrop(pos);
        }

        if (graphNode.Beam == GraphNode.BeamType.Above) {
            TraceDrop(pos);
        } else if (graphNode.Beam == GraphNode.BeamType.Below) {
            TraceDrop(pos);
        }

        if (graphNode.Type is NodeType.Floor) {
            var headPos = new IVec2(pos.x, pos.y + 1);
            var v0 = descriptor.FloorJumpVector(1);
            if (goRight) {
                TraceJump(graphNode, headPos, v0, new ConnectionType.Jump(1));
                if (sharedGraph.GetNode(pos.x + 1, pos.y - 1)?.Type is NodeType.Wall) {
                    v0.y = 0f;
                    TraceJump(graphNode, headPos, v0, new ConnectionType.WalkOffEdge(1));
                }
            }
            if (goLeft) {
                v0.x = -v0.x;
                TraceJump(graphNode, headPos, v0, new ConnectionType.Jump(-1));
                if (sharedGraph.GetNode(pos.x - 1, pos.y - 1)?.Type is NodeType.Wall) {
                    v0.y = 0f;
                    TraceJump(graphNode, headPos, v0, new ConnectionType.WalkOffEdge(-1));
                }
            }
            if (sharedGraph.GetNode(pos.x, pos.y - 1)?.HasPlatform == true) {
                TraceDrop(pos);
            }

        } else if (graphNode.Type is NodeType.Corridor) {
            var v0 = descriptor.HorizontalCorridorFallVector(1);
            // v0.x might be too large
            if (sharedGraph.GetNode(pos.x + 1, pos.y) is null) {
                TraceJump(graphNode, pos, v0, new ConnectionType.WalkOffEdge(1), upright: false);
            }
            if (sharedGraph.GetNode(pos.x - 1, pos.y) is null) {
                v0.x = -v0.x;
                TraceJump(graphNode, pos, v0, new ConnectionType.WalkOffEdge(-1), upright: false);
            }
            if (sharedGraph.GetNode(pos.x, pos.y - 1) is null
                && _room.Tiles[pos.x, pos.y - 1].Terrain == Room.Tile.TerrainType.Air
            ) {
                TraceDrop(pos);
            }
        } else if (graphNode.Type is NodeType.Wall jumpWall) {
            var v0 = descriptor.WallJumpVector(jumpWall.Direction);
            Plugin.Logger!.LogDebug("wall jump");
            TraceJump(graphNode, pos, v0, new ConnectionType.Jump(-jumpWall.Direction));
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
        GraphNode startNode,
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
                var destConnectionList = AdjacencyLists[x, upright ? y - 1 : y];
                if (destConnectionList is not null && currentNode?.Type is NodeType.Floor or NodeType.Slope) {
                    destConnectionList.Add(new NodeConnection(type, startNode, new IVec2(x, y).FloatDist(startNode.GridPos) + 1));
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
            var shiftedConnectionList = AdjacencyLists[x, y];
            if (shiftedNode is null || shiftedConnectionList is null) {
                continue;
            }
            if (shiftedNode.Type is NodeType.Corridor) {
                break;
            }
            if (shiftedNode.Type is NodeType.Wall wall && wall.Direction == direction) {
                shiftedConnectionList.Add(new NodeConnection(type, startNode, new IVec2(x, y).FloatDist(startNode.GridPos) + 1));
                break;
            } else if (shiftedNode.Beam == GraphNode.BeamType.Vertical) {
                float poleResult = Parabola(pathOffset.y, v0, _room.gravity, (20 * x + 10 - pathOffset.x) / v0.x) / 20;
                if (poleResult > y && poleResult < y + 1) {
                    shiftedConnectionList.Add(new NodeConnection(type, startNode, new IVec2(x, y).FloatDist(startNode.GridPos) + 1));
                }
            } else if (shiftedNode.Beam == GraphNode.BeamType.Horizontal) {
                float leftHeight = Parabola(pathOffset.y, v0, _room.gravity, (20 * x - pathOffset.x) / v0.x);
                float rightHeight = Parabola(pathOffset.y, v0, _room.gravity, (20 * (x + 1) - pathOffset.x) / v0.x);
                float poleHeight = 20 * y + 10;
                if (direction * leftHeight < direction * poleHeight && direction * poleHeight < direction * rightHeight) {
                    shiftedConnectionList.Add(new NodeConnection(type, startNode, new IVec2(x, y).FloatDist(startNode.GridPos) + 2));
                }
            } else if (shiftedNode.Beam == GraphNode.BeamType.Cross) {
                shiftedConnectionList.Add(new NodeConnection(type, startNode, new IVec2(x, y).FloatDist(startNode.GridPos) + 1));
            }
        }
    }

    private void TraceDrop(IVec2 pos) {
        TraceDrop(pos.x, pos.y);
    }

    /// <summary>
    /// Trace vertical drop from the specified tile coordinates.
    /// </summary>
    private void TraceDrop(int x, int y) {
        var sharedGraph = _room.GetCWT().SharedGraph!;
        var startNode = sharedGraph.GetNode(x, y);
        if (startNode is null) {
            return;
        }
        for (int i = y - 1; i >= 0; i--) {
            var currentNode = sharedGraph.Nodes[x, i];
            var adjacencyList = AdjacencyLists[x, i];
            if (currentNode is null || adjacencyList is null) {
                continue;
            }
            if (currentNode.Type is NodeType.Air) {
                if (sharedGraph.Nodes[x, i + 1]?.HasVerticalBeam == true) {
                    if (currentNode.Beam == GraphNode.BeamType.Horizontal) {
                        adjacencyList.Add(
                            new NodeConnection(
                                new ConnectionType.Drop(),
                                startNode,
                                Mathf.Sqrt(2 * 20 * (y - i) / _room.gravity) * 4.2f / 20
                            )
                        );
                    }
                } else if (currentNode.HasBeam) {
                    adjacencyList.Add(
                        new NodeConnection(
                            new ConnectionType.Drop(),
                            startNode,
                            Mathf.Sqrt(2 * 20 * (y - i) / _room.gravity) * 4.2f / 20
                        )
                    );
                }
            } else {
                // t = sqrt(2 * d / g)
                // weight might have inaccurate units
                adjacencyList.Add(
                    new NodeConnection(
                        new ConnectionType.Drop(),
                        startNode,
                        Mathf.Sqrt(2 * 20 * (y - i) / _room.gravity) * 4.2f / 20
                    )
                );
                break;
            }
        }
    }
}