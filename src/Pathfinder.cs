using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

using MoreSlugcats;

using UnityEngine;

using IVec2 = RWCustom.IntVector2;

namespace JumpSlug;

class Pathfinder {
    public class Path {
        // these aren't private because it's more straightforward for the visualizer to use manual indexing
        // and the accessors can't be made const correct
        public int cursor;
        public readonly List<IVec2> nodes;
        public readonly List<ConnectionType> connections;

        public Path(PathNode startNode) {
            nodes = new();
            connections = new();
            var currentNode = startNode;
            while (currentNode is not null) {
                nodes.Add(currentNode.gridPos);
                if (currentNode.connection is not null) {
                    connections.Add(currentNode.connection.Value.type);
                }
                currentNode = currentNode.connection?.next;
            }
            cursor = nodes.Count - 1;
        }

        public int NodeCount => nodes.Count;
        public int ConnectionCount => connections.Count;

        public IVec2? CurrentNode() {
            if (cursor < 0) {
                return null;
            }
            return nodes[cursor];
        }
        public IVec2? PeekNode(int offset) {
            if (cursor - offset < 0) {
                return null;
            }
            return nodes[cursor - offset];
        }
        public ConnectionType? CurrentConnection() {
            if (cursor < 1) {
                return null;
            }
            return connections[cursor - 1];
        }
        public ConnectionType? PeekConnection(int offset) {
            if (cursor - offset < 1) {
                return null;
            }
            return connections[cursor - offset - 1];
        }
        public void Advance() {
            cursor -= 1;
        }
    }

    public class PathNode {
        public IVec2 gridPos;
        public PathConnection? connection;
        public float pathCost;
        public float heuristic;
        public PathNode(IVec2 gridPos, IVec2 goalPos, float cost) {
            this.gridPos = gridPos;
            pathCost = cost;
            heuristic = Mathf.Sqrt((gridPos.x - goalPos.x) * (gridPos.x - goalPos.x) + (gridPos.y - goalPos.y) * (gridPos.y - goalPos.y));
        }
    }
    public struct PathConnection {
        public ConnectionType type;
        public PathNode next;
        public PathConnection(ConnectionType type, PathNode next) {
            this.type = type;
            this.next = next;
        }
    }
    public class Node {
        public NodeType type;
        public bool verticalBeam;
        public bool horizontalBeam;
        public bool hasPlatform;
        public List<NodeConnection> connections;
        public List<NodeConnection> dynamicConnections;
        public IVec2 gridPos;

        public Node(NodeType type, int x, int y) {
            this.type = type;
            gridPos = new IVec2(x, y);
            connections = new();
            dynamicConnections = new();
        }
    }
    public record NodeType {
        public record Air() : NodeType();
        public record Floor() : NodeType();
        public record Slope() : NodeType();
        public record Corridor() : NodeType();
        public record ShortcutEntrance(int Index) : NodeType();
        public record Wall(int Direction) : NodeType();

        private NodeType() { }
    }

    public class NodeConnection {
        public ConnectionType type;
        public Node next;
        public float weight;

        public NodeConnection(ConnectionType type, Node next, float weight = 1f) {
            if (next is null) {
                throw new NoNullAllowedException();
            }
            this.next = next;
            this.weight = weight;
            this.type = type;
        }
    }

    public record ConnectionType {
        public record Walk(int Direction) : ConnectionType();
        public record Climb(IVec2 Direction) : ConnectionType();
        public record Crawl(IVec2 Direction) : ConnectionType();
        public record Jump(int Direction) : ConnectionType();
        public record WalkOffEdge(int Direction) : ConnectionType();
        public record Pounce(int Direction) : ConnectionType();
        public record Shortcut() : ConnectionType();
        public record Drop() : ConnectionType();

        private ConnectionType() { }
    }

    public Player player;
    public Node?[,]? graph;
    public Pathfinder(Player player) {
        this.player = player;
        graph = new Node[0, 0];
    }

    public Node? GetNode(int x, int y) {
        if (graph is null || x < 0 || y < 0 || x >= graph.GetLength(0) || y >= graph.GetLength(1)) {
            return null;
        }
        return graph[x, y];
    }

    public Node? GetNode(IVec2 pos) {
        return GetNode(pos.x, pos.y);
    }

    public Node? CurrentNode() {
        IVec2 pos = player.room.GetTilePosition(player.bodyChunks[0].pos);
        if (player.bodyMode == Player.BodyModeIndex.Stand
            || player.animation == Player.AnimationIndex.StandOnBeam
        ) {
            return GetNode(pos.x, pos.y - 1) is Node node ? node : GetNode(pos.x, pos.y - 2);
        }
        return GetNode(pos);
    }

    public void Update() {
        if (InputHelper.JustPressed(KeyCode.G)) {
            if (graph is null) {
                NewRoom();
            } else {
                graph = null;
            }
        }
    }

    public void NewRoom() {
        if (player.room is null) {
            return;
        }
        Room room = player.room;
        int width = room.Tiles.GetLength(0);
        int height = room.Tiles.GetLength(1);
        graph = new Node[width, height];
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                if (x - 1 < 0 || x + 1 >= width || y - 1 < 0 || y + 1 >= height) {
                    continue;
                }
                if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Solid) {
                    continue;
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Floor) {
                    if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid) {
                        graph[x, y] = new Node(new NodeType.Corridor(), x, y) {
                            hasPlatform = true,
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
                        graph[x, y] = new Node(new NodeType.Corridor(), x, y);
                    } else if (
                          room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Solid
                          || room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.ShortcutEntrance
                          // pretend invalid slope is solid
                          || room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Slope
                          && room.Tiles[x - 1, y - 1].Terrain == Room.Tile.TerrainType.Solid
                          && room.Tiles[x + 1, y - 1].Terrain == Room.Tile.TerrainType.Solid
                    ) {
                        graph[x, y] = new Node(new NodeType.Floor(), x, y);
                    } else if (room.Tiles[x, y - 1].Terrain == Room.Tile.TerrainType.Floor) {
                        graph[x, y] = new Node(new NodeType.Floor(), x, y);
                    } else if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Air
                          && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid) {
                        graph[x, y] = new Node(new NodeType.Wall(1), x, y);
                    } else if (room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid
                          && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Air) {
                        graph[x, y] = new Node(new NodeType.Wall(-1), x, y);
                    }
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Slope
                      && room.Tiles[x, y + 1].Terrain == Room.Tile.TerrainType.Air
                      && !(room.Tiles[x - 1, y].Terrain == Room.Tile.TerrainType.Solid
                          && room.Tiles[x + 1, y].Terrain == Room.Tile.TerrainType.Solid)) {
                    graph[x, y] = new Node(new NodeType.Slope(), x, y);
                } else if (room.Tiles[x, y].Terrain == Room.Tile.TerrainType.ShortcutEntrance) {
                    int index = Array.IndexOf(room.shortcutsIndex, new IVec2(x, y));
                    if (index > -1 && room.shortcuts[index].shortCutType == ShortcutData.Type.Normal) {
                        graph[x, y] = new Node(new NodeType.ShortcutEntrance(index), x, y);
                    }
                }

                if (room.Tiles[x, y].verticalBeam) {
                    if (graph[x, y] is null) {
                        graph[x, y] = new Node(new NodeType.Air(), x, y);
                    }
                    graph[x, y]!.verticalBeam = true;
                }

                if (room.Tiles[x, y].horizontalBeam) {
                    if (graph[x, y] is null) {
                        graph[x, y] = new Node(new NodeType.Air(), x, y);
                    }
                    graph[x, y]!.horizontalBeam = true;
                }
            }
        }

        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                if (graph[x, y] is null) {
                    continue;
                }
                if (graph[x, y]!.type is NodeType.Floor) {
                    if (GetNode(x + 1, y)?.type is NodeType.Floor or NodeType.Slope) {
                        ConnectNodes(
                            graph[x, y]!,
                            graph[x + 1, y]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    } else if (GetNode(x + 1, y)?.type is NodeType.Corridor) {
                        ConnectNodes(
                            graph[x, y]!,
                            graph[x + 1, y]!,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    }
                    if (GetNode(x + 1, y - 1)?.type is NodeType.Slope) {
                        ConnectNodes(
                            graph[x, y]!,
                            graph[x + 1, y - 1]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    }
                    if (GetNode(x + 1, y + 1)?.type is NodeType.Corridor) {
                        graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Walk(1), graph[x + 1, y + 1]!));
                    }
                }

                if (graph[x, y]!.type is NodeType.Slope) {
                    if (GetNode(x + 1, y)?.type is NodeType.Floor or NodeType.Slope) {
                        ConnectNodes(
                            graph[x, y]!,
                            graph[x + 1, y]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    } else if (GetNode(x + 1, y - 1)?.type is NodeType.Slope) {
                        ConnectNodes(
                            graph[x, y]!,
                            graph[x + 1, y - 1]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    } else if (GetNode(x + 1, y + 1)?.type is NodeType.Slope or NodeType.Floor) {
                        ConnectNodes(
                            graph[x, y]!,
                            graph[x + 1, y + 1]!,
                            new ConnectionType.Walk(1),
                            new ConnectionType.Walk(-1)
                        );
                    }
                }

                var rightNode = GetNode(x + 1, y);
                var aboveNode = GetNode(x, y + 1);
                if (graph[x, y]!.type is NodeType.Corridor) {
                    if (rightNode?.type is NodeType.Corridor or NodeType.Floor or NodeType.ShortcutEntrance) {
                        ConnectNodes(
                            graph[x, y]!,
                            rightNode,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    } else if (rightNode?.horizontalBeam == true) {
                        ConnectNodes(
                            graph[x, y]!,
                            rightNode,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            rightNode.horizontalBeam
                                ? new ConnectionType.Climb(new IVec2(-1, 0))
                                : new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    }
                    if (GetNode(x + 1, y - 1)?.type is NodeType.Floor) {
                        graph[x + 1, y - 1]!.connections.Add(new NodeConnection(new ConnectionType.Walk(-1), graph[x, y]!));
                    }
                    if (aboveNode?.type is NodeType.Corridor or NodeType.Floor or NodeType.ShortcutEntrance) {
                        ConnectNodes(
                            graph[x, y]!,
                            aboveNode,
                            new ConnectionType.Crawl(new IVec2(0, 1)),
                            new ConnectionType.Crawl(new IVec2(0, -1))
                        );
                    }
                } else {
                    if (graph[x, y]!.horizontalBeam) {
                        if (rightNode?.horizontalBeam == true) {
                            ConnectNodes(
                                graph[x, y]!,
                                graph[x + 1, y]!,
                                new ConnectionType.Climb(new IVec2(1, 0)),
                                rightNode.type is NodeType.Corridor
                                    ? new ConnectionType.Crawl(new IVec2(-1, 0))
                                    : new ConnectionType.Climb(new IVec2(-1, 0))
                            );
                        }
                    }
                    if (graph[x, y]!.verticalBeam) {
                        if (aboveNode?.verticalBeam == true) {
                            ConnectNodes(
                                graph[x, y]!,
                                aboveNode,
                                new ConnectionType.Climb(new IVec2(0, 1)),
                                aboveNode.type is NodeType.Corridor
                                    ? new ConnectionType.Crawl(new IVec2(0, -1))
                                    : new ConnectionType.Climb(new IVec2(0, -1))
                            );
                        }
                    }
                }
                if (graph[x, y]!.type is NodeType.ShortcutEntrance) {
                    var entrance = graph[x, y]!.type as NodeType.ShortcutEntrance;
                    var shortcutData = room.shortcuts[entrance!.Index];
                    var destNode = graph[shortcutData.destinationCoord.x, shortcutData.destinationCoord.y];
                    if (destNode is null || destNode.type is not NodeType.ShortcutEntrance) {
                        Plugin.Logger!.LogError($"Shortcut entrance has no valid exit, pos: ({x}, {y}), index: {entrance.Index}");
                        return;
                    }
                    graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Shortcut(), destNode, shortcutData.length));
                    if (GetNode(x + 1, y)?.type is NodeType.Corridor) {
                        ConnectNodes(
                            graph[x, y]!,
                            graph[x + 1, y]!,
                            new ConnectionType.Crawl(new IVec2(1, 0)),
                            new ConnectionType.Crawl(new IVec2(-1, 0))
                        );
                    }
                    if (GetNode(x, y + 1)?.type is NodeType.Corridor) {
                        ConnectNodes(
                            graph[x, y]!,
                            graph[x, y + 1]!,
                            new ConnectionType.Crawl(new IVec2(0, 1)),
                            new ConnectionType.Crawl(new IVec2(0, -1))
                        );
                    }
                }
            }
        }
    }

    private void ConnectNodes(Node start, Node end, ConnectionType startToEndType, ConnectionType endToStartType, float weight = 1f) {
        start.connections.Add(new NodeConnection(startToEndType, end, weight));
        end.connections.Add(new NodeConnection(endToStartType, start, weight));
    }

    private void TraceDrop(int x, int y) {
        if (graph is null || graph[x, y]?.type is NodeType.Floor or NodeType.Slope) {
            return;
        }
        for (int i = y - 1; i >= 0; i--) {
            if (graph[x, i] is null) {
                continue;
            }
            if (graph[x, i]!.type is NodeType.Floor or NodeType.Slope) {
                // t = sqrt(2 * d / g)
                // weight might have inaccurate units
                graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Drop(), graph[x, i]!, Mathf.Sqrt(2 * 20 * (y - i) / player.room.gravity) * 4.2f / 20));
                break;
            } else if (graph[x, i]!.horizontalBeam) {
                graph[x, y]!.connections.Add(new NodeConnection(new ConnectionType.Drop(), graph[x, i]!, Mathf.Sqrt(2 * 20 * (y - i) / player.room.gravity)));
            }
        }
    }

    public float Parabola(float yOffset, Vector2 v0, float t) => v0.y * t - 0.5f * player.gravity * t * t + yOffset;

    // start refers to the head position
    private void TraceJump(Node startNode, IVec2 start, Vector2 v0, ConnectionType type, bool upright = true) {
        int x = start.x;
        int y = start.y;
        int width = player.room.Tiles.GetLength(0);
        int height = player.room.Tiles.GetLength(1);
        if (x < 0 || y < 0 || x >= width || y >= height || graph is null) {
            return;
        }
        int direction = v0.x switch {
            > 0 => 1,
            < 0 => -1,
            0 or float.NaN => throw new ArgumentOutOfRangeException(),
        };
        int xOffset = (direction + 1) / 2;
        var pathOffset = player.room.MiddleOfTile(start);

        while (true) {
            float t = (20 * (x + xOffset) - pathOffset.x) / v0.x;
            float result = Parabola(pathOffset.y, v0, t) / 20;
            if (result > y + 1) {
                y++;
            } else if (result < y) {
                if (y - 2 < 0) {
                    break;
                }
                if (graph[x, upright ? y - 1 : y]?.type is NodeType.Floor or NodeType.Slope) {
                    startNode.dynamicConnections.Add(new NodeConnection(type, graph[x, upright ? y - 1 : y]!, t * 20 / 4.2f + 1));
                }
                if (player.room.Tiles[x, upright ? y - 2 : y - 1].Terrain == Room.Tile.TerrainType.Solid) {
                    break;
                }
                y--;
            } else {
                x += direction;
            }

            if (x < 0 || y < 0 || x >= width || y >= height
                || player.room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Solid
                || player.room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Slope) {
                break;
            }

            if (graph[x, y] is null) {
                continue;
            }
            if (graph[x, y]!.type is NodeType.Corridor) {
                break;
            }
            if (graph[x, y]!.type is NodeType.Wall wall && wall.Direction == direction) {
                startNode.dynamicConnections.Add(new NodeConnection(type, graph[x, y]!, t * 4.2f / 20));
                break;
            } else if (graph[x, y]!.verticalBeam) {
                float poleResult = Parabola(pathOffset.y, v0, (20 * x + 10 - pathOffset.x) / v0.x) / 20;
                if (poleResult > y && poleResult < y + 1) {
                    startNode.dynamicConnections.Add(new NodeConnection(type, graph[x, y]!, t * 4.2f / 20 + 5));
                }
            } else if (graph[x, y]!.horizontalBeam) {
                startNode.dynamicConnections.Add(new NodeConnection(type, graph[x, y]!, t * 4.2f / 20 + 10));
            }
        }
    }

    public static float JumpBoost(float boost) {
        float t = Mathf.Ceil(boost / 1.5f);
        return 0.3f * ((boost - 0.5f) * t - 0.75f * t * t);
    }

    public Path? FindPath(IVec2 start, IVec2 destination) {
        // TODO: optimize this entire function, it's probably really inefficient
        if (graph is null) {
            return null;
        }
        if (GetNode(start) is null) {
            Plugin.Logger!.LogDebug($"no node at start ({start.x}, {start.y})");
            return null;
        }
        if (GetNode(destination) is null) {
            Plugin.Logger!.LogDebug($"no node at destination ({destination.x}, {destination.y})");
            return null;
        }
        if (Timers.active) {
            Timers.findPath.Start();
        }
        var openNodes = new List<PathNode>()
        {
            new(start, destination, 0),
        };
        var closedNodes = new List<PathNode>();
        while (openNodes.Count > 0) {
            float currentF = float.MaxValue;
            PathNode? currentNode = null;
            int currentIndex = 0;
            for (int i = 0; i < openNodes.Count; i++) {
                if (openNodes[i].pathCost + openNodes[i].heuristic < currentF) {
                    currentNode = openNodes[i];
                    currentIndex = i;
                    currentF = openNodes[i].pathCost + openNodes[i].heuristic;
                }
            }

            // might be redundant
            if (currentNode is null) {
                Plugin.Logger!.LogError($"current node was null");
                if (Timers.active) {
                    Timers.findPath.Stop();
                }
                return null;
            }

            var currentPos = currentNode.gridPos;

            if (currentPos == destination) {
                if (Timers.active) {
                    Timers.findPath.Stop();
                }
                return new Path(currentNode);
            }

            openNodes.RemoveAt(currentIndex);
            closedNodes.Add(currentNode);

            var graphNode = graph[currentPos.x, currentPos.y];
            graphNode!.dynamicConnections.Clear();

            bool goLeft = true;
            bool goRight = true;
            if (graphNode.type is NodeType.Wall(int direction)) {
                if (direction == -1) {
                    goLeft = false;
                } else {
                    goRight = false;
                }
            }
            if (GetNode(currentPos.x, currentPos.y - 1)?.type is NodeType.Wall footWall) {
                if (footWall.Direction == -1) {
                    goLeft = false;
                } else {
                    goRight = false;
                }
            }

            if (graphNode.verticalBeam && !graphNode.horizontalBeam
                && graphNode.type is not (NodeType.Corridor or NodeType.Floor or NodeType.Slope)
                && GetNode(currentPos.x, currentPos.y - 1)?.type is not (NodeType.Corridor or NodeType.Floor or NodeType.Slope)) {
                Vector2 v0;
                if (player.isRivulet) {
                    v0 = new Vector2(9f, 9f) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                } else if (player.isSlugpup) {
                    v0 = new Vector2(5f, 7f) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                } else {
                    v0 = new Vector2(6f, 8f) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                }
                if (goRight) {
                    TraceJump(graphNode, currentPos, v0, new ConnectionType.Jump(1));
                }
                if (goLeft) {
                    v0.x = -v0.x;
                    TraceJump(graphNode, currentPos, v0, new ConnectionType.Jump(-1));
                }
                if (!graphNode.dynamicConnections.Any(c => c.type == new ConnectionType.Drop())) {
                    TraceDrop(currentPos.x, currentPos.y);
                }
            }
            if (graphNode.horizontalBeam && graphNode.type is not (NodeType.Corridor or NodeType.Floor or NodeType.Slope)) {
                var headPos = new IVec2(currentPos.x, currentPos.y + 1);
                var v0 = new Vector2(
                    4.2f * player.slugcatStats.runspeedFac * Mathf.Lerp(1, 1.5f, player.Adrenaline),
                    (player.isRivulet ? 6f : 4f) * Mathf.Lerp(1, 1.15f, player.Adrenaline) + JumpBoost(player.isSlugpup ? 7 : 8));
                if (goRight) {
                    TraceJump(graphNode, headPos, v0, new ConnectionType.Jump(1));
                }
                if (goLeft) {
                    v0.x = -v0.x;
                    TraceJump(graphNode, headPos, v0, new ConnectionType.Jump(-1));
                }

                if (!graphNode.dynamicConnections.Any(c => c.type == new ConnectionType.Drop())) {
                    TraceDrop(currentPos.x, currentPos.y);
                }
            }
            if (graphNode.type is NodeType.Floor) {
                var headPos = new IVec2(currentPos.x, currentPos.y + 1);
                var v0 = new Vector2(
                    4.2f * player.slugcatStats.runspeedFac * Mathf.Lerp(1, 1.5f, player.Adrenaline),
                    (player.isRivulet ? 6f : 4f) * Mathf.Lerp(1, 1.15f, player.Adrenaline) + JumpBoost(player.isSlugpup ? 7 : 8));
                if (goRight) {
                    TraceJump(graphNode, headPos, v0, new ConnectionType.Jump(1));
                    if (headPos.x + 1 < graph.GetLength(0) && graph[currentPos.x + 1, currentPos.y - 1]?.type is NodeType.Wall) {
                        v0.y = 0f;
                        TraceJump(graphNode, headPos, v0, new ConnectionType.WalkOffEdge(1));
                    }
                }
                if (goLeft) {
                    v0.x = -v0.x;
                    TraceJump(graphNode, headPos, v0, new ConnectionType.Jump(-1));
                    if (currentPos.x - 1 > 0 && graph[currentPos.x - 1, currentPos.y - 1]?.type is NodeType.Wall) {
                        v0.y = 0f;
                        TraceJump(graphNode, headPos, v0, new ConnectionType.WalkOffEdge(-1));
                    }
                }

            } else if (graphNode.type is NodeType.Corridor) {
                var v0 = new Vector2(
                    4.2f * player.slugcatStats.runspeedFac * Mathf.Lerp(1, 1.5f, player.Adrenaline),
                    0);
                // v0.x might be too large
                if (GetNode(currentPos.x + 1, currentPos.y) is null) {
                    TraceJump(graphNode, currentPos, v0, new ConnectionType.WalkOffEdge(1), upright: false);
                }
                if (GetNode(currentPos.x - 1, currentPos.y) is null) {
                    v0.x = -v0.x;
                    TraceJump(graphNode, currentPos, v0, new ConnectionType.WalkOffEdge(-1), upright: false);
                }
                if (GetNode(currentPos.x, currentPos.y - 1) is null
                    && player.room.Tiles[currentPos.x, currentPos.y - 1].Terrain == Room.Tile.TerrainType.Air) {
                    TraceDrop(currentPos.x, currentPos.y);
                }
            } else if (graphNode.type is NodeType.Wall jumpWall) {
                Vector2 v0;
                if (player.isRivulet) {
                    v0 = new Vector2(-jumpWall.Direction * 9, 10) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                } else if (player.isSlugpup) {
                    v0 = new Vector2(-jumpWall.Direction * 5, 6) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                } else {
                    v0 = new Vector2(-jumpWall.Direction * 6, 8) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
                }
                TraceJump(graphNode, currentPos, v0, new ConnectionType.Jump(-jumpWall.Direction));
            }

            void CheckConnection(NodeConnection connection) {
                PathNode? currentNeighbour = null;
                foreach (var node in openNodes) {
                    if (connection.next.gridPos == node.gridPos) {
                        currentNeighbour = node;
                    }
                }

                if (currentNeighbour is null) {
                    foreach (var node in closedNodes) {
                        if (connection.next.gridPos == node.gridPos) {
                            return;
                        }
                    }
                    currentNeighbour = new PathNode(connection.next.gridPos, destination, currentNode.pathCost + connection.weight) {
                        connection = new PathConnection(connection.type, currentNode),
                    };
                    openNodes.Add(currentNeighbour);
                }
                if (currentNode.pathCost + connection.weight < currentNeighbour.pathCost) {
                    currentNeighbour.pathCost = currentNode.pathCost + connection.weight;
                    currentNeighbour.connection = new PathConnection(connection.type, currentNode);
                }
            }

            foreach (var connection in graphNode.connections) {
                CheckConnection(connection);
            }
            foreach (var connection in graphNode.dynamicConnections) {
                CheckConnection(connection);
            }
        }
        if (Timers.active) {
            Timers.findPath.Stop();
        }
        return null;
    }
}

static class PathfinderHooks {
    public static void RegisterHooks() {
        On.Player.ctor += Player_ctor;
        On.Player.Update += Player_Update;
    }

    public static void UnregisterHooks() {
        On.Player.ctor -= Player_ctor;
        On.Player.Update -= Player_Update;
    }

    private static void Player_ctor(On.Player.orig_ctor orig, Player self, AbstractCreature abstractCreature, World world) {
        orig(self, abstractCreature, world);
        self.GetCWT().pathfinder = new Pathfinder(self);
    }

    private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu) {
        // horrible hack because creature creation is pain
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
                var realizedScug = (abstractScug.realizedCreature as Player)!;
                realizedScug.controller = null;
            }
        }
        orig(self, eu);
        self.GetCWT().pathfinder?.Update();
    }
}