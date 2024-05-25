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
                ConnectionType.GrabPole => Color.grey,
                _ => throw new InvalidUnionVariantException("unsupported NodeType variant"),
            };
            var connection = path.Connections[i];
            var sharedGraph = _room.GetCWT().SharedGraph!;
            if (connection is ConnectionType.Jump) {
                // this node can be null only if the path is constructed incorrectly so this should throw
                Node graphNode = sharedGraph.GetNode(startTile)!;
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