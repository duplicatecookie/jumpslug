using System;
using System.Text;
using System.Collections.Generic;

using UnityEngine;

using IVec2 = RWCustom.IntVector2;
using System.IO;
using RWCustom;
using System.Linq;

namespace JumpSlug.Pathfinding;

/// <summary>
/// Node used by the pathfinding algorithm when generating paths.
/// </summary>
public class PathNode {
    public IVec2 GridPos { get; }
    public PathConnection? Connection;
    public float PathCost;

    public float Heuristic;
    public float FCost => PathCost + Heuristic;
    public float Threat;
    public int DiveLength;

    /// <summary>
    /// Create new node at specified grid position.
    /// </summary>
    public PathNode(int x, int y) {
        GridPos = new IVec2(x, y);
        PathCost = 0;
        Heuristic = 0;
        DiveLength = 0;
    }

    public PathNode(IVec2 pos) : this(pos.x, pos.y) { }

    /// <summary>
    /// Overwrite all values except position.
    /// </summary>
    /// <param name="start">
    /// the grid position of the node the pathfinder is trying to reach.
    /// </param>
    /// <param name="connection">
    /// the new connection to assign to the node.
    /// </param>
    /// <param name="cost">
    /// the new path cost to assign to the node.
    /// </param>
    public void Reset(PathConnection? connection, float cost, float threat, float heuristic, int diveLength) {
        PathCost = cost;
        Connection = connection;
        Heuristic = heuristic;
        Threat = threat;
        DiveLength = diveLength;
    }
}

/// <summary>
/// Connection between <see cref="PathNode">nodes</see> used by the pathfinding algorithm.
/// </summary>
public struct PathConnection {
    public ConnectionType Type;
    public PathNode Next;
    public PathConnection(ConnectionType type, PathNode next) {
        Type = type;
        Next = next;
    }

    public readonly ConnectionType? PeekType(int offset) {
        if (offset < 1) {
            return null;
        }
        var cursor = this;
        while (offset > 0) {
            if (cursor.Next.Connection is null) {
                return null;
            }
            cursor = cursor.Next.Connection.Value;
            offset -= 1;
        }
        return cursor.Type;
    }

    public readonly IVec2? PeekPos(int offset) {
        if (offset < 1) {
            return null;
        }
        if (offset == 1) {
            return Next.GridPos;
        }
        var cursor = this;
        while (offset > 1) {
            if (cursor.Next.Connection is null) {
                return null;
            }
            cursor = cursor.Next.Connection.Value;
            offset -= 1;
        }
        return cursor.Next.GridPos;
    }

    public readonly PathConnection? FindInPath(IVec2 pos) {
        for (var cursor = this;
            cursor.Next.Connection is not null;
            cursor = cursor.Next.Connection.Value
        ) {
            if (cursor.Next.GridPos == pos) {
                return cursor.Next.Connection;
            }
        }
        return null;
    }
}

/// <summary>
/// slugcat-specific values that may or may not be able to change over time.
/// </summary>
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
        float adrenaline = Mathf.Lerp(1, 1.5f, Adrenaline);
        return new Vector2(
            4.2f * Runspeed * adrenaline,
            (IsRivulet ? 6f : 4f) * adrenaline + JumpBoost(IsPup ? 7 : 8));
    }

    public readonly Vector2 VerticalPoleJumpVector() {
        float adrenaline = Mathf.Lerp(1, 1.5f, Adrenaline);
        if (IsRivulet) {
            return new Vector2(9f, 9f) * adrenaline;
        } else if (IsPup) {
            return new Vector2(5f, 7f) * adrenaline;
        } else {
            return new Vector2(6f, 8f) * adrenaline;
        }
    }

    public readonly Vector2 HorizontalPoleJumpVector() {
        float adrenaline = Mathf.Lerp(1, 1.5f, Adrenaline);
        return new Vector2(
            4.2f * Runspeed * adrenaline,
            (IsRivulet ? 6f : 4f) * adrenaline + JumpBoost(IsPup ? 7 : 8)
        );
    }

    public readonly Vector2 HorizontalCorridorFallVector() {
        return new Vector2(
            4.2f * Runspeed * Mathf.Lerp(1, 1.5f, Adrenaline),
            0
        );
    }

    public readonly Vector2 WallJumpVector() {
        float adrenaline = Mathf.Lerp(1, 1.15f, Adrenaline);
        if (IsRivulet) {
            return new Vector2(9, 10) * adrenaline;
        } else if (IsPup) {
            return new Vector2(5, 6) * adrenaline;
        } else {
            return new Vector2(6, 8) * adrenaline;
        }
    }

    public readonly Vector2 PounceVector() {
        float adrenaline = Mathf.Lerp(1, 1.15f, Adrenaline);
        if (IsRivulet) {
            return new Vector2(12 + 3f * adrenaline, 4 + JumpBoost(6)) * adrenaline;
        } else if (IsPup) {
            return new Vector2(5.5f + 3f * adrenaline, 4 + JumpBoost(6)) * adrenaline;
        } else {
            return new Vector2(9 + 3f * adrenaline, 4 + JumpBoost(6)) * adrenaline;
        }
    }

    public readonly JumpVectors ToJumpVectors() {
        return new JumpVectors {
            FloorJumpVector = FloorJumpVector(),
            VerticalPoleJumpVector = VerticalPoleJumpVector(),
            HorizontalPoleJumpVector = HorizontalPoleJumpVector(),
            HorizontalCorridorFallVector = HorizontalCorridorFallVector(),
            WallJumpVector = WallJumpVector(),
            PounceVector = PounceVector(),
        };
    }
}

public struct JumpVectors {
    public Vector2 FloorJumpVector;
    public Vector2 VerticalPoleJumpVector;
    public Vector2 HorizontalPoleJumpVector;
    public Vector2 HorizontalCorridorFallVector;
    public Vector2 WallJumpVector;
    public Vector2 PounceVector;
    public readonly Vector2 FloorJump(int direction) {
        return new Vector2(FloorJumpVector.x * direction, FloorJumpVector.y);
    }

    public readonly Vector2 VerticalPoleJump(int direction) {
        return new Vector2(VerticalPoleJumpVector.x * direction, VerticalPoleJumpVector.y);
    }

    public readonly Vector2 HorizontalPoleJump(int direction) {
        return new Vector2(HorizontalPoleJumpVector.x * direction, VerticalPoleJumpVector.y);
    }

    public readonly Vector2 HorizontalCorridorFall(int direction) {
        return new Vector2(HorizontalCorridorFallVector.x * direction, VerticalPoleJumpVector.y);
    }

    public readonly Vector2 WallJump(int jumpDirection) {
        return new Vector2(-WallJumpVector.x * jumpDirection, WallJumpVector.y);
    }

    public readonly Vector2 Pounce(int direction) {
        return new Vector2(PounceVector.x * direction, PounceVector.y);
    }
}

public class PathNodeQueue {
    private readonly List<PathNode> _nodes;
    private readonly int[,] _indexMap;

    public int Count => _nodes.Count;
    public int Width => _indexMap.GetLength(0);
    public int Height => _indexMap.GetLength(1);
    public PathNode? Root => _nodes.Count > 0 ? _nodes[0] : null;

    public PathNodeQueue(int capacity, int width, int height) {
        _nodes = new(capacity);
        _indexMap = new int[width, height];
        ResetMap();
    }

    public void Reset() {
        _nodes.Clear();
        ResetMap();
    }

    private void ResetMap() {
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                _indexMap[x, y] = -1;
            }
        }
    }

    public void Add(PathNode node) {
        _nodes.Add(node);
        IVec2 pos = node.GridPos;
        _indexMap[pos.x, pos.y] = _nodes.Count - 1;
        MoveUp(_nodes.Count - 1);
    }

    public void DecreasePriority(IVec2 pos) {
        int index = _indexMap[pos.x, pos.y];
        if (index < 0) {
            throw new ArgumentOutOfRangeException();
        }
        MoveUp(index);
    }

    private void MoveUp(int index) {
        while (index > 0 && _nodes[index].FCost < _nodes[(index - 1) / 2].FCost) {
            Swap(index, (index - 1) / 2);
            index = (index - 1) / 2;
        }
    }

    private void MoveDown(int index) {
        int leftIndex = 2 * index + 1;
        int rightIndex = 2 * index + 2;
        while (true) {
            if (rightIndex < _nodes.Count) {
                float parentCost = _nodes[index].FCost;
                float leftCost = _nodes[leftIndex].FCost;
                float rightCost = _nodes[rightIndex].FCost;
                if (leftCost < parentCost) {
                    if (rightCost < parentCost) {
                        if (leftCost < rightCost) {
                            Swap(index, leftIndex);
                            index = leftIndex;
                        } else {
                            Swap(index, rightIndex);
                            index = rightIndex;
                        }
                    } else {
                        Swap(index, leftIndex);
                        index = leftIndex;
                    }
                } else if (rightCost < parentCost) {
                    Swap(index, rightIndex);
                    index = rightIndex;
                } else {
                    // heap in order
                    break;
                }
            } else if (leftIndex < _nodes.Count) {
                float parentCost = _nodes[index].FCost;
                float leftCost = _nodes[leftIndex].FCost;
                if (leftCost < parentCost) {
                    Swap(index, leftIndex);
                    index = leftIndex;
                } else {
                    // heap in order
                    break;
                }
            } else {
                // no children
                break;
            }
            leftIndex = 2 * index + 1;
            rightIndex = 2 * index + 2;
        }
    }

    private void Swap(int i, int j) {
        (_nodes[i], _nodes[j]) = (_nodes[j], _nodes[i]);
        IVec2 a = _nodes[i].GridPos;
        IVec2 b = _nodes[j].GridPos;
        (_indexMap[b.x, b.y], _indexMap[a.x, a.y]) = (_indexMap[a.x, a.y], _indexMap[b.x, b.y]);
    }

    public void RemoveRoot() {
        _nodes[0] = _nodes[_nodes.Count - 1];
        IVec2 pos = _nodes[0].GridPos;
        _indexMap[pos.x, pos.y] = 0;
        _nodes.Pop();
        MoveDown(0);
    }

    public void ResetHeuristics(IVec2 start) {
        foreach (var node in _nodes) {
            node.Heuristic = start.FloatDist(node.GridPos);
        }
        for (int i = _nodes.Count / 2; i >= 0; i--) {
            MoveDown(i);
        }
    }

    public void RemoveHeuristics() {
        foreach (var node in _nodes) {
            node.Heuristic = 0;
        }
        for (int i = _nodes.Count / 2; i >= 0; i--) {
            MoveDown(i);
        }
    }

    public bool Validate() {
        PathNode node;
        for (int i = 0; i < Count; i++) {
            node = _nodes[i];
            if (node.FCost < Root!.FCost) {
                return false;
            }
        }
        return true;
    }

    public string DebugList() {
        var validationString = new StringBuilder();
        int i = 1;
        int k = 0;
        for (int j = 0; j < Count; j++) {
            validationString.Append(_nodes[j].FCost);
            validationString.Append(" ");
            if ((j - k) % (int)Mathf.Pow(2, i) == 0) {
                validationString.AppendLine();
                k += (int)Mathf.Pow(2, i);
                i += 1;
            }
        }
        return validationString.ToString();
    }
}

/// <summary>
/// An object pool allowing <see cref="PathNode">path nodes</see> to be reused between ticks and shared between pathfinders without deallocation.
/// </summary>
public readonly struct PathNodePool {
    private readonly PathNode?[,] _array;
    public readonly int NonNullCount;
    public readonly int Width => _array.GetLength(0);
    public readonly int Height => _array.GetLength(1);

    public PathNodePool(SharedGraph graph) {
        _array = new PathNode[graph.Width, graph.Height];
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                if (graph.Nodes[x, y] is not null) {
                    _array[x, y] = new PathNode(x, y);
                    NonNullCount++;
                }
            }
        }
    }

    public PathNode? this[int x, int y] {
        get => _array[x, y];
        set => _array[x, y] = value;
    }
    public PathNode? this[IVec2 pos] {
        get => _array[pos.x, pos.y];
        set => _array[pos.x, pos.y] = value;
    }
}

public interface IDestinationFinder {
    bool StopSearching(PathNode node);
    IVec2? Destination();
}

public class Pathfinder {
    public const float BASE_BREATH_USE = 1f / (40f * 9f * 1.28f);
    public static readonly IVec2[] LowerThreeDirections = {
        new IVec2(-1, -1),
        new IVec2(0, -1),
        new IVec2(1,-1),
    };
    public int DivingLimit;
    private Room _room;
    private IVec2? _lastDestination;
    public DynamicGraph DynamicGraph;
    public PathNodePool PathNodePool;
    public BitGrid OpenNodes;
    public BitGrid ClosedNodes;
    public PathNodeQueue NodeQueue;
    private readonly ThreatTracker _threatTracker;

    /// <summary>
    /// Create new pathfinder in the specified room.
    /// </summary>
    /// <param name="room">
    /// the room the pathfinder will try to find paths through
    /// </param>
    /// <param name="descriptor">
    /// relevant information about the slugcat using this pathfinder.
    /// </param>
    public Pathfinder(Room room, DynamicGraph dynGraph, ThreatTracker threatTracker, int divingLimit) {
        _room = room;
        _lastDestination = null;
        DivingLimit = divingLimit;
        DynamicGraph = dynGraph;
        var sharedGraph = _room.GetCWT().SharedGraph!;
        PathNodePool = new PathNodePool(sharedGraph);
        int width = sharedGraph.Width;
        int height = sharedGraph.Height;
        OpenNodes = new BitGrid(width, height);
        ClosedNodes = new BitGrid(width, height);
        NodeQueue = new PathNodeQueue(PathNodePool.NonNullCount, width, height);
        _threatTracker = threatTracker;
    }

    /// <summary>
    /// Reinitialize pathfinder for new room. Does nothing if the room has not actually changed.
    /// </summary>
    public void NewRoom(Room room, DynamicGraph dynGraph) {
        if (room != _room) {
            _room = room;
            DynamicGraph = dynGraph;
            var sharedGraph = _room.GetCWT().SharedGraph!;
            PathNodePool = new PathNodePool(sharedGraph);
            int width = sharedGraph.Width;
            int height = sharedGraph.Height;
            OpenNodes = new BitGrid(width, height);
            ClosedNodes = new BitGrid(width, height);
            NodeQueue = new PathNodeQueue(PathNodePool.NonNullCount, width, height);
        }
    }

    public static int FindDivingLimit(float averageSpeed, float lungsFac, float airInLungCutoff) {
        return Mathf.CeilToInt(averageSpeed * airInLungCutoff / (BASE_BREATH_USE * lungsFac) / 20f);
    }

    /// <summary>
    /// Find fastest path between two coordinates.
    /// </summary>
    /// <param name="start">
    /// the start of the requested path.
    /// </param>
    /// <param name="destination">
    /// the end of the requested path.
    /// </param>
    /// <param name="descriptor">
    /// updated slugcat-specific values.
    /// </param>
    /// <returns>
    /// Null if no path could be found or the coordinates are invalid,
    /// otherwise returns a <see cref="Path">path</see> ready for use by the AI.
    /// </returns>
    public PathConnection? FindPathTo(
        IVec2 start,
        IVec2 destination,
        bool forceUpdate = false
    ) {
        var sharedGraph = _room.GetCWT().SharedGraph!;
        if (sharedGraph.GetNode(start) is null && start.y > _room.defaultWaterLevel) {
            Plugin.Logger!.LogDebug($"no node at start ({start.x}, {start.y})");
            return null;
        }
        if (sharedGraph.GetNode(destination) is null && destination.y > _room.defaultWaterLevel) {
            Plugin.Logger!.LogDebug($"no node at destination ({destination.x}, {destination.y})");
            return null;
        }
        if (start == destination) {
            return null;
        }

        if (_lastDestination != destination || forceUpdate) {
            if (Timers.Active) {
                Timers.FindPathTo_Setup.Start();
            }
            OpenNodes.Reset();
            ClosedNodes.Reset();
            var destNode = PathNodePool[destination];
            if (destNode is null) {
                destNode = new PathNode(destination);
                PathNodePool[destination] = destNode;
            }
            destNode.Reset(null, 0f, 0f, 0f, 0);
            NodeQueue.Reset();
            NodeQueue.Add(destNode);
            OpenNodes[destination] = true;
            _lastDestination = destination;
            int width = _room.Width;
            int height = _room.Height;
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    var node = PathNodePool[x, y];
                    if (node is not null) {
                        node.Threat = 0;
                    }
                }
            }
            if (Timers.Active) {
                Timers.FindPathTo_Setup.Stop();
            }
        } else {
            if (ClosedNodes[start]) {
                return PathNodePool[start]!.Connection;
            } else {
                NodeQueue.ResetHeuristics(start);
            }
        }

        if (Timers.Active) {
            Timers.FindPathTo_MainLoop_Individual.Watch.Start();
            Timers.FindPathTo_MainLoop_Total.Start();
        }

        while (NodeQueue.Count > 0) {
            PathNode currentNode = NodeQueue.Root!;
            var currentPos = currentNode.GridPos;
            if (currentPos == start) {
                if (Timers.Active) {
                    Timers.FindPathTo_MainLoop_Individual.Stop();
                    Timers.FindPathTo_MainLoop_Total.Stop();
                }
                return PathNodePool[start]!.Connection;
            }
            NodeQueue.RemoveRoot();
            OpenNodes[currentPos] = false;
            ClosedNodes[currentPos] = true;
            if (currentNode.DiveLength > DivingLimit) {
                continue;
            }
            if (currentPos.y == _room.defaultWaterLevel) {
                var dir = Consts.IVec2.Left;
                if (_room.GetTile(dir).Terrain != Room.Tile.TerrainType.Solid) {
                    CheckWaterSurfaceConnection(currentNode, dir, false);
                }
                dir = Consts.IVec2.Right;
                if (_room.GetTile(dir).Terrain != Room.Tile.TerrainType.Solid) {
                    CheckWaterSurfaceConnection(currentNode, dir, false);
                }
                foreach (var diveDir in LowerThreeDirections) {
                    if (_room.GetTile(diveDir).Terrain != Room.Tile.TerrainType.Solid) {
                        CheckWaterSurfaceConnection(currentNode, diveDir, false);
                    }
                }
                var graphNode = sharedGraph.GetNode(currentPos);
                var extension = DynamicGraph.Extensions[currentPos.x, currentPos.y];
                if (extension is not null) {
                    foreach (var connection in extension.Value.IncomingConnections) {
                        CheckConnection(currentNode, connection, useHeuristic: true, underwater: false);
                    }
                }
                if (graphNode?.HasVerticalBeam is true) {
                    var climbUp = graphNode.IncomingConnections.Find(
                        c => c.Type is ConnectionType.Climb(IVec2 climbDir)
                        && climbDir == Consts.IVec2.Down
                    );
                    if (climbUp is not null) {
                        CheckConnection(currentNode, climbUp, useHeuristic: true, underwater: false);
                    }
                }
            } else if (currentPos.y < _room.defaultWaterLevel) {
                var graphNode = sharedGraph.GetNode(currentPos);
                if (graphNode?.Type is NodeType.Corridor) {
                    foreach (var connection in graphNode.IncomingConnections) {
                        CheckConnection(currentNode, connection, useHeuristic: true, underwater: true);
                    }
                } else {
                    foreach (var dir in Custom.eightDirections) {
                        if (_room.GetTile(currentPos + dir).Terrain != Room.Tile.TerrainType.Solid) {
                            CheckWaterSurfaceConnection(currentNode, dir, true);
                        }
                    }
                }
            } else {
                var graphNode = sharedGraph.GetNode(currentPos)!;
                var extension = DynamicGraph.Extensions[currentPos.x, currentPos.y]!.Value;
                foreach (var connection in graphNode.IncomingConnections.Concat(extension.IncomingConnections)) {
                    CheckConnection(currentNode, connection, useHeuristic: true, underwater: false);
                }
            }
            if (Timers.Active) {
                Timers.FindPathTo_MainLoop_Individual.Invocations += 1;
            }
        }
        if (Timers.Active) {
            Timers.FindPathTo_MainLoop_Individual.Stop();
            Timers.FindPathTo_MainLoop_Total.Stop();
        }
        return null;
    }

    public (IVec2 destination, PathConnection connection)? FindPathFrom(
        IVec2 start,
        IDestinationFinder finder
    ) {
        var sharedGraph = _room.GetCWT().SharedGraph!;
        if (sharedGraph.GetNode(start) is null) {
            Plugin.Logger!.LogDebug($"no node at start ({start.x}, {start.y})");
            return null;
        }
        if (Timers.Active) {
            Timers.FindPathFrom_Setup.Start();
        }
        OpenNodes.Reset();
        ClosedNodes.Reset();
        var startNode = PathNodePool[start]!;
        startNode.Reset(null, 0f, 0f, 0f, 0);
        NodeQueue.Reset();
        NodeQueue.Add(startNode);
        OpenNodes[start] = true;
        _lastDestination = null;
        int width = _room.Width;
        int height = _room.Height;
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                var node = PathNodePool[x, y];
                if (node is not null) {
                    node.Threat = 0;
                }
            }
        }
        if (Timers.Active) {
            Timers.FindPathFrom_Setup.Stop();
        }

        if (Timers.Active) {
            Timers.FindPathFrom_MainLoop_Individual.Watch.Start();
            Timers.FindPathFrom_MainLoop_Total.Start();
        }

        while (NodeQueue.Count > 0) {
            var currentNode = NodeQueue.Root!;
            var currentPos = currentNode.GridPos;
            if (finder.StopSearching(currentNode)) {
                if (Timers.Active) {
                    Timers.FindPathFrom_MainLoop_Individual.Stop();
                    Timers.FindPathFrom_MainLoop_Total.Stop();
                }
                _lastDestination = finder.Destination();
                if (_lastDestination is null || _lastDestination.Value == start) {
                    return null;
                }
                ReversePath(currentNode);
                return (_lastDestination.Value, PathNodePool[start]!.Connection!.Value);
            }
            NodeQueue.RemoveRoot();
            OpenNodes[currentPos] = false;
            ClosedNodes[currentPos] = true;

            var graphNode = sharedGraph.Nodes[currentPos.x, currentPos.y]!;
            var extension = DynamicGraph.Extensions[currentPos.x, currentPos.y]!.Value;

            foreach (var connection in graphNode.OutgoingConnections.Concat(extension.OutgoingConnections)) {
                CheckConnection(currentNode, connection, useHeuristic: false, underwater: false);
            }

            if (Timers.Active) {
                Timers.FindPathTo_MainLoop_Individual.Invocations += 1;
            }
        }
        if (Timers.Active) {
            Timers.FindPathFrom_MainLoop_Individual.Stop();
            Timers.FindPathFrom_MainLoop_Total.Stop();
        }
        _lastDestination = finder.Destination();
        if (PathNodePool[start] is null) {
            Plugin.Logger!.LogDebug("start node null");
            return null;
        } else if (PathNodePool[start]!.Connection is null) {
            Plugin.Logger!.LogDebug("start connection null");
            return null;
        }
        return _lastDestination is null ? null : (_lastDestination.Value, PathNodePool[start]!.Connection!.Value);
    }

    private void CheckWaterSurfaceConnection(PathNode currentNode, IVec2 direction, bool underwater) {
        IVec2 neighbourPos = currentNode.GridPos + direction;
        PathNode? currentNeighbour = PathNodePool[neighbourPos];
        if (currentNeighbour is null) {
            var node = new PathNode(neighbourPos);
            PathNodePool[neighbourPos] = node;
            currentNeighbour = node;
        }

        if (ClosedNodes[neighbourPos]) {
            return;
        }

        if (!OpenNodes[neighbourPos]) {
            OpenNodes[neighbourPos] = true;
            currentNeighbour.Reset(
                new PathConnection(new ConnectionType.Swim(direction.Inverse()), currentNode),
                currentNode.PathCost + 1,
                _threatTracker.ThreatAtTile(neighbourPos),
                currentNode.GridPos.FloatDist(neighbourPos),
                underwater ? currentNode.DiveLength + 1 : 0
            );
            NodeQueue.Add(currentNeighbour);
        }

        if (currentNode.PathCost + currentNode.Threat + 1
            < currentNeighbour.PathCost + currentNeighbour.Threat
        ) {
            currentNeighbour.PathCost = currentNode.PathCost + 1;
            currentNeighbour.Connection = new PathConnection(new ConnectionType.Swim(direction.Inverse()), currentNode);
            NodeQueue.DecreasePriority(currentNeighbour.GridPos);
        }
    }

    private void CheckConnection(PathNode currentNode, NodeConnection connection, bool useHeuristic, bool underwater) {
        IVec2 neighbourPos = connection.Next.GridPos;
        PathNode currentNeighbour = PathNodePool[neighbourPos]!;
        if (ClosedNodes[neighbourPos]) {
            return;
        }

        if (!OpenNodes[neighbourPos]) {
            OpenNodes[neighbourPos] = true;
            currentNeighbour.Reset(
                new PathConnection(connection.Type, currentNode),
                currentNode.PathCost + connection.Weight,
                _threatTracker.ThreatAtTile(neighbourPos),
                useHeuristic ? currentNode.GridPos.FloatDist(neighbourPos) : 0,
                underwater ? currentNode.DiveLength + 1 : 0
            );
            NodeQueue.Add(currentNeighbour);
        }

        if (currentNode.PathCost + currentNode.Threat + connection.Weight
            < currentNeighbour.PathCost + currentNeighbour.Threat
        ) {
            currentNeighbour.PathCost = currentNode.PathCost + connection.Weight;
            currentNeighbour.Connection = new PathConnection(connection.Type, currentNode);
            NodeQueue.DecreasePriority(currentNeighbour.GridPos);
        }
    }

    private void ReversePath(PathNode destNode) {
        OpenNodes.Reset();
        ClosedNodes.Reset();
        ClosedNodes[destNode.GridPos] = true;
        NodeQueue.Reset();
        PathNode? previousNode = null;
        PathNode? currentNode = destNode;
        PathNode? nextNode = null;
        ConnectionType? previousType = null;
        ConnectionType? currentType;
        while (currentNode is not null) {
            nextNode = currentNode.Connection?.Next;
            if (nextNode is not null) {
                ClosedNodes[nextNode.GridPos] = true;
            }
            currentType = currentNode.Connection?.Type;
            currentNode.Connection = previousType is null ? null : new PathConnection(previousType, previousNode!);
            previousType = currentType;
            previousNode = currentNode;
            currentNode = nextNode;
        }
    }

    public class ThreatMapVisualizer {
        private Room _room;
        private readonly Pathfinder _pathfinder;
        private FLabel?[,] _labels;
        public bool Active { get; private set; }

        public ThreatMapVisualizer(Pathfinder pathfinder) {
            _pathfinder = pathfinder;
            _room = _pathfinder._room;
            _labels = new FLabel[_room.Width, _room.Height];
            CreateLabels();
        }

        private void CreateLabels() {
            int width = _room.Width;
            int height = _room.Height;
            var container = _room!.game.cameras[0].ReturnFContainer("Foreground");
            var camPos = _room.game.cameras[0].pos;
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    var node = _pathfinder.PathNodePool[x, y];
                    if (node is not null) {
                        var label = new FLabel(Custom.GetFont(), ((int)node.Threat).ToString()) {
                            isVisible = false
                        };
                        label.SetPosition(RoomHelper.MiddleOfTile(x, y) - camPos);
                        _labels[x, y] = label;
                        container.AddChild(label);
                    }
                }
            }
        }

        public void NewRoom(Room room) {
            if (_room == room) {
                _room = room;
                _labels = new FLabel[_room.Width, _room.Height];
                CreateLabels();
            }
        }

        public void Display() {
            Active = true;
            int width = _room.Width;
            int height = _room.Height;
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    var label = _labels[x, y];
                    var node = _pathfinder.PathNodePool[x, y];
                    if (label is null || node is null) {
                        continue;
                    }
                    if (node.Threat > 0) {
                        label.isVisible = true;
                        float normalizedThreat = Mathf.InverseLerp(0, 10, node.Threat);
                        label.color = new Color(
                            normalizedThreat,
                            1 - normalizedThreat,
                            0,
                            1
                        );
                        label.text = ((int)node.Threat).ToString();
                    } else {
                        label.isVisible = false;
                    }
                }

            }
        }

        public void Hide() {
            Active = false;
            foreach (var label in _labels) {
                if (label is not null) {
                    label.isVisible = false;
                }
            }
        }
    }
}