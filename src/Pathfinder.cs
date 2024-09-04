using System;
using System.Text;
using System.Collections.Generic;

using MoreSlugcats;

using UnityEngine;

using IVec2 = RWCustom.IntVector2;

namespace JumpSlug.Pathfinding;

/// <summary>
/// Represents the path generated by the pathfinder in a format that is easier for the AI to consume
/// and allows it to be easily iterated over.
/// </summary>
public class Path {
    // these aren't private because it's more straightforward for the visualizer to use manual indexing
    // and the accessors can't be made const correct
    public int Cursor;
    public readonly List<IVec2> Nodes;
    public readonly List<ConnectionType> Connections;

    public int NodeCount => Nodes.Count;
    public int ConnectionCount => Connections.Count;

    /// <summary>
    /// Creates new path from the raw path generated by the pathfinder.
    /// </summary>
    /// <param name="endNode">the node at the destination of the path.</param>
    /// <param name="sharedGraph">the room specific graph the path was generated for.</param>
    public Path(PathNode endNode) {
        Nodes = new();
        Connections = new();
        var currentNode = endNode;
        while (currentNode is not null) {
            Nodes.Add(currentNode.GridPos);
            if (currentNode.Connection is not null) {
                Connections.Add(currentNode.Connection.Value.Type);
            }
            currentNode = currentNode.Connection?.Next;
        }
        Cursor = Nodes.Count - 1;
    }

    /// <summary>
    /// Access node at the current cursor position.
    /// </summary>
    /// <returns>
    /// Null if the cursor has moved past the end of the path.
    /// </returns>
    public IVec2? CurrentNode() {
        if (Cursor < 0) {
            return null;
        }
        return Nodes[Cursor];
    }
    /// <summary>
    /// Inspect previous or upcoming nodes without moving the cursor.
    /// </summary>
    /// <param name="offset">
    /// Offset from the cursor position, positive values look ahead, negative value look back.
    /// </param>
    /// <returns>
    /// Null if the specified offset falls outside the path.
    /// </returns>
    public IVec2? PeekNode(int offset) {
        if (Cursor - offset < 0) {
            return null;
        }
        return Nodes[Cursor - offset];
    }
    /// <summary>
    /// Access outgoing connection from the current cursor position.
    /// </summary>
    /// <returns>
    /// Null if the cursor has moved past the end of the path.
    /// </returns>
    public ConnectionType? CurrentConnection() {
        if (Cursor < 1) {
            return null;
        }
        return Connections[Cursor - 1];
    }
    /// <summary>
    /// Inspect outgoing connection from previous or upcoming nodes without moving the cursor.
    /// </summary>
    /// <param name="offset">
    /// Offset from the cursor position, positive values look ahead, negative value look back.
    /// </param>
    /// <returns>
    /// Null if the specified offset falls outside the path.
    /// </returns>
    public ConnectionType? PeekConnection(int offset) {
        if (Cursor - offset < 1) {
            return null;
        }
        return Connections[Cursor - offset - 1];
    }
    /// <summary>
    /// Move the cursor forward by one step.
    /// </summary>
    public void Advance() {
        Cursor -= 1;
    }

    public bool FindNodeAhead(IVec2 node) {
        int initialCursor = Cursor;
        while (Cursor >= 0) {
            if (CurrentNode() == node) {
                return true;
            } else {
                Advance();
            }
        }
        Cursor = initialCursor;
        return false;
    }

    public bool FindEitherNodeAhead(IVec2 primary, IVec2 secondary) {
        int initialCursor = Cursor;
        while (Cursor >= 0) {
            if (CurrentNode() == primary || CurrentNode() == secondary) {
                return true;
            } else {
                Advance();
            }
        }
        Cursor = initialCursor;
        return false;
    }

    public bool FindNodeBehind(IVec2 node) {
        int initialCursor = Cursor;
        Cursor = Nodes.Count - 1;
        while (Cursor > initialCursor) {
            if (CurrentNode() == node) {
                return true;
            } else {
                Advance();
            }
        }
        return false;
    }

    public bool FindEitherNodeBehind(IVec2 primary, IVec2 secondary) {
        int initialCursor = Cursor;
        Cursor = Nodes.Count - 1;
        while (Cursor > initialCursor) {
            if (CurrentNode() == primary || CurrentNode() == secondary) {
                return true;
            } else {
                Advance();
            }
        }
        return false;
    }

    public bool FindNode(IVec2 node) => FindNodeAhead(node) || FindNodeBehind(node);

    public bool FindEitherNode(IVec2 primary, IVec2 secondary) {
        return FindEitherNodeAhead(primary, secondary) || FindEitherNodeBehind(primary, secondary);
    }
}

/// <summary>
/// Node used by the pathfinding algorithm when generating paths.
/// </summary>
public class PathNode {
    public IVec2 GridPos { get; }
    public PathConnection? Connection;
    public float PathCost;

    public float Heuristic { get; private set; }
    public float FCost => PathCost + Heuristic;

    /// <summary>
    /// Create new node at specified grid position.
    /// </summary>
    public PathNode(int x, int y) {
        GridPos = new IVec2(x, y);
        PathCost = 0;
        Heuristic = 0;
    }

    /// <summary>
    /// Overwrite all values except position.
    /// </summary>
    /// <param name="destination">
    /// the grid position of the node the pathfinder is trying to reach.
    /// </param>
    /// <param name="connection">
    /// the new connection to assign to the node.
    /// </param>
    /// <param name="cost">
    /// the new path cost to assign to the node.
    /// </param>
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

    public readonly Vector2 FloorJumpVector(int direction) {
        return new Vector2(
            4.2f * direction * Runspeed * Mathf.Lerp(1, 1.5f, Adrenaline),
            (IsRivulet ? 6f : 4f) * Mathf.Lerp(1, 1.15f, Adrenaline) + JumpBoost(IsPup ? 7 : 8));
    }

    public readonly Vector2 VerticalPoleJumpVector(int direction) {
        Vector2 v0;
        if (IsRivulet) {
            v0 = new Vector2(direction * 9f, 9f) * Mathf.Lerp(1, 1.15f, Adrenaline);
        } else if (IsPup) {
            v0 = new Vector2(direction * 5f, 7f) * Mathf.Lerp(1, 1.15f, Adrenaline);
        } else {
            v0 = new Vector2(direction * 6f, 8f) * Mathf.Lerp(1, 1.15f, Adrenaline);
        }
        return v0;
    }

    public readonly Vector2 HorizontalPoleJumpVector(int direction) {
        return new Vector2(
            4.2f * direction * Runspeed * Mathf.Lerp(1, 1.5f, Adrenaline),
            (IsRivulet ? 6f : 4f) * Mathf.Lerp(1, 1.15f, Adrenaline) + JumpBoost(IsPup ? 7 : 8)
        );
    }

    public readonly Vector2 HorizontalCorridorFallVector(int direction) {
        return new Vector2(
            4.2f * direction * Runspeed * Mathf.Lerp(1, 1.5f, Adrenaline),
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
        int index = 0;
        int leftIndex = 1;
        int rightIndex = 2;
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
        for (int j = 0; j < Count; j++) {
            validationString.Append(_nodes[j].FCost);
            validationString.Append(" ");
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

    public readonly PathNode? this[int x, int y] => _array[x, y];
    public readonly PathNode? this[IVec2 pos] => _array[pos.x, pos.y];
}

public class Pathfinder {
    private Room _room;
    private SlugcatDescriptor _lastDescriptor;
    public readonly DynamicGraph DynamicGraph;

    /// <summary>
    /// Create new pathfinder in the specified room.
    /// </summary>
    /// <param name="room">
    /// the room the pathfinder will try to find paths through
    /// </param>
    /// <param name="descriptor">
    /// relevant information about the slugcat using this pathfinder.
    /// </param>
    public Pathfinder(Room room, SlugcatDescriptor descriptor) {
        _room = room;
        _lastDescriptor = descriptor;
        DynamicGraph = new DynamicGraph(room);
    }

    /// <summary>
    /// Reinitialize pathfinder for new room. Does nothing if the room has not actually changed.
    /// </summary>
    public void NewRoom(Room room) {
        if (room != _room) {
            _room = room;
            DynamicGraph.NewRoom(room);
        }
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
    public Path? FindPath(IVec2 start, IVec2 destination, SlugcatDescriptor descriptor) {
        var sharedGraph = _room.GetCWT().SharedGraph!;
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
        if (Timers.Active) {
            Timers.FindPath.Start();
        }
        var pathNodePool = _room.GetCWT().PathNodePool!.Value;
        var openNodes = _room.GetCWT().OpenNodes!;
        openNodes.Reset();
        var closedNodes = _room.GetCWT().ClosedNodes!;
        closedNodes.Reset();
        var startNode = pathNodePool[start]!;
        startNode.Reset(destination, null, 0);
        var nodeQueue = _room.GetCWT().NodeQueue!;
        nodeQueue.Reset();
        nodeQueue.Add(startNode);
        openNodes[start] = true;
        while (nodeQueue.Count > 0) {
            PathNode currentNode = nodeQueue.Root!;
            var currentPos = currentNode.GridPos;
            if (currentPos == destination) {
                if (Timers.Active) {
                    Timers.FindPath.Stop();
                }
                _lastDescriptor = descriptor;
                return new Path(currentNode);
            }
            nodeQueue.RemoveRoot();
            openNodes[currentPos] = false;
            closedNodes[currentPos] = true;

            var graphNode = sharedGraph.Nodes[currentPos.x, currentPos.y]!;
            var adjacencyList = DynamicGraph.AdjacencyLists[currentPos.x, currentPos.y]!;

            if (adjacencyList.Count == 0) {
                DynamicGraph.TraceFromNode(currentPos, descriptor);
            } else if (_lastDescriptor != descriptor) {
                adjacencyList.Clear();
                DynamicGraph.TraceFromNode(currentPos, descriptor);
            }

            void CheckConnection(NodeConnection connection) {
                IVec2 neighbourPos = connection.Next.GridPos;
                PathNode currentNeighbour = pathNodePool[neighbourPos]!;
                if (closedNodes[neighbourPos]) {
                    return;
                }
                if (!openNodes[neighbourPos]) {
                    openNodes[neighbourPos] = true;
                    currentNeighbour.Reset(
                        destination,
                        new PathConnection(connection.Type, currentNode),
                        currentNode.PathCost + connection.Weight
                    );
                    nodeQueue.Add(currentNeighbour);
                }
                if (currentNode.PathCost + connection.Weight < currentNeighbour.PathCost) {
                    currentNeighbour.PathCost = currentNode.PathCost + connection.Weight;
                    currentNeighbour.Connection = new PathConnection(connection.Type, currentNode);
                    nodeQueue.DecreasePriority(currentNeighbour.GridPos);
                }
            }

            foreach (var connection in graphNode.Connections) {
                CheckConnection(connection);
            }
            foreach (var connection in adjacencyList) {
                CheckConnection(connection);
            }
        }
        if (Timers.Active) {
            Timers.FindPath.Stop();
        }
        _lastDescriptor = descriptor;
        return null;
    }
}

static class PathfinderHooks {
    public static void RegisterHooks() {
        On.Player.Update += Player_Update;
    }

    public static void UnregisterHooks() {
        On.Player.Update -= Player_Update;
    }

    private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu) {
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
                var realizedScug = (Player)abstractScug.realizedCreature;
                realizedScug.controller = null;
            }
        }
        orig(self, eu);
    }
}