using System.Collections.Generic;

using UnityEngine;

using IVec2 = RWCustom.IntVector2;

namespace JumpSlug.Pathfinding;

class SharedGraphVisualizer {
    private Room _room;
    private readonly List<DebugSprite> _nodeSprites;
    private readonly List<DebugSprite> _connectionSprites;
    public bool VisualizingNodes { get; private set; }
    public bool VisualizingConnections { get; private set; }
    public SharedGraphVisualizer(Room room) {
        _room = room;
        _nodeSprites = new();
        _connectionSprites = new();
    }

    public void NewRoom(Room room) {
        if (_room != room) {
            _room = room;
            ResetNodeSprites();
            ResetConnectionSprites();
        }
    }

    public void ToggleNodes() {
        var graph = _room.GetCWT().SharedGraph!;
        if (VisualizingNodes) {
            ResetNodeSprites();
            return;
        }
        VisualizingNodes = true;
        foreach (var node in graph.Nodes) {
            if (node is null) {
                continue;
            }

            var color = node.Type switch {
                NodeType.Air => Color.red,
                NodeType.Floor => Color.white,
                NodeType.Slope => Color.green,
                NodeType.Corridor => Color.blue,
                NodeType.ShortcutEntrance => Color.cyan,
                NodeType.Wall => Color.grey,
                _ => throw new InvalidUnionVariantException("unsupported NodeType variant"),
            };

            var pos = RoomHelper.MiddleOfTile(node.GridPos);
            var fs = new FSprite("pixel") {
                color = color,
                scale = 5f,
            };
            var sprite = new DebugSprite(pos, fs, _room);
            _room.AddObject(sprite);
            _nodeSprites.Add(sprite);
        }
    }

    public void ToggleConnections() {
        var graph = _room.GetCWT().SharedGraph!;
        if (VisualizingConnections) {
            foreach (var sprite in _connectionSprites) {
                sprite.Destroy();
            }
            _connectionSprites.Clear();
            VisualizingConnections = false;
            return;
        }
        VisualizingConnections = true;
        foreach (var node in graph.Nodes) {
            if (node is null) {
                continue;
            }
            var start = RoomHelper.MiddleOfTile(node.GridPos);
            foreach (var connection in node.Connections) {
                var end = RoomHelper.MiddleOfTile(connection.Next.GridPos);
                var mesh = LineHelper.MakeLine(start, end, Color.white);
                var line = new DebugSprite(start, mesh, _room);
                _room.AddObject(line);
                _connectionSprites.Add(line);
            }
        }
    }

    private void ResetConnectionSprites() {
        foreach (var sprite in _connectionSprites) {
            sprite.Destroy();
        }
        _connectionSprites.Clear();
        VisualizingConnections = false;
    }

    private void ResetNodeSprites() {
        foreach (var sprite in _nodeSprites) {
            sprite.Destroy();
        }
        _nodeSprites.Clear();
        VisualizingNodes = false;
    }
}

class PathVisualizer {
    private Room _room;
    private readonly List<DebugSprite> _pathSprites;
    public bool VisualizingPath { get; private set; }

    public PathVisualizer(Room room) {
        _room = room;
        _pathSprites = new();
    }

    public void NewRoom(Room room) {
        if (room != _room) {
            _room = room;
            ResetSprites();
        }
    }

    public void TogglePath(Path? path, SlugcatDescriptor slugcat) {
        if (VisualizingPath
            || path is null
            || path.NodeCount <= 2
        ) {
            ResetSprites();
            return;
        }
        VisualizingPath = true;
        for (int i = 0; i < path!.ConnectionCount; i++) {
            IVec2 startTile = path.Nodes[i + 1];
            IVec2 endTile = path.Nodes[i];
            var start = RoomHelper.MiddleOfTile(startTile);
            var end = RoomHelper.MiddleOfTile(endTile);
            var color = path.Connections[i] switch {
                ConnectionType.Jump
                or ConnectionType.WalkOffEdge
                or ConnectionType.Pounce => Color.blue,
                ConnectionType.Drop => Color.red,
                ConnectionType.Shortcut => Color.cyan,
                ConnectionType.Crawl => Color.green,
                ConnectionType.Climb => Color.magenta,
                ConnectionType.Walk => Color.white,
                ConnectionType.SlideOnWall => Color.yellow,
                _ => throw new InvalidUnionVariantException("unsupported NodeType variant"),
            };
            var connection = path.Connections[i];
            var sharedGraph = _room.GetCWT().SharedGraph!;
            if (connection is ConnectionType.Jump) {
                // this node can be null only if the path is constructed incorrectly so this should throw
                GraphNode graphNode = sharedGraph.GetNode(startTile)!;
                if (graphNode.VerticalBeam && !graphNode.HorizontalBeam) {
                    var v0 = slugcat.VerticalPoleJumpVector();
                    VisualizeJump(v0, startTile, endTile);
                } else if (graphNode.HorizontalBeam || graphNode.Type is NodeType.Floor) {
                    var headPos = new IVec2(startTile.x, startTile.y + 1);
                    var v0 = slugcat.HorizontalPoleJumpVector();
                    VisualizeJump(v0, headPos, endTile);
                    var preLine = LineHelper.MakeLine(start, RoomHelper.MiddleOfTile(headPos), Color.white);
                    var preSprite = new DebugSprite(start, preLine, _room);
                    _pathSprites.Add(preSprite);
                    _room.AddObject(preSprite);
                } else if (graphNode.Type is NodeType.Wall wall) {
                    Vector2 v0 = slugcat.WallJumpVector(wall.Direction);
                    VisualizeJump(v0, startTile, endTile);
                }
            } else if (connection is ConnectionType.WalkOffEdge) {
                var startPos = new IVec2(
                    startTile.x,
                    sharedGraph.GetNode(startTile)?.Type is NodeType.Corridor ? startTile.y : startTile.y + 1
                );
                var v0 = slugcat.HorizontalCorridorFallVector();
                VisualizeJump(v0, startPos, endTile);
                var preLine = LineHelper.MakeLine(start, RoomHelper.MiddleOfTile(startPos), Color.white);
                var preSprite = new DebugSprite(start, preLine, _room);
                _pathSprites.Add(preSprite);
                _room.AddObject(preSprite);
            }
            var mesh = LineHelper.MakeLine(start, end, color);
            var sprite = new DebugSprite(start, mesh, _room);
            _room.AddObject(sprite);
            _pathSprites.Add(sprite);
        }
    }

    private void ResetSprites() {
        foreach (var sprite in _pathSprites) {
            sprite.Destroy();
        }
        _pathSprites.Clear();
        VisualizingPath = false;
    }

    private void VisualizeJump(Vector2 v0, IVec2 startTile, IVec2 endTile) {
        Vector2 pathOffset = RoomHelper.MiddleOfTile(startTile);
        Vector2 lastPos = pathOffset;
        float maxT = 20 * (endTile.x - startTile.x) / v0.x;
        for (float t = 0; t < maxT; t += 2f) {
            var nextPos = new Vector2(pathOffset.x + v0.x * t, DynamicGraph.Parabola(pathOffset.y, v0, _room.gravity, t));
            var sprite = new DebugSprite(lastPos, LineHelper.MakeLine(lastPos, nextPos, Color.white), _room);
            _pathSprites.Add(sprite);
            _room.AddObject(sprite);
            lastPos = nextPos;
        }
        var postLine = LineHelper.MakeLine(lastPos, _room.MiddleOfTile(endTile), Color.white);
        var postSprite = new DebugSprite(lastPos, postLine, _room);
        _pathSprites.Add(postSprite);
        _room.AddObject(postSprite);
    }
}

/// <summary>
/// Contains copy of main pathfinder logic but only runs one loop iteration per frame while visualizing pathfinder state
/// </summary>
public class DebugPathfinder {
    private Room _room;
    private readonly DebugSprite?[,] _nodeSprites;
    private readonly DebugSprite?[,] _connectionSprites;
    private SlugcatDescriptor _descriptor;
    private PathNodePool _pathNodePool;
    private BitGrid _openNodes;
    private BitGrid _closedNodes;
    private PathNodeQueue _nodeQueue;
    private readonly DynamicGraph _dynamicGraph;
    public IVec2 Start { get; private set; }
    public IVec2 Destination { get; private set; }
    public bool IsInit { get; private set; }
    public bool IsFinished { get; private set; }


    /// <summary>
    /// Create new pathfinder in the specified room.
    /// </summary>
    /// <param name="room">
    /// the room the pathfinder will try to find paths through
    /// </param>
    /// <param name="descriptor">
    /// relevant information about the slugcat using this pathfinder.
    /// </param>
    public DebugPathfinder(Room room, SlugcatDescriptor descriptor) {
        var sharedGraph = room.GetCWT().SharedGraph!;
        _room = room;
        _descriptor = descriptor;
        _dynamicGraph = new DynamicGraph(room);
        _pathNodePool = new PathNodePool(sharedGraph);
        _nodeSprites = new DebugSprite[_pathNodePool.Width, _pathNodePool.Height];
        _connectionSprites = new DebugSprite[_pathNodePool.Width, _pathNodePool.Height];
        for (int y = 0; y < sharedGraph.Height; y++) {
            for (int x = 0; x < sharedGraph.Width; x++) {
                if (_pathNodePool[x, y] is not null) {
                    var pos = RoomHelper.MiddleOfTile(x, y);
                    var nodeSprite = new DebugSprite(
                        pos,
                        new FSprite("pixel") {
                            scale = 5f,
                            isVisible = false,
                        },
                        room
                    );
                    _nodeSprites[x, y] = nodeSprite;
                    room.AddObject(nodeSprite);
                    var connectionSprite = new DebugSprite(
                        pos,
                        TriangleMesh.MakeLongMesh(1, false, true),
                        room
                    );
                    connectionSprite.sprite.isVisible = false;
                    _connectionSprites[x, y] = connectionSprite;
                    room.AddObject(connectionSprite);
                }
            }
        }
        _openNodes = new BitGrid(_dynamicGraph.Width, _dynamicGraph.Height);
        _closedNodes = new BitGrid(_dynamicGraph.Width, _dynamicGraph.Height);
        _nodeQueue = new PathNodeQueue(
            _pathNodePool.NonNullCount,
            _pathNodePool.Width,
            _pathNodePool.Height
        );
    }

    /// <summary>
    /// Reinitialize pathfinder for new room. Does nothing if the room has not actually changed.
    /// </summary>
    public void NewRoom(Room room) {
        if (room != _room) {
            _room = room;
            _dynamicGraph.NewRoom(room);
            _pathNodePool = new PathNodePool(room.GetCWT().SharedGraph!);
            _nodeQueue = new PathNodeQueue(
                _pathNodePool.NonNullCount,
                _pathNodePool.Width,
                _pathNodePool.Height
            );
            Reset();
            foreach (var nodeSprite in _nodeSprites) {
                if (nodeSprite is not null) {
                    room.AddObject(nodeSprite);
                }
            }
            foreach (var connectionSprite in _connectionSprites) {
                if (connectionSprite is not null) {
                    room.AddObject(connectionSprite);
                }
            }
        }
    }

    public void Reset() {
        IsInit = false;
        _openNodes = new BitGrid(_dynamicGraph.Width, _dynamicGraph.Height);
        _closedNodes = new BitGrid(_dynamicGraph.Width, _dynamicGraph.Height);
        foreach (var nodeSprite in _nodeSprites) {
            if (nodeSprite is not null) {
                nodeSprite.sprite.color = Color.white;
                nodeSprite.sprite.isVisible = false;
            }
        }
        foreach (var connectionSprite in _connectionSprites) {
            if (connectionSprite is not null) {
                connectionSprite.sprite.isVisible = false;
            }
        }
    }

    public void InitPathfinding(IVec2 start, IVec2 destination, SlugcatDescriptor descriptor) {
        var sharedGraph = _room.GetCWT().SharedGraph!;
        if (sharedGraph.GetNode(start) is null) {
            return;
        }
        if (sharedGraph.GetNode(destination) is null) {
            return;
        }
        Start = start;
        Destination = destination;
        if (_descriptor != descriptor) {
            for (int y = 0; y < _dynamicGraph.Height; y++) {
                for (int x = 0; x < _dynamicGraph.Width; x++) {
                    _dynamicGraph.AdjacencyLists[x, y]?.Clear();
                }
            }
            _descriptor = descriptor;
        }
        var startNode = _pathNodePool[Start]!;
        startNode.Reset(Destination, null, 0);
        _nodeQueue.Reset();
        _nodeQueue.Add(startNode);
        _openNodes[Start] = true;
        var startNodeSprite = _nodeSprites[Start.x, Start.y]!.sprite;
        startNodeSprite.isVisible = true;
        startNodeSprite.scale = 10f;
        startNodeSprite.color = Color.red;
        var destNodeSprite = _nodeSprites[Destination.x, Destination.y]!.sprite;
        destNodeSprite.isVisible = true;
        destNodeSprite.color = Color.blue;
        IsInit = true;
        IsFinished = false;
        return;
    }

    public void Poll() {
        if (!IsInit || IsFinished) {
            return;
        }
        if (_nodeQueue.Count > 0) {
            if (!_nodeQueue.Validate()) {
                IsFinished = true;
                return;
            }
            PathNode currentNode = _nodeQueue.Root!;
            var currentPos = currentNode.GridPos;
            if (currentPos == Destination) {
                PathNode? cursor = _pathNodePool[Destination];
                if (cursor is null) {
                    return;
                }
                while (cursor.Connection is not null) {
                    _connectionSprites[cursor.GridPos.x, cursor.GridPos.y]!.sprite.color = Color.red;
                    cursor = cursor.Connection.Value.Next;
                }
                IsFinished = true;
            }
            _nodeQueue.RemoveRoot();
            _openNodes[currentPos] = false;
            _closedNodes[currentPos] = true;
            _nodeSprites[currentPos.x, currentPos.y]!.sprite.scale = 5f;

            var graphNode = _room.GetCWT().SharedGraph!.Nodes[currentPos.x, currentPos.y]!;
            var adjacencyList = _dynamicGraph.AdjacencyLists[currentPos.x, currentPos.y]!;

            if (adjacencyList.Count == 0) {
                _dynamicGraph.TraceFromNode(currentPos, _descriptor);
            }

            void CheckConnection(NodeConnection connection) {
                IVec2 neighbourPos = connection.Next.GridPos;
                PathNode currentNeighbour = _pathNodePool[neighbourPos]!;
                if (_closedNodes[neighbourPos]) {
                    return;
                }
                if (!_openNodes[neighbourPos]) {
                    _openNodes[neighbourPos] = true;
                    currentNeighbour.Reset(
                        Destination,
                        new PathConnection(connection.Type, currentNode),
                        currentNode.PathCost + connection.Weight
                    );
                    _nodeQueue.Add(currentNeighbour);
                    var neighbourNodeSprite = _nodeSprites[neighbourPos.x, neighbourPos.y]!.sprite;
                    neighbourNodeSprite.isVisible = true;
                    neighbourNodeSprite.scale = 10f;
                    var neighbourConnectionSprite = _connectionSprites[neighbourPos.x, neighbourPos.y]!.sprite;
                    neighbourConnectionSprite.isVisible = true;
                    if (connection.Type
                        is ConnectionType.Jump
                        or ConnectionType.Drop
                        or ConnectionType.WalkOffEdge
                        or ConnectionType.Pounce
                    ) {
                        neighbourConnectionSprite.color = Color.grey;
                    } else {
                        neighbourConnectionSprite.color = Color.white;
                    }
                    var currentScreenPos = RoomHelper.MiddleOfTile(currentPos);
                    var neighbourScreenPos = RoomHelper.MiddleOfTile(neighbourPos);
                    LineHelper.ReshapeLine(
                        (TriangleMesh)neighbourConnectionSprite,
                        neighbourScreenPos,
                        currentScreenPos
                    );
                }
                if (currentNode.PathCost + connection.Weight < currentNeighbour.PathCost) {
                    currentNeighbour.PathCost = currentNode.PathCost + connection.Weight;
                    currentNeighbour.Connection = new PathConnection(connection.Type, currentNode);
                    _nodeQueue.DecreasePriority(currentNeighbour.GridPos);
                    var neighbourConnectionSprite = _connectionSprites[neighbourPos.x, neighbourPos.y]!.sprite;
                    neighbourConnectionSprite.isVisible = true;
                    if (connection.Type
                        is ConnectionType.Jump
                        or ConnectionType.Drop
                        or ConnectionType.WalkOffEdge
                        or ConnectionType.Pounce
                    ) {
                        neighbourConnectionSprite.color = Color.grey;
                    } else {
                        neighbourConnectionSprite.color = Color.white;
                    }
                    var currentScreenPos = RoomHelper.MiddleOfTile(currentPos);
                    var neighbourScreenPos = RoomHelper.MiddleOfTile(neighbourPos);
                    LineHelper.ReshapeLine(
                        (TriangleMesh)neighbourConnectionSprite,
                        neighbourScreenPos,
                        currentScreenPos
                    );
                }
            }

            foreach (var connection in graphNode.Connections) {
                CheckConnection(connection);
            }
            foreach (var connection in adjacencyList) {
                CheckConnection(connection);
            }
            IsFinished = false;
        } else {
            IsFinished = true;
        }
    }
}

static class VisualizerHooks {
    public static void RegisterHooks() {
        On.Player.Update += Player_Update;
        On.Player.NewRoom += Player_NewRoom;
    }

    public static void UnregisterHooks() {
        On.Player.Update -= Player_Update;
        On.Player.NewRoom -= Player_NewRoom;
    }

    private static void Player_NewRoom(On.Player.orig_NewRoom orig, Player self, Room room) {
        orig(self, room);
        self.GetCWT().DebugPathfinder?.NewRoom(room);
    }

    private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu) {
        var debugPathfinder = self.GetCWT().DebugPathfinder;
        if (debugPathfinder is null) {
            if (InputHelper.JustPressed(KeyCode.V)) {
                self.GetCWT().DebugPathfinder = new DebugPathfinder(self.room, new SlugcatDescriptor(self));
            }
        } else {
            if (debugPathfinder.IsInit && !debugPathfinder.IsFinished) {
                debugPathfinder.Poll();
            }
            if (InputHelper.JustPressedMouseButton(0)) {
                debugPathfinder.Reset();
                var mousePos = (Vector2)Input.mousePosition + self.room.game.cameras[0].pos;
                debugPathfinder.InitPathfinding(
                    RoomHelper.TilePosition(self.bodyChunks[1].pos),
                    RoomHelper.TilePosition(mousePos),
                    new SlugcatDescriptor(self)
                );
            }
        }
        orig(self, eu);
    }
}