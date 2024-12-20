using System;
using System.Collections.Generic;

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
    public List<NodeConnection> IncomingConnections;
    public List<NodeConnection> OutgoingConnections;
    public IVec2 GridPos;

    public GraphNode(NodeType type, int x, int y) {
        Type = type;
        GridPos = new IVec2(x, y);
        IncomingConnections = new();
        OutgoingConnections = new();
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
        _ => Color.black,
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
            throw new ArgumentNullException();
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
    public record JumpToLedge(int Direction) : ConnectionType();
    public record JumpUp() : ConnectionType();
    public record JumpUpToLedge(int Direction) : ConnectionType();
    public record WalkOffEdge(int Direction) : ConnectionType();
    public record WalkOffEdgeOntoLedge(int Direction) : ConnectionType();
    public record Pounce(int Direction) : ConnectionType();
    public record PounceOntoLedge(int Direction) : ConnectionType();
    public record Shortcut() : ConnectionType();
    public record Drop() : ConnectionType();
    public record SlideOnWall(int Direction) : ConnectionType();
    public record SurfaceSwim(int Direction) : ConnectionType();

    private ConnectionType() { }

    public Color VisualizationColor => this switch {
        Jump
        or JumpUp
        or WalkOffEdge
        or Pounce => Color.blue,
        JumpToLedge
        or JumpUpToLedge
        or WalkOffEdgeOntoLedge
        or PounceOntoLedge => new Color(1f, 0.75f, 0f), // orange
        Drop => Color.red,
        Shortcut => Color.cyan,
        Crawl => Color.green,
        Climb => Color.magenta,
        Walk => Color.white,
        SlideOnWall => Color.yellow,
        SurfaceSwim => new Color(0f, 0.75f, 0.6f), // teal
        _ => Color.black,
    };

    public sealed override string ToString() {
        return this switch {
            Climb(IVec2 dir) => $"Climb({dir.x}, {dir.y})",
            Crawl(IVec2 dir) => $"Crawl({dir.x}, {dir.y})",
            Drop => "Drop",
            Jump(int dir) => $"Jump({dir})",
            JumpUp => "JumpUp",
            Pounce(int dir) => $"Pounce({dir})",
            Shortcut => "Shortcut",
            Walk(int dir) => $"Walk({dir})",
            WalkOffEdge(int dir) => $"WalkOffEdge({dir})",
            SlideOnWall(int dir) => $"SlideOnWall({dir})",
            JumpToLedge(int dir) => $"JumpToLedge({dir})",
            JumpUpToLedge(int dir) => $"JumpUpToLedge({dir})",
            PounceOntoLedge(int dir) => $"PounceOntoLedge({dir})",
            WalkOffEdgeOntoLedge(int dir) => $"WalkOffEdgeOntoLedge({dir})",
            SurfaceSwim(int dir) => $"SurfaceSwim({dir})",
            _ => "Unknown",
        };
    }

    public ConnectionType AsLedgeMove(int direction) {
        return this switch {
            Jump => new JumpToLedge(direction),
            JumpUp => new JumpUpToLedge(direction),
            WalkOffEdge => new WalkOffEdge(direction),
            Pounce => new PounceOntoLedge(direction),
            _ => throw new Exception("connection type doesn't have ledge counterpart")
        };
    }
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
                        ConnectNodes(currentNode, rightNode, new ConnectionType.Walk(1));
                        ConnectNodes(rightNode, currentNode, new ConnectionType.Walk(-1));
                    } else if (rightNode?.Type is NodeType.Corridor) {
                        ConnectNodes(currentNode, rightNode, new ConnectionType.Crawl(Consts.IVec2.Right));
                        ConnectNodes(rightNode, currentNode, new ConnectionType.Crawl(Consts.IVec2.Left));
                    }
                    if (GetNode(x + 1, y - 1)?.Type is NodeType.Slope) {
                        var rightDownNode = Nodes[x + 1, y - 1]!;
                        ConnectNodes(currentNode, rightDownNode, new ConnectionType.Walk(1));
                        ConnectNodes(rightDownNode, currentNode, new ConnectionType.Walk(-1));
                    } else if (GetNode(x + 1, y - 1)?.Type is NodeType.Floor) {
                        ConnectNodes(Nodes[x + 1, y - 1]!, currentNode, new ConnectionType.Walk(-1));
                    }
                    if (GetNode(x + 1, y + 1)?.Type is NodeType.Corridor or NodeType.Slope or NodeType.Floor) {
                        ConnectNodes(currentNode, Nodes[x + 1, y + 1]!, new ConnectionType.Walk(1));
                    }
                    if (aboveNode?.Type is NodeType.Wall(int wallDir)) {
                        ConnectNodes(aboveNode, currentNode, new ConnectionType.SlideOnWall(wallDir));
                    }
                }

                if (currentNode.Type is NodeType.Slope) {
                    if (rightNode?.Type is NodeType.Floor or NodeType.Slope) {
                        ConnectNodes(currentNode, rightNode, new ConnectionType.Walk(1));
                        ConnectNodes(rightNode, currentNode, new ConnectionType.Walk(-1));
                    } else if (GetNode(x + 1, y - 1)?.Type is NodeType.Slope) {
                        var downRightNode = Nodes[x + 1, y - 1]!;
                        ConnectNodes(currentNode, downRightNode, new ConnectionType.Walk(1));
                        ConnectNodes(downRightNode, currentNode, new ConnectionType.Walk(-1));
                    } else if (GetNode(x + 1, y - 1)?.Type is NodeType.Floor) {
                        ConnectNodes(Nodes[x + 1, y - 1]!, currentNode, new ConnectionType.Walk(-1));
                    } else if (GetNode(x + 1, y + 1)?.Type is NodeType.Slope or NodeType.Floor) {
                        var upRightNode = Nodes[x + 1, y + 1]!;
                        ConnectNodes(currentNode, upRightNode, new ConnectionType.Walk(1));
                        ConnectNodes(upRightNode, currentNode, new ConnectionType.Walk(-1));
                    } else if (GetNode(x + 1, y + 1)?.Type is NodeType.Corridor) {
                        var upRightNode = Nodes[x + 1, y + 1]!;
                        ConnectNodes(currentNode, upRightNode, new ConnectionType.Walk(1));
                        ConnectNodes(upRightNode, currentNode, new ConnectionType.Crawl(Consts.IVec2.Left));
                    }
                    if (aboveNode?.Type is NodeType.Wall(int wallDir)) {
                        ConnectNodes(aboveNode, currentNode, new ConnectionType.SlideOnWall(wallDir));
                    }
                }

                if (currentNode.Type is NodeType.Corridor) {
                    if (rightNode?.Type is NodeType.Corridor or NodeType.Floor or NodeType.ShortcutEntrance or NodeType.RoomExit) {
                        ConnectNodes(currentNode, rightNode, new ConnectionType.Crawl(Consts.IVec2.Right));
                        ConnectNodes(rightNode, currentNode, new ConnectionType.Crawl(Consts.IVec2.Left));
                    } else if (rightNode?.HasHorizontalBeam == true) {
                        ConnectNodes(currentNode, rightNode, new ConnectionType.Crawl(Consts.IVec2.Right));
                        ConnectNodes(
                            rightNode,
                            currentNode,
                            rightNode.HasHorizontalBeam
                                ? new ConnectionType.Climb(Consts.IVec2.Left)
                                : new ConnectionType.Crawl(Consts.IVec2.Right)
                        );
                    }
                    if (GetNode(x + 1, y - 1)?.Type is NodeType.Floor) {
                        ConnectNodes(Nodes[x + 1, y - 1]!, currentNode, new ConnectionType.Walk(-1));
                    } else if (GetNode(x + 1, y - 1)?.Type is NodeType.Slope) {
                        var downRightNode = Nodes[x + 1, y - 1]!;
                        ConnectNodes(currentNode, downRightNode, new ConnectionType.Climb(Consts.IVec2.Right));
                        ConnectNodes(downRightNode, currentNode, new ConnectionType.Walk(-1));
                    }
                    if (aboveNode?.Type is NodeType.Corridor or NodeType.ShortcutEntrance or NodeType.Floor or NodeType.RoomExit) {
                        ConnectNodes(
                            currentNode,
                            aboveNode,
                            currentNode.HasVerticalBeam
                                ? new ConnectionType.Climb(Consts.IVec2.Up)
                                : new ConnectionType.Crawl(Consts.IVec2.Up)
                        );
                        ConnectNodes(aboveNode, currentNode, new ConnectionType.Crawl(Consts.IVec2.Down));
                    }
                    if (GetNode(x + 1, y + 1)?.Type is NodeType.Wall(int wallDir)) {
                        ConnectNodes(
                            Nodes[x + 1, y + 1]!,
                            currentNode,
                            new ConnectionType.SlideOnWall(wallDir),
                            2
                        );
                    }
                } else {
                    if (currentNode.HasHorizontalBeam
                        && rightNode?.HasHorizontalBeam == true
                    ) {
                        ConnectNodes(currentNode, rightNode, new ConnectionType.Climb(Consts.IVec2.Right));
                        ConnectNodes(
                            rightNode,
                            currentNode,
                            rightNode.Type is NodeType.Corridor
                                ? new ConnectionType.Crawl(Consts.IVec2.Left)
                                : new ConnectionType.Climb(Consts.IVec2.Left)
                        );
                    }
                    if (currentNode.HasVerticalBeam) {
                        if (aboveNode?.HasVerticalBeam == true) {
                            ConnectNodes(currentNode, aboveNode, new ConnectionType.Climb(Consts.IVec2.Up));
                            ConnectNodes(
                                aboveNode,
                                currentNode,
                                aboveNode.Type is NodeType.Corridor
                                    ? new ConnectionType.Crawl(Consts.IVec2.Down)
                                    : new ConnectionType.Climb(Consts.IVec2.Down)
                            );
                        } else if (aboveNode?.Beam == GraphNode.BeamType.Above) {
                            ConnectNodes(currentNode, aboveNode, new ConnectionType.Climb(Consts.IVec2.Up));
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
                    ConnectNodes(currentNode, destNode, new ConnectionType.Shortcut(), shortcutData.length);
                    if (rightNode?.Type is NodeType.Corridor) {
                        ConnectNodes(currentNode, rightNode, new ConnectionType.Crawl(Consts.IVec2.Right));
                        ConnectNodes(rightNode, currentNode, new ConnectionType.Crawl(Consts.IVec2.Left));
                    }
                    if (aboveNode?.Type is NodeType.Corridor) {
                        ConnectNodes(currentNode, aboveNode, new ConnectionType.Crawl(Consts.IVec2.Up));
                        ConnectNodes(aboveNode, currentNode, new ConnectionType.Crawl(Consts.IVec2.Down));
                    }
                } else if (currentNode.Type is NodeType.RoomExit) {
                    if (rightNode?.Type is NodeType.Corridor) {
                        ConnectNodes(currentNode, rightNode, new ConnectionType.Crawl(Consts.IVec2.Right));
                        ConnectNodes(rightNode, currentNode, new ConnectionType.Crawl(Consts.IVec2.Left));
                    }
                    if (aboveNode?.Type is NodeType.Corridor) {
                        ConnectNodes(currentNode, aboveNode, new ConnectionType.Crawl(Consts.IVec2.Up));
                        ConnectNodes(aboveNode, currentNode, new ConnectionType.Crawl(Consts.IVec2.Down));
                    }
                }

                if (currentNode.Type is NodeType.Wall(int wallDir1)) {
                    if (aboveNode?.Type is NodeType.Wall) {
                        ConnectNodes(aboveNode, currentNode, new ConnectionType.SlideOnWall(wallDir1), 2);
                    }
                    if (GetNode(x + 1, y - 1)?.Type is NodeType.Corridor) {
                        ConnectNodes(currentNode, Nodes[x + 1, y - 1]!, new ConnectionType.SlideOnWall(wallDir1), 2);
                    }
                }

                if (currentNode.Beam == GraphNode.BeamType.Below) {
                    ConnectNodes(currentNode, aboveNode!, new ConnectionType.Climb(Consts.IVec2.Up));
                    ConnectNodes(aboveNode!, currentNode, new ConnectionType.Climb(Consts.IVec2.Down));
                }
            }
        }
    }

    /// <summary>
    /// Make a bidirectional connection between two nodes in the graph.
    /// </summary>
    private void ConnectNodes(GraphNode start, GraphNode end, ConnectionType type, float weight = 1f) {
        start.OutgoingConnections.Add(new NodeConnection(type, end, weight));
        end.IncomingConnections.Add(new NodeConnection(type, start, weight));
    }
}

public struct NodeExtension {
    public NodeExtension() {
        IncomingConnections = new();
        OutgoingConnections = new();
    }
    public List<NodeConnection> IncomingConnections;
    public List<NodeConnection> OutgoingConnections;
}

/// <summary>
/// Stores connections dependent on slugcat-specific values like jumps.
/// </summary>
public class DynamicGraph {
    private readonly Room _room;
    public JumpVectors Vectors { get; private set; }

    public NodeExtension?[,] Extensions;
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>
    /// Create empty adjacencly lists at positions with a corresponding <see cref="GraphNode">graph node</see>.
    /// </summary>
    public DynamicGraph(Room room, JumpVectors vectors) {
        _room = room;
        Vectors = vectors;
        var sharedGraph = room.GetCWT().SharedGraph!;
        Width = sharedGraph.Width;
        Height = sharedGraph.Height;
        Extensions = new NodeExtension?[Width, Height];
        ResetLists();
    }

    private void ResetLists() {
        var sharedGraph = _room.GetCWT().SharedGraph!;
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                if (sharedGraph.Nodes[x, y] is not null) {
                    Extensions[x, y] = new NodeExtension {
                        IncomingConnections = new(),
                        OutgoingConnections = new()
                    };
                }
            }
        }
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                if (Extensions[x, y] is not null) {
                    TraceFromNode(new IVec2(x, y));
                }
            }
        }
    }

    public void Reset(JumpVectors vectors) {
        Vectors = vectors;
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                if (Extensions[x, y] is NodeExtension ext) {
                    ext.IncomingConnections.Clear();
                    ext.OutgoingConnections.Clear();
                    TraceFromNode(new IVec2(x, y));
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
    private void TraceFromNode(IVec2 pos) {
        if (Timers.Active) {
            Timers.TraceFromNode.Start();
        }
        var sharedGraph = _room.GetCWT().SharedGraph!;
        var graphNode = sharedGraph.GetNode(pos);
        if (graphNode is null || graphNode.Type is NodeType.ShortcutEntrance or NodeType.RoomExit) {
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
            if (goRight) {
                TraceJump(
                    graphNode,
                    pos,
                    Vectors.VerticalPoleJump(1),
                    new ConnectionType.Jump(1)
                );
            }
            if (goLeft) {
                TraceJump(
                    graphNode,
                    pos,
                    Vectors.VerticalPoleJump(-1),
                    new ConnectionType.Jump(-1)
                );
            }
        }
        if (graphNode.Beam == GraphNode.BeamType.Horizontal && graphNode.Type is NodeType.Air or NodeType.Wall) {
            var headPos = new IVec2(pos.x, pos.y + 1);
            if (goRight) {
                TraceJump(
                    graphNode,
                    headPos,
                    Vectors.HorizontalPoleJump(1),
                    new ConnectionType.Jump(1)
                );
                TraceJump(
                    graphNode,
                    headPos,
                    Vectors.HorizontalPoleJump(1) with { y = 0 },
                    new ConnectionType.WalkOffEdge(1)
                );
            }
            if (goLeft) {
                TraceJump(
                    graphNode,
                    headPos,
                    Vectors.HorizontalPoleJump(-1),
                    new ConnectionType.Jump(-1)
                );
                TraceJump(
                    graphNode,
                    headPos,
                    Vectors.HorizontalPoleJump(-1) with { y = 0 },
                    new ConnectionType.WalkOffEdge(-1)
                );
            }
            TraceJumpUp(pos, Vectors.HorizontalPoleJumpVector.y);
            TraceDrop(pos);
        }

        if (graphNode.Beam == GraphNode.BeamType.Above) {
            var headPos = new IVec2(pos.x, pos.y + 1);
            if (goRight) {
                TraceJump(
                    graphNode,
                    headPos,
                    Vectors.FloorJump(1),
                    new ConnectionType.Jump(1)
                );
            }
            if (goLeft) {
                TraceJump(
                    graphNode,
                    headPos,
                    Vectors.FloorJump(-1),
                    new ConnectionType.Jump(-1)
                );
            }
            TraceJumpUp(pos, Vectors.HorizontalPoleJumpVector.y);
            TraceDrop(pos);
        } else if (graphNode.Beam == GraphNode.BeamType.Below) {
            TraceDrop(pos);
        }

        if (graphNode.Type is NodeType.Floor) {
            var headPos = new IVec2(pos.x, pos.y + 1);
            if (sharedGraph.GetNode(headPos)?.Type is NodeType.Wall(int wallDir)) {
                if (wallDir == -1) {
                    goLeft = false;
                } else {
                    goRight = false;
                }
            }
            if (goRight) {
                TraceJump(
                    graphNode,
                    headPos,
                    Vectors.FloorJump(1),
                    new ConnectionType.Jump(1)
                );
            }
            if (sharedGraph.GetNode(pos.x + 1, pos.y - 1)?.Type is NodeType.Wall) {
                TraceJump(
                    graphNode,
                    headPos,
                    Vectors.FloorJump(1) with { y = 0 },
                    new ConnectionType.WalkOffEdge(1)
                );
                TraceJump(
                    graphNode,
                    pos,
                    Vectors.Pounce(1),
                    new ConnectionType.Pounce(1),
                    false,
                    5f
                );
            }
            if (goLeft) {
                TraceJump(
                    graphNode,
                    headPos,
                    Vectors.FloorJump(-1),
                    new ConnectionType.Jump(-1)
                );
            }
            if (sharedGraph.GetNode(pos.x - 1, pos.y - 1)?.Type is NodeType.Wall) {
                TraceJump(
                    graphNode,
                    headPos,
                    Vectors.FloorJump(-1) with { y = 0 },
                    new ConnectionType.WalkOffEdge(-1)
                );
                TraceJump(
                    graphNode,
                    pos,
                    Vectors.Pounce(-1),
                    new ConnectionType.Pounce(-1),
                    false,
                    5f
                );
            }
            TraceJumpUp(pos, Vectors.FloorJumpVector.y);
            if (sharedGraph.GetNode(pos.x, pos.y - 1)?.HasPlatform == true) {
                TraceDrop(pos);
            }
        } else if (graphNode.Type is NodeType.Corridor) {
            if (sharedGraph.GetNode(pos.x + 1, pos.y) is null) {
                TraceJump(
                    graphNode,
                    pos,
                    Vectors.HorizontalCorridorFall(1),
                    new ConnectionType.WalkOffEdge(1),
                    upright: false
                );
            }
            if (sharedGraph.GetNode(pos.x - 1, pos.y) is null) {
                TraceJump(
                    graphNode,
                    pos,
                    Vectors.HorizontalCorridorFall(-1),
                    new ConnectionType.WalkOffEdge(-1),
                    upright: false
                );
            }
            if (sharedGraph.GetNode(pos.x, pos.y - 1) is null
                && _room.Tiles[pos.x, pos.y - 1].Terrain == Room.Tile.TerrainType.Air
            ) {
                TraceDrop(pos);
            }
        } else if (graphNode.Type is NodeType.Wall jumpWall
            && !graphNode.HasBeam
            && sharedGraph.GetNode(pos.x, pos.y - 1)?.Type is NodeType.Wall footJumpWall
            && sharedGraph.GetNode(pos.x, pos.y + 1)?.Type is NodeType.Wall // pressing jump under a ledge doesn't always wall jump
            && jumpWall.Direction == footJumpWall.Direction
        ) {
            TraceJump(
                graphNode,
                pos,
                Vectors.WallJump(-jumpWall.Direction),
                new ConnectionType.Jump(-jumpWall.Direction)
            );
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
        bool upright = true,
        float weightBoost = 0f
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
                var extension = Extensions[x, upright ? y - 1 : y];
                if (extension is not null && currentNode?.Type is NodeType.Floor or NodeType.Slope) {
                    ConnectNodes(startNode, currentNode, type, new IVec2(x, y).FloatDist(startNode.GridPos) + 1 + weightBoost);
                }
                if (_room.Tiles[x, upright ? y - 2 : y - 1].Terrain == Room.Tile.TerrainType.Solid) {
                    break;
                }
                y--;
            } else {
                x += direction;
                var sideNode = sharedGraph.GetNode(x, y);
                if (sideNode is not null
                    && (sideNode.Type is NodeType.Floor
                        && _room.GetTile(x, y - 1).Terrain == Room.Tile.TerrainType.Solid
                    || sideNode.Type is NodeType.Slope)
                ) {
                    ConnectNodes(
                        startNode,
                        sideNode,
                        type.AsLedgeMove(direction),
                        new IVec2(x, y).FloatDist(startNode.GridPos) + 3 + weightBoost
                    );
                    break;
                }
            }

            if (x < 0 || y < 0 || x >= Width || y >= Height
                || _room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Solid
                || _room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Slope) {
                break;
            }

            var shiftedNode = sharedGraph.Nodes[x, y];
            if (shiftedNode is null || Extensions[x, y] is null) {
                continue;
            }
            if (shiftedNode.Type is NodeType.Corridor) {
                break;
            }
            float weight = new IVec2(x, y).FloatDist(startNode.GridPos) + 1 + weightBoost;
            if (shiftedNode.Type is NodeType.Wall wall && wall.Direction == direction) {
                ConnectNodes(startNode, shiftedNode, type, weight);
                break;
            } else if (shiftedNode.Beam == GraphNode.BeamType.Vertical) {
                float poleResult = Parabola(pathOffset.y, v0, _room.gravity, (20 * x + 10 - pathOffset.x) / v0.x) / 20;
                if (poleResult > y && poleResult < y + 1) {
                    ConnectNodes(startNode, shiftedNode, type, weight);
                }
            } else if (shiftedNode.Beam == GraphNode.BeamType.Horizontal) {
                float leftHeight = Parabola(pathOffset.y, v0, _room.gravity, (20 * x - pathOffset.x) / v0.x);
                float rightHeight = Parabola(pathOffset.y, v0, _room.gravity, (20 * (x + 1) - pathOffset.x) / v0.x);
                float poleHeight = 20 * y + 10;
                if (direction * leftHeight > direction * poleHeight && direction * poleHeight > direction * rightHeight) {
                    ConnectNodes(startNode, shiftedNode, type, weight + 1f);
                }
            } else if (shiftedNode.Beam == GraphNode.BeamType.Cross) {
                ConnectNodes(startNode, shiftedNode, type, weight);
            } else {
                AddIncomingConnection(x, y, type, startNode, weight);
            }
        }
    }

    private void TraceJumpUp(IVec2 pos, float v0) {
        TraceJumpUp(pos.x, pos.y, v0);
    }

    private void TraceJumpUp(int x, int y, float v0) {
        var sharedGraph = _room.GetCWT().SharedGraph!;
        var startNode = sharedGraph.GetNode(x, y);
        if (startNode is null) {
            return;
        }
        // the max height of the jump in tile space, truncated to prevent overpredition and moved up so it starts at the head tile 
        int maxHeight = y + (int)(0.5f * v0 * v0 / _room.gravity / 20) + 1;
        for (int i = y + 1; i <= maxHeight && i < sharedGraph.Height; i++) {
            var currentTile = _room.GetTile(x, i);
            var currentNode = sharedGraph.GetNode(x, i);
            if (currentTile.Terrain == Room.Tile.TerrainType.Solid
                || currentTile.Terrain == Room.Tile.TerrainType.Slope
            ) {
                break;
            }
            var leftNode = sharedGraph.GetNode(x - 1, i);
            if (leftNode?.Type is NodeType.Floor or NodeType.Slope) {
                ConnectNodes(startNode, leftNode, new ConnectionType.JumpUpToLedge(-1), i - y);
                break;
            }
            var rightNode = sharedGraph.GetNode(x + 1, i);
            if (rightNode?.Type is NodeType.Floor or NodeType.Slope) {
                ConnectNodes(startNode, rightNode, new ConnectionType.JumpUpToLedge(1), i - y);
                break;
            }

            if (currentNode is null) {
                AddIncomingConnection(x, i, new ConnectionType.JumpUp(), startNode, i - y);
                continue;
            }

            if (currentNode.Type is NodeType.Corridor || currentNode.Beam == GraphNode.BeamType.Below) {
                ConnectNodes(startNode, currentNode, new ConnectionType.JumpUp(), i - y);
                break;
            } else if (currentNode.Beam == GraphNode.BeamType.Horizontal) {
                ConnectNodes(startNode, currentNode, new ConnectionType.JumpUp(), i - y);
            } else {
                AddIncomingConnection(x, i, new ConnectionType.JumpUp(), startNode, i - y);
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
            var currentNode = sharedGraph.GetNode(x, i);
            if (currentNode is null) {
                AddIncomingConnection(x, i, new ConnectionType.Drop(), startNode, y - i);
                continue;
            }
            if (currentNode.Type is NodeType.Air) {
                if (sharedGraph.Nodes[x, i + 1]?.HasVerticalBeam == true) {
                    if (currentNode.Beam == GraphNode.BeamType.Horizontal) {
                        ConnectNodes(startNode, currentNode, new ConnectionType.Drop(), y - i);
                    }
                } else if (currentNode.HasBeam) {
                    ConnectNodes(startNode, currentNode, new ConnectionType.Drop(), y - i + 2);
                }
            } else {
                ConnectNodes(startNode, currentNode, new ConnectionType.Drop(), y - i);
                break;
            }
        }
    }

    private void ConnectNodes(GraphNode startNode, GraphNode endNode, ConnectionType type, float weight = 1) {
        if (startNode is null || endNode is null) {
            return;
        }
        var startExt = Extensions[startNode.GridPos.x, startNode.GridPos.y]!.Value;
        var endExt = Extensions[endNode.GridPos.x, endNode.GridPos.y]!.Value;
        startExt.OutgoingConnections.Add(new NodeConnection(type, endNode, weight));
        endExt.IncomingConnections.Add(new NodeConnection(type, startNode, weight));
    }

    private void AddIncomingConnection(int x, int y, ConnectionType type, GraphNode next, float weight) {
        if (Extensions[x, y] is null) {
            Extensions[x, y] = new NodeExtension();
        }
        Extensions[x, y]!.Value.IncomingConnections.Add(new NodeConnection(type, next, weight));
    }
}