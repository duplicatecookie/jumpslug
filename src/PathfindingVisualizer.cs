using System.Collections.Generic;

using UnityEngine;

using IVec2 = RWCustom.IntVector2;

namespace JumpSlug.Pathfinding;

class SharedGraphVisualizer {
    public Room room;
    public bool visualizingNodes { get; private set; }
    public bool visualizingConnections { get; private set; }
    private readonly List<DebugSprite> nodeSprites;
    private readonly List<DebugSprite> connectionSprites;
    public SharedGraphVisualizer(Room room) {
        this.room = room;
        nodeSprites = new();
        connectionSprites = new();
    }

    public void NewRoom(Room room) {
        if (this.room != room) {
            this.room = room;
            ResetNodeSprites();
            ResetConnectionSprites();
        }
    }

    public void ToggleNodes() {
        var graph = room.GetCWT().sharedGraph!;
        if (visualizingNodes) {
            ResetNodeSprites();
            return;
        }
        visualizingNodes = true;
        foreach (var node in graph.nodes) {
            if (node is null) {
                continue;
            }

            var color = node.type switch {
                NodeType.Air => Color.red,
                NodeType.Floor => Color.white,
                NodeType.Slope => Color.green,
                NodeType.Corridor => Color.blue,
                NodeType.ShortcutEntrance => Color.cyan,
                NodeType.Wall => Color.grey,
                _ => throw new InvalidUnionVariantException("unsupported NodeType variant"),
            };

            var pos = RoomHelper.MiddleOfTile(node.gridPos);
            var fs = new FSprite("pixel") {
                color = color,
                scale = 5f,
            };
            var sprite = new DebugSprite(pos, fs, room);
            room.AddObject(sprite);
            nodeSprites.Add(sprite);
        }
    }

    public void ToggleConnections() {
        var graph = room.GetCWT().sharedGraph!;
        if (visualizingConnections) {
            foreach (var sprite in connectionSprites) {
                sprite.Destroy();
            }
            connectionSprites.Clear();
            visualizingConnections = false;
            return;
        }
        visualizingConnections = true;
        foreach (var node in graph.nodes) {
            if (node is null) {
                continue;
            }
            var start = RoomHelper.MiddleOfTile(node.gridPos);
            foreach (var connection in node.connections) {
                var end = RoomHelper.MiddleOfTile(connection.next.gridPos);
                var mesh = LineHelper.MakeLine(start, end, Color.white);
                var line = new DebugSprite(start, mesh, room);
                room.AddObject(line);
                connectionSprites.Add(line);
            }
        }
    }

    private void ResetConnectionSprites() {
        foreach (var sprite in connectionSprites) {
            sprite.Destroy();
        }
        connectionSprites.Clear();
        visualizingConnections = false;
    }

    private void ResetNodeSprites() {
        foreach (var sprite in nodeSprites) {
            sprite.Destroy();
        }
        nodeSprites.Clear();
        visualizingNodes = false;
    }
}

class PathVisualizer {
    private Room room;
    public bool visualizingPath { get; private set; }
    private readonly List<DebugSprite> pathSprites;

    public PathVisualizer(Room room) {
        this.room = room;
        pathSprites = new();
    }

    public void NewRoom(Room room) {
        if (room != this.room) {
            this.room = room;
            ResetSprites();
        }
    }

    public void TogglePath(Path? path, SlugcatDescriptor slugcat) {
        if (visualizingPath
            || path is null
            || path.NodeCount <= 2
        ) {
            ResetSprites();
            return;
        }
        visualizingPath = true;
        for (int i = 0; i < path!.ConnectionCount; i++) {
            IVec2 startTile = path.nodes[i + 1];
            IVec2 endTile = path.nodes[i];
            var start = RoomHelper.MiddleOfTile(startTile);
            var end = RoomHelper.MiddleOfTile(endTile);
            var color = path.connections[i] switch {
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
            var connection = path.connections[i];
            var sharedGraph = room.GetCWT().sharedGraph!;
            if (connection is ConnectionType.Jump) {
                // this node can be null only if the path is constructed incorrectly so this should throw
                Node graphNode = sharedGraph.GetNode(startTile)!;
                if (graphNode.verticalBeam && !graphNode.horizontalBeam) {
                    var v0 = slugcat.VerticalPoleJumpVector();
                    VisualizeJump(v0, startTile, endTile);
                } else if (graphNode.horizontalBeam || graphNode.type is NodeType.Floor) {
                    var headPos = new IVec2(startTile.x, startTile.y + 1);
                    var v0 = slugcat.HorizontalPoleJumpVector();
                    VisualizeJump(v0, headPos, endTile);
                    var preLine = LineHelper.MakeLine(start, RoomHelper.MiddleOfTile(headPos), Color.white);
                    var preSprite = new DebugSprite(start, preLine, room);
                    pathSprites.Add(preSprite);
                    room.AddObject(preSprite);
                } else if (graphNode.type is NodeType.Wall wall) {
                    Vector2 v0 = slugcat.WallJumpVector(wall.Direction);
                    VisualizeJump(v0, startTile, endTile);
                }
            } else if (connection is ConnectionType.WalkOffEdge) {
                var startPos = new IVec2(
                    startTile.x,
                    sharedGraph.GetNode(startTile)?.type is NodeType.Corridor ? startTile.y : startTile.y + 1
                );
                var v0 = slugcat.HorizontalCorridorFallVector();
                VisualizeJump(v0, startPos, endTile);
                var preLine = LineHelper.MakeLine(start, RoomHelper.MiddleOfTile(startPos), Color.white);
                var preSprite = new DebugSprite(start, preLine, room);
                pathSprites.Add(preSprite);
                room.AddObject(preSprite);
            }
            var mesh = LineHelper.MakeLine(start, end, color);
            var sprite = new DebugSprite(start, mesh, room);
            room.AddObject(sprite);
            pathSprites.Add(sprite);
        }
    }

    private void ResetSprites() {
        foreach (var sprite in pathSprites) {
            sprite.Destroy();
        }
        pathSprites.Clear();
        visualizingPath = false;
    }

    private void VisualizeJump(Vector2 v0, IVec2 startTile, IVec2 endTile) {
        Vector2 pathOffset = RoomHelper.MiddleOfTile(startTile);
        Vector2 lastPos = pathOffset;
        float maxT = 20 * (endTile.x - startTile.x) / v0.x;
        for (float t = 0; t < maxT; t += 2f) {
            var nextPos = new Vector2(pathOffset.x + v0.x * t, DynamicGraph.Parabola(pathOffset.y, v0, room.gravity, t));
            var sprite = new DebugSprite(lastPos, LineHelper.MakeLine(lastPos, nextPos, Color.white), room);
            pathSprites.Add(sprite);
            room.AddObject(sprite);
            lastPos = nextPos;
        }
        var postLine = LineHelper.MakeLine(lastPos, room.MiddleOfTile(endTile), Color.white);
        var postSprite = new DebugSprite(lastPos, postLine, room);
        pathSprites.Add(postSprite);
        room.AddObject(postSprite);
    }
}