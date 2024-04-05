using System;
using System.Collections.Generic;

using UnityEngine;

using static JumpSlug.Pathfinder;

using IVec2 = RWCustom.IntVector2;

namespace JumpSlug;

class PathfindingVisualizer {
    public Pathfinder pathfinder;
    public Player Player => pathfinder.player;
    public bool visualizingNodes { get; private set; }
    public bool visualizingConnections { get; private set; }
    public bool visualizingPath { get; private set; }
    private readonly List<DebugSprite> nodeSprites;
    private readonly List<DebugSprite> connectionSprites;
    private readonly List<DebugSprite> pathSprites;

    public PathfindingVisualizer(Pathfinder pathfinder) {
        this.pathfinder = pathfinder;
        nodeSprites = new();
        connectionSprites = new();
        pathSprites = new();
    }

    public void ToggleNodes() {
        if (visualizingNodes || pathfinder is null || pathfinder.graph is null) {
            foreach (var sprite in nodeSprites) {
                sprite.Destroy();
            }
            nodeSprites.Clear();
            visualizingNodes = false;
            return;
        }
        visualizingNodes = true;
        foreach (var node in pathfinder.graph) {
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
                _ => throw new ArgumentOutOfRangeException("unsupported NodeType variant"),
            };

            var pos = Player.room.MiddleOfTile(node.gridPos);
            var fs = new FSprite("pixel") {
                color = color,
                scale = 5f,
            };
            var sprite = new DebugSprite(pos, fs, Player.room);
            Player.room.AddObject(sprite);
            nodeSprites.Add(sprite);
        }
    }

    public void ToggleConnections() {
        if (visualizingConnections || pathfinder is null || pathfinder.graph is null) {
            foreach (var sprite in connectionSprites) {
                sprite.Destroy();
            }
            connectionSprites.Clear();
            visualizingConnections = false;
            return;
        }
        visualizingConnections = true;
        foreach (var node in pathfinder.graph) {
            if (node is null) {
                continue;
            }
            var start = Player.room.MiddleOfTile(node.gridPos);
            foreach (var connection in node.connections) {
                var end = Player.room.MiddleOfTile(connection.next.gridPos);
                var mesh = LineHelper.MakeLine(start, end, Color.white);
                var line = new DebugSprite(start, mesh, Player.room);
                Player.room.AddObject(line);
                connectionSprites.Add(line);
            }
        }
    }

    public void TogglePath(PathNode? path) {
        if (visualizingPath || pathfinder?.graph is null || path is null) {
            foreach (var sprite in pathSprites) {
                sprite.Destroy();
            }
            pathSprites.Clear();
            visualizingPath = false;
            return;
        }
        visualizingPath = true;
        PathNode node = path;
        while (node.connection is not null) {
            var connection = node.connection.Value;
            var startTile = node.gridPos;
            var endTile = connection.next.gridPos;
            var start = Player.room.MiddleOfTile(startTile);
            var end = Player.room.MiddleOfTile(endTile);
            var color = connection.type switch {
                ConnectionType.Jump or ConnectionType.WalkOffEdge => Color.blue,
                ConnectionType.Pounce => Color.green,
                ConnectionType.Drop => Color.red,
                ConnectionType.Shortcut => Color.cyan,
                ConnectionType.Walk or ConnectionType.Climb or ConnectionType.Crawl => Color.white,
                _ => throw new ArgumentOutOfRangeException(),
            };
            int direction = startTile.x < endTile.x ? 1 : -1;

            if (connection.type is ConnectionType.Jump) {
                // this node can be null only if the path is constructed incorrectly so this should throw
                Node graphNode = pathfinder.graph[startTile.x, startTile.y]!;
                if (graphNode.verticalBeam && !graphNode.horizontalBeam) {
                    Vector2 v0;
                    if (Player.isRivulet) {
                        v0 = new Vector2(9f * direction, 9f) * Mathf.Lerp(1, 1.15f, Player.Adrenaline);
                    } else if (Player.isSlugpup) {
                        v0 = new Vector2(5f * direction, 7f) * Mathf.Lerp(1, 1.15f, Player.Adrenaline);
                    } else {
                        v0 = new Vector2(6f * direction, 8f) * Mathf.Lerp(1, 1.15f, Player.Adrenaline);
                    }
                    VisualizeJump(v0, startTile, endTile);
                } else if (graphNode.horizontalBeam || graphNode.type is NodeType.Floor) {
                    var headPos = new IVec2(startTile.x, startTile.y + 1);
                    var v0 = new Vector2(
                        4.2f * direction * Player.slugcatStats.runspeedFac * Mathf.Lerp(1, 1.5f, Player.Adrenaline),
                        (Player.isRivulet ? 6f : 4f) * Mathf.Lerp(1, 1.15f, Player.Adrenaline) + JumpBoost(Player.isSlugpup ? 7 : 8));
                    VisualizeJump(v0, headPos, endTile);
                    var preLine = LineHelper.MakeLine(start, Player.room.MiddleOfTile(headPos), Color.white);
                    var preSprite = new DebugSprite(start, preLine, Player.room);
                    pathSprites.Add(preSprite);
                    Player.room.AddObject(preSprite);
                } else if (graphNode.type is NodeType.Wall wall) {
                    Vector2 v0;
                    if (Player.isRivulet) {
                        v0 = new Vector2(-wall.Direction * 9, 10) * Mathf.Lerp(1, 1.15f, Player.Adrenaline);
                    } else if (Player.isSlugpup) {
                        v0 = new Vector2(-wall.Direction * 5, 6) * Mathf.Lerp(1, 1.15f, Player.Adrenaline);
                    } else {
                        v0 = new Vector2(-wall.Direction * 6, 8) * Mathf.Lerp(1, 1.15f, Player.Adrenaline);
                    }
                    VisualizeJump(v0, startTile, endTile);
                }
            } else if (connection.type is ConnectionType.WalkOffEdge) {
                var headPos = new IVec2(startTile.x, startTile.y + 1);
                var v0 = new Vector2(
                    4.2f * direction * Player.slugcatStats.runspeedFac * Mathf.Lerp(1, 1.5f, Player.Adrenaline),
                    0);
                VisualizeJump(v0, headPos, endTile);
                var preLine = LineHelper.MakeLine(start, Player.room.MiddleOfTile(headPos), Color.white);
                var preSprite = new DebugSprite(start, preLine, Player.room);
                pathSprites.Add(preSprite);
                Player.room.AddObject(preSprite);
            }
            var mesh = LineHelper.MakeLine(start, end, color);
            var sprite = new DebugSprite(start, mesh, Player.room);
            Player.room.AddObject(sprite);
            pathSprites.Add(sprite);
            node = connection.next;
        }
    }

    private void VisualizeJump(Vector2 v0, IVec2 startTile, IVec2 endTile) {
        Vector2 pathOffset = Player.room.MiddleOfTile(startTile);
        Vector2 lastPos = pathOffset;
        float maxT = 20 * (endTile.x - startTile.x) / v0.x;
        for (float t = 0; t < maxT; t += 2f) {
            var nextPos = new Vector2(pathOffset.x + v0.x * t, pathfinder.Parabola(pathOffset.y, v0, t));
            var sprite = new DebugSprite(lastPos, LineHelper.MakeLine(lastPos, nextPos, Color.white), Player.room);
            pathSprites.Add(sprite);
            Player.room.AddObject(sprite);
            lastPos = nextPos;
        }
        var postLine = LineHelper.MakeLine(lastPos, Player.room.MiddleOfTile(endTile), Color.white);
        var postSprite = new DebugSprite(lastPos, postLine, Player.room);
        pathSprites.Add(postSprite);
        Player.room.AddObject(postSprite);
    }
}