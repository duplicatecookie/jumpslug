using System.Collections.Generic;
using System;

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

            var color = node.Type.VisualizationColor;

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
            var end = RoomHelper.MiddleOfTile(node.GridPos);
            foreach (var connection in node.Connections) {
                var start = RoomHelper.MiddleOfTile(connection.Next.GridPos);
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

class NodeVisualizer {
    private Room _room;
    private DynamicGraph _dynamicGraph;
    private readonly DebugSprite _nodeSprite;
    private int _connectionIndex;
    private readonly List<DebugSprite> _connectionSprites;
    public NodeVisualizer(Room room, DynamicGraph dynamicGraph) {
        _room = room;
        _dynamicGraph = dynamicGraph;
        _nodeSprite = new DebugSprite(
            Vector2.zero,
            new FSprite("pixel") {
                isVisible = false,
                scale = 10f,
            },
            _room
        );
        _room.AddObject(_nodeSprite);
        _connectionSprites = new();
    }

    public void NewRoom(Room room, DynamicGraph dynamicGraph) {
        if (_room != room) {
            _room = room;
            _dynamicGraph = dynamicGraph;
            _nodeSprite.sprite.isVisible = false;
            _room.AddObject(_nodeSprite);
            foreach (var sprite in _connectionSprites) {
                sprite.sprite.isVisible = false;
                _room.AddObject(sprite);
            }
        }
    }

    public void VisualizeNode(GraphNode node) {
        Vector2 pixelPos = RoomHelper.MiddleOfTile(node.GridPos);
        _nodeSprite.pos = pixelPos;
        _nodeSprite.sprite.color = node.Type.VisualizationColor;
        _connectionIndex = 0;
        foreach (var connection in node.Connections) {
            AddConnection(node.GridPos, connection);
        }
        var dynamicConnections = _dynamicGraph.AdjacencyLists[node.GridPos.x, node.GridPos.y]!;
        foreach (var connection in dynamicConnections) {
            AddConnection(node.GridPos, connection);
        }
        for (int i = _connectionIndex; i < _connectionSprites.Count; i++) {
            _connectionSprites[i].sprite.isVisible = false;
        }
    }

    private void AddConnection(IVec2 pos, NodeConnection connection) {
        if (connection.Next is null) {
            return;
        }
        Vector2 pixelPos = RoomHelper.MiddleOfTile(pos);
        var color = connection.Type.VisualizationColor;
        if (_connectionIndex >= _connectionSprites.Count) {
            var sprite = new DebugSprite(
                    pixelPos,
                    LineHelper.MakeLine(
                        pixelPos,
                        RoomHelper.MiddleOfTile(connection.Next.GridPos),
                        color
                    ),
                    _room
                );
            _connectionSprites.Add(
                sprite
            );
            _room.AddObject(sprite);
            _connectionIndex = _connectionSprites.Count;
        } else {
            var sprite = _connectionSprites[_connectionIndex];
            sprite.pos = pixelPos;
            LineHelper.ReshapeLine(
                (TriangleMesh)sprite.sprite,
                pixelPos,
                RoomHelper.MiddleOfTile(connection.Next.GridPos)
            );
            sprite.sprite.color = color;
            sprite.sprite.isVisible = true;
            _connectionIndex += 1;
        }
    }

    public void ResetSprites() {
        _nodeSprite.sprite.isVisible = false;
        _connectionIndex = 0;
        foreach (var sprite in _connectionSprites) {
            sprite.sprite.isVisible = false;
        }
    }
}

class PathVisualizer {
    private Room _room;
    private readonly Pathfinder _pathfinder;
    private int _spriteCursor;
    private readonly List<DebugSprite> _lineSprites;
    private int _traceCursor;
    private readonly List<DebugSprite> _traceSprites;
    private int _labelCursor;
    private readonly List<FLabel> _weightLabels;
    private FContainer _foreground;

    public PathVisualizer(Room room, Pathfinder pathfinder) {
        _room = room;
        _spriteCursor = 0;
        _lineSprites = new();
        _traceCursor = 0;
        _traceSprites = new();
        _labelCursor = 0;
        _weightLabels = new();
        _foreground = room.game.cameras[0].ReturnFContainer("Foreground");
        _pathfinder = pathfinder;
    }

    public void NewRoom(Room room) {
        if (room != _room) {
            _room = room;
            _foreground = _room.game.cameras[0].ReturnFContainer("Foreground");
            _spriteCursor = 0;
            foreach (var sprite in _lineSprites) {
                sprite.sprite.isVisible = false;
                _room.AddObject(sprite);
            }
            _traceCursor = 0;
            foreach (var sprite in _traceSprites) {
                sprite.sprite.isVisible = false;
                _room.AddObject(sprite);
            }
            _labelCursor = 0;
            foreach (var label in _weightLabels) {
                label.isVisible = false;
                _foreground.AddChild(label);
            }
        }
    }

    public void ClearPath() {
        _spriteCursor = 0;
        foreach (var sprite in _lineSprites) {
            sprite.sprite.isVisible = false;
        }

        _traceCursor = 0;
        foreach (var sprite in _traceSprites) {
            sprite.sprite.isVisible = false;
        }

        _labelCursor = 0;
        foreach (var label in _weightLabels) {
            label.isVisible = false;
        }
    }

    public void DisplayPath(Path path, SlugcatDescriptor slugcat) {
        ClearPath();

        if (path.NodeCount <= 2) {
            return;
        }

        for (int i = 0; i < path.ConnectionCount; i++) {
            IVec2 startTile = path.Nodes[i];
            IVec2 endTile = path.Nodes[i + 1];
            var start = RoomHelper.MiddleOfTile(startTile);
            var end = RoomHelper.MiddleOfTile(endTile);
            var color = path.Connections[i].VisualizationColor;
            var connectionType = path.Connections[i];
            var sharedGraph = _room.GetCWT().SharedGraph!;
            if (connectionType is ConnectionType.Jump jump) {
                // this node can be null only if the path is constructed incorrectly so this should throw
                GraphNode graphNode = sharedGraph.GetNode(startTile)!;
                Vector2 v0 = Vector2.zero;
                if (graphNode.Beam == GraphNode.BeamType.Vertical) {
                    v0 = slugcat.VerticalPoleJumpVector(jump.Direction);
                    VisualizeJump(v0, startTile, endTile);
                    VisualizeJumpTracing(v0, startTile);
                } else if (graphNode.Beam == GraphNode.BeamType.Horizontal
                    || graphNode.Type is NodeType.Floor
                ) {
                    var headPos = new IVec2(startTile.x, startTile.y + 1);
                    v0 = slugcat.HorizontalPoleJumpVector(jump.Direction);
                    VisualizeJump(v0, headPos, endTile);
                    VisualizeJumpTracing(v0, headPos);
                    AddLine(start, RoomHelper.MiddleOfTile(headPos), Color.white);
                } else if (graphNode.Type is NodeType.Wall wall) {
                    v0 = slugcat.WallJumpVector(wall.Direction);
                    VisualizeJump(v0, startTile, endTile);
                    VisualizeJumpTracing(v0, startTile);
                }
                AddLabel(connectionType, startTile, endTile, v0);
            } else if (connectionType is ConnectionType.WalkOffEdge edgeWalk) {
                var startPos = new IVec2(
                    startTile.x,
                    sharedGraph.GetNode(startTile)?.Type is NodeType.Corridor ? startTile.y : startTile.y + 1
                );
                var v0 = slugcat.HorizontalCorridorFallVector(edgeWalk.Direction);
                VisualizeJump(v0, startPos, endTile);
                VisualizeJumpTracing(v0, startPos);
                AddLine(start, RoomHelper.MiddleOfTile(startPos), Color.white);
                AddLabel(connectionType, startTile, endTile, v0);
            } else if (connectionType is ConnectionType.Drop) {
                AddLabel(connectionType, startTile, endTile, Vector2.zero);
            }
            AddLine(start, end, color);
        }
    }

    private void AddLine(Vector2 start, Vector2 end, Color color) {
        if (_lineSprites.Count == 0 || _lineSprites.Count <= _spriteCursor) {
            var debugSprite = new DebugSprite(start, LineHelper.MakeLine(start, end, color), _room);
            _lineSprites.Add(debugSprite);
            _room.AddObject(debugSprite);
            _spriteCursor = _lineSprites.Count;
        } else {
            var debugSprite = _lineSprites[_spriteCursor];
            debugSprite.pos = start;
            LineHelper.ReshapeLine((TriangleMesh)debugSprite.sprite, start, end);
            debugSprite.sprite.color = color;
            debugSprite.sprite.isVisible = true;
            _spriteCursor += 1;
        }
    }

    private void AddLabel(ConnectionType connectionType, IVec2 startTile, IVec2 endTile, Vector2 v0) {
        NodeConnection? nodeConnection = null;
        var list = _pathfinder.DynamicGraph.AdjacencyLists[startTile.x, startTile.y];
        if (list is null) {
            return;
        }
        foreach (var connection in list) {
            if (connection.Type == connectionType && connection.Next.GridPos == endTile) {
                nodeConnection = connection;
                break;
            }
        }
        if (nodeConnection is null) {
            return;
        }

        var start = RoomHelper.MiddleOfTile(startTile);
        Vector2 labelPos;

        if (connectionType is ConnectionType.Jump jump) {
            float halfDist = 10 * (endTile.x - startTile.x);
            labelPos = new Vector2(
                start.x + halfDist * jump.Direction,
                DynamicGraph.Parabola(start.y, v0, _room.gravity, halfDist / v0.x)
            ) - _room.game.cameras[0].pos;
        } else if (connectionType is ConnectionType.WalkOffEdge edgeWalk) {
            float halfDist = 10 * (endTile.x - startTile.x);
            labelPos = new Vector2(
                start.x + halfDist * edgeWalk.Direction,
                DynamicGraph.Parabola(start.y, v0, _room.gravity, halfDist / v0.x)
            ) - _room.game.cameras[0].pos;
        } else {
            var end = RoomHelper.MiddleOfTile(endTile);
            labelPos = start + 0.5f * (end - start) - _room.game.cameras[0].pos;
        }

        if (_weightLabels.Count == 0 || _weightLabels.Count <= _labelCursor) {
            var label = new FLabel(RWCustom.Custom.GetFont(), nodeConnection!.Weight.ToString()) {
                alignment = FLabelAlignment.Center,
                color = Color.white,
            };
            label.SetPosition(labelPos);
            _foreground.AddChild(label);
            _weightLabels.Add(label);
            _labelCursor = _weightLabels.Count;
        } else {
            var label = _weightLabels[_labelCursor];
            label.SetPosition(labelPos);
            label.isVisible = true;
            _labelCursor += 1;
        }
    }

    private void AddSquare(IVec2 pos, Color color) {
        if (_traceSprites.Count == 0 || _traceSprites.Count <= _traceCursor) {
            var debugSprite = new DebugSprite(
                RoomHelper.MiddleOfTile(pos),
                new FSprite("pixel") {
                    alpha = 0.3f,
                    scale = 20f,
                    color = color
                },
                _room
            );
            _traceSprites.Add(debugSprite);
            _room.AddObject(debugSprite);
            _traceCursor = _traceSprites.Count;
        } else {
            var debugSprite = _traceSprites[_traceCursor];
            debugSprite.pos = RoomHelper.MiddleOfTile(pos);
            debugSprite.sprite.color = color;
            debugSprite.sprite.isVisible = true;
            _traceCursor += 1;
        }
    }

    private void VisualizeJump(Vector2 v0, IVec2 startTile, IVec2 endTile) {
        Vector2 pathOffset = RoomHelper.MiddleOfTile(startTile);
        Vector2 lastPos = pathOffset;
        float maxT = 20 * (endTile.x - startTile.x) / v0.x;
        for (float t = 0; t < maxT; t += 2f) {
            var nextPos = new Vector2(pathOffset.x + v0.x * t, DynamicGraph.Parabola(pathOffset.y, v0, _room.gravity, t));
            AddLine(lastPos, nextPos, Color.white);
            lastPos = nextPos;
        }
        AddLine(lastPos, _room.MiddleOfTile(endTile), Color.white);
    }

    private void VisualizeJumpTracing(Vector2 v0, IVec2 headPos, bool upright = true) {
        int x = headPos.x;
        int y = headPos.y;
        var sharedGraph = _room.GetCWT().SharedGraph!;
        if (x < 0 || y < 0 || x >= sharedGraph.Width || y >= sharedGraph.Height) {
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
            float result = DynamicGraph.Parabola(pathOffset.y, v0, _room.gravity, t) / 20;
            if (result > y + 1) {
                y++;
            } else if (result < y) {
                if (y - 2 < 0) {
                    break;
                }
                var currentNode = sharedGraph.Nodes[x, upright ? y - 1 : y];
                if (currentNode?.Type is NodeType.Floor or NodeType.Slope) {
                    AddSquare(new IVec2(x, y), Color.cyan);
                }
                if (_room.Tiles[x, upright ? y - 2 : y - 1].Terrain == Room.Tile.TerrainType.Solid) {
                    break;
                }
                y--;
            } else {
                x += direction;
            }

            if (x < 0 || y < 0 || x >= sharedGraph.Width || y >= sharedGraph.Height
                || _room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Solid
                || _room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Slope
            ) {
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
                AddSquare(new IVec2(x, y), Color.cyan);
                break;
            } else if (shiftedNode.Beam == GraphNode.BeamType.Vertical) {
                float poleResult = DynamicGraph.Parabola(pathOffset.y, v0, _room.gravity, (20 * x + 10 - pathOffset.x) / v0.x) / 20;
                if (poleResult > y && poleResult < y + 1) {
                    AddSquare(new IVec2(x, y), Color.cyan);
                }
            } else if (shiftedNode.Beam == GraphNode.BeamType.Horizontal) {
                float leftHeight = DynamicGraph.Parabola(pathOffset.y, v0, _room.gravity, (20 * x - pathOffset.x) / v0.x);
                float rightHeight = DynamicGraph.Parabola(pathOffset.y, v0, _room.gravity, (20 * (x + 1) - pathOffset.x) / v0.x);
                float poleHeight = 20 * y + 10;
                if (direction * leftHeight < direction * poleHeight && direction * poleHeight < direction * rightHeight) {
                    AddSquare(new IVec2(x, y), Color.cyan);
                }
            } else if (shiftedNode.Beam == GraphNode.BeamType.Cross) {
                AddSquare(new IVec2(x, y), Color.cyan);
            }
        }
    }
}

/// <summary>
/// Contains copy of main pathfinder logic but only runs one loop iteration per frame while visualizing pathfinder state
/// </summary>
public class DebugPathfinder {
    private Room _room;
    private DebugSprite?[,] _nodeSprites;
    private DebugSprite?[,] _connectionSprites;
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
    public bool DisplayingSprites { get; private set; }


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
        _dynamicGraph = new DynamicGraph(room, descriptor);
        _pathNodePool = new PathNodePool(sharedGraph);
        _nodeSprites = new DebugSprite[_pathNodePool.Width, _pathNodePool.Height];
        _connectionSprites = new DebugSprite[_pathNodePool.Width, _pathNodePool.Height];
        ResetSprites();
        _openNodes = new BitGrid(_dynamicGraph.Width, _dynamicGraph.Height);
        _closedNodes = new BitGrid(_dynamicGraph.Width, _dynamicGraph.Height);
        _nodeQueue = new PathNodeQueue(
            _pathNodePool.NonNullCount,
            _pathNodePool.Width,
            _pathNodePool.Height
        );
    }

    private void ResetSprites() {
        for (int y = 0; y < _pathNodePool.Height; y++) {
            for (int x = 0; x < _pathNodePool.Width; x++) {
                if (_pathNodePool[x, y] is not null) {
                    var pos = RoomHelper.MiddleOfTile(x, y);
                    var nodeSprite = new DebugSprite(
                        pos,
                        new FSprite("pixel") {
                            scale = 5f,
                            isVisible = false,
                        },
                        _room
                    );
                    _nodeSprites[x, y] = nodeSprite;
                    _room.AddObject(nodeSprite);
                    var connectionSprite = new DebugSprite(
                        pos,
                        TriangleMesh.MakeLongMesh(1, false, true),
                        _room
                    );
                    connectionSprite.sprite.isVisible = false;
                    _connectionSprites[x, y] = connectionSprite;
                    _room.AddObject(connectionSprite);
                }
            }
        }
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
            _openNodes = new BitGrid(_dynamicGraph.Width, _dynamicGraph.Height);
            _closedNodes = new BitGrid(_dynamicGraph.Width, _dynamicGraph.Height);
            _nodeSprites = new DebugSprite[_pathNodePool.Width, _pathNodePool.Height];
            _connectionSprites = new DebugSprite[_pathNodePool.Width, _pathNodePool.Height];
            ResetSprites();
            IsInit = false;
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

        var oldStartSprite = _nodeSprites[Start.x, Start.y];
        if (oldStartSprite is not null) {
            oldStartSprite.sprite.color = Color.white;
            if (_closedNodes[Start]) {
                oldStartSprite.sprite.scale = 5f;
            } else if (!_openNodes[Start]) {
                oldStartSprite.sprite.isVisible = false;
            }
        }

        if (Destination != destination) {
            _openNodes.Reset();
            _closedNodes.Reset();
            var destNode = _pathNodePool[destination]!;
            destNode.Reset(start, null, 0);
            _nodeQueue.Reset();
            _nodeQueue.Add(destNode);
            _openNodes[destination] = true;
            var oldDestSprite = _nodeSprites[Destination.x, Destination.y];
            if (oldDestSprite is not null) {
                oldDestSprite.sprite.color = Color.white;
            }
            Destination = destination;
            RemovePathHighlight();
            HideSprites();
            Start = start;
            IsFinished = false;
        } else {
            DisplaySprites();
            if (_closedNodes[start]) {
                RemovePathHighlight();
                Start = start;
                HighlightPath();
                IsFinished = true;
            } else {
                _nodeQueue.ResetHeuristics(start);
                RemovePathHighlight();
                Start = start;
                IsFinished = false;
            }
        }

        if (_descriptor != descriptor) {
            _dynamicGraph.Reset(descriptor);
            _descriptor = descriptor;
        }

        var startSprite = _nodeSprites[Start.x, Start.y]!.sprite;
        startSprite.isVisible = true;
        startSprite.color = Color.red;
        startSprite.scale = 10f;

        var destSprite = _nodeSprites[Destination.x, Destination.y]!.sprite;
        destSprite.isVisible = true;
        destSprite.color = Color.blue;
        destSprite.scale = 10f;

        DisplayingSprites = true;

        IsInit = true;
        return;
    }

    public void Poll() {
        if (!IsInit || IsFinished || !DisplayingSprites) {
            return;
        }
        if (_nodeQueue.Count > 0) {
            if (!_nodeQueue.Validate()) {
                Plugin.Logger!.LogError($"min heap does not satisfy heap property\n{_nodeQueue.DebugList()}");
                IsFinished = true;
                return;
            }
            PathNode currentNode = _nodeQueue.Root!;
            var currentPos = currentNode.GridPos;
            if (currentPos == Start) {
                HighlightPath();
                IsFinished = true;
                return;
            }
            _nodeQueue.RemoveRoot();
            _openNodes[currentPos] = false;
            _closedNodes[currentPos] = true;
            _nodeSprites[currentPos.x, currentPos.y]!.sprite.scale = 5f;

            var graphNode = _room.GetCWT().SharedGraph!.Nodes[currentPos.x, currentPos.y]!;
            var adjacencyList = _dynamicGraph.AdjacencyLists[currentPos.x, currentPos.y]!;

            void CheckConnection(NodeConnection connection) {
                IVec2 neighbourPos = connection.Next.GridPos;
                PathNode currentNeighbour = _pathNodePool[neighbourPos]!;
                if (_closedNodes[neighbourPos]) {
                    return;
                }
                if (!_openNodes[neighbourPos]) {
                    _openNodes[neighbourPos] = true;
                    currentNeighbour.Reset(
                        Start,
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

    private void HighlightPath() {
        PathNode? cursor = _pathNodePool[Start];
        if (cursor is null) {
            return;
        }
        while (cursor.Connection is not null) {
            _connectionSprites[cursor.GridPos.x, cursor.GridPos.y]!.sprite.color = Color.red;
            cursor = cursor.Connection.Value.Next;
        }
    }

    private void RemovePathHighlight() {
        PathNode? cursor = _pathNodePool[Start];
        if (cursor is null) {
            return;
        }
        while (cursor.Connection is not null) {

            _connectionSprites[cursor.GridPos.x, cursor.GridPos.y]!.sprite.color = cursor.Connection.Value.Type switch {
                ConnectionType.Drop
                or ConnectionType.Jump
                or ConnectionType.Pounce
                or ConnectionType.Shortcut
                or ConnectionType.WalkOffEdge => Color.grey,
                _ => Color.white,
            };
            cursor = cursor.Connection.Value.Next;
        }
    }

    public void DisplaySprites() {
        if (DisplayingSprites || !IsInit) {
            return;
        }
        for (int y = 0; y < _closedNodes.Height; y++) {
            for (int x = 0; x < _closedNodes.Width; x++) {
                if (_closedNodes[x, y] || _openNodes[x, y]) {
                    _nodeSprites[x, y]!.sprite.isVisible = true;
                    _connectionSprites[x, y]!.sprite.isVisible = true;
                }
            }
        }
        DisplayingSprites = true;
    }

    public void HideSprites() {
        if (!DisplayingSprites) {
            return;
        }
        for (int y = 0; y < _closedNodes.Height; y++) {
            for (int x = 0; x < _closedNodes.Width; x++) {
                var nodeSprite = _nodeSprites[x, y];
                var connectionSprite = _connectionSprites[x, y];
                if (nodeSprite is not null) {
                    nodeSprite.sprite.isVisible = false;
                }
                if (connectionSprite is not null) {
                    connectionSprite.sprite.isVisible = false;
                }
            }
        }
        DisplayingSprites = false;
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
            if (InputHelper.JustPressed(KeyCode.V)) {
                if (debugPathfinder.DisplayingSprites) {
                    debugPathfinder.HideSprites();
                } else {
                    debugPathfinder.DisplaySprites();
                }
            }
            if (InputHelper.JustPressedMouseButton(0)) {
                var mousePos = (Vector2)Input.mousePosition + self.room.game.cameras[0].pos;
                debugPathfinder.InitPathfinding(
                    RoomHelper.TilePosition(self.bodyChunks[1].pos),
                    RoomHelper.TilePosition(mousePos),
                    new SlugcatDescriptor(self)
                );
            }
            if (debugPathfinder.IsInit
                && !debugPathfinder.IsFinished
                && debugPathfinder.DisplayingSprites
            ) {
                debugPathfinder.Poll();
            }
        }
        orig(self, eu);
    }
}