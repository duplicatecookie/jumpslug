using System;

using Mono.Cecil.Cil;

using MonoMod.Cil;

using IVec2 = RWCustom.IntVector2;

using UnityEngine;

using JumpSlug.Pathfinding;
using RWCustom;

namespace JumpSlug;

class JumpSlugAbstractAI : AbstractCreatureAI {
    public JumpSlugAbstractAI(AbstractCreature abstractCreature, World world) : base(world, abstractCreature) { }
}

class JumpSlugAI : ArtificialIntelligence, IUseARelationshipTracker {
    private readonly Player _slugcat;
    private Room _room;
    private bool _waitOneTick;
    private IVec2? _destination;
    private readonly Pathfinder _pathfinder;
    private GraphNode? _currentNode;
    private PathConnection? _currentConnection;
    private bool _performingAirMovement;
    private float _pathThreat;
    private readonly EscapeFinder _escapeFinder;
    private Visualizer? _visualizer;
    private bool _visualizeNode;
    private readonly NodeVisualizer _nodeVisualizer;
    private bool _visualizeThreat;
    private Pathfinder.ThreatMapVisualizer? _threatVisualizer;

    public JumpSlugAI(AbstractCreature abstractCreature, World world) : base(abstractCreature, world) {
        _slugcat = (Player)abstractCreature.realizedCreature;
        _room = creature.Room.realizedRoom;
        AddModule(new Tracker(this, 10, 10, -1, 0.5f, 5, 5, 10));
        AddModule(new ThreatTracker(this, 10));
        AddModule(new RelationshipTracker(this, tracker));
        var graphs = _room.GetCWT().DynamicGraphs;
        var descriptor = new SlugcatDescriptor(_slugcat);
        if (!graphs.TryGetValue(descriptor, out var dynGraph)) {
            dynGraph = new DynamicGraph(_room, descriptor.ToJumpVectors());
            graphs.Add(descriptor, dynGraph);
        }
        _pathfinder = new Pathfinder(_room!, dynGraph, threatTracker);
        _nodeVisualizer = new NodeVisualizer(_room!, _pathfinder.DynamicGraph);
        _escapeFinder = new EscapeFinder();
    }

    AIModule? IUseARelationshipTracker.ModuleToTrackRelationship(CreatureTemplate.Relationship relationship) {
        if (relationship.type == CreatureTemplate.Relationship.Type.Afraid) {
            return threatTracker;
        }
        return null;
    }

    CreatureTemplate.Relationship IUseARelationshipTracker.UpdateDynamicRelationship(RelationshipTracker.DynamicRelationship dRelation) {
        return StaticRelationship(dRelation.trackerRep.representedCreature);
    }

    RelationshipTracker.TrackedCreatureState IUseARelationshipTracker.CreateTrackedCreatureState(RelationshipTracker.DynamicRelationship rel) {
        return new RelationshipTracker.TrackedCreatureState();
    }

    public override float VisualScore(Vector2 lookAtPoint, float bonus) {
        float distance = Vector2.Distance(_slugcat.mainBodyChunk.pos, lookAtPoint) * (1 + bonus);
        if (distance > _slugcat.abstractCreature.creatureTemplate.visualRadius) {
            return 0;
        } else {
            // TODO: add restrictions for water and ambush predators (white lizards, dropswigs, pole plants)
            return 1;
        }
    }

    public override void NewRoom(Room room) {
        base.NewRoom(room);
        if (_room != room) {
            _room = room;
            _pathfinder.NewRoom(room);
            _visualizer?.NewRoom(room);
            _nodeVisualizer.NewRoom(room, _pathfinder.DynamicGraph);
            _threatVisualizer?.NewRoom(room);
        }
    }

    public override void Update() {
        base.Update();
        if (_room is null) {
            return;
        }
        foreach (var layer in _room.physicalObjects) {
            foreach (var obj in layer) {
                if (obj is Creature creature) {
                    tracker.SeeCreature(creature.abstractCreature);
                }
            }
        }
        if (InputHelper.JustPressedMouseButton(0)) {
            var mousePos = (Vector2)UnityEngine.Input.mousePosition + _room!.game.cameras[0].pos;
            _destination = _room.GetTilePosition(mousePos);
            _currentConnection = FindPath();
            _visualizer?.UpdatePath();
        }

        float lastThreat = _pathThreat;
        if (_currentNode is not null && _currentConnection is PathConnection connection) {
            _pathThreat = threatTracker.ThreatAlongPath(_currentNode.GridPos, connection, 15);
        }

        if (threatTracker.mostThreateningCreature is not null) {
            if (_currentNode is not null
                && (_currentConnection is null
                && threatTracker
                    .mostThreateningCreature
                    .representedCreature
                    .abstractAI
                    .RealAI
                    .pathFinder
                    .CoordinateReachable(_room.ToWorldCoordinate(_currentNode.GridPos))
                || _pathThreat > lastThreat + 10
                )
            ) {
                FindEscapePathAndDestination();
            }
        }

        if (_waitOneTick || _destination is null) {
            _slugcat.input[0] = default;
            _waitOneTick = false;
        } else {
            var input = _performingAirMovement ? MoveInAir() : Move();
            _slugcat.input[0] = default(Player.InputPackage) with {
                x = input.Direction.x,
                y = input.Direction.y,
                jmp = input.Jump,
            };
        }

        UpdateVisualization();
    }

    private void UpdateVisualization() {
        if (InputHelper.JustPressed(KeyCode.P)) {
            _visualizer ??= new Visualizer(this);
            if (_visualizer.Active) {
                _visualizer.Deactivate();
            } else {
                _visualizer.Activate();
            }
        }
        _visualizer?.Update();

        if (InputHelper.JustPressed(KeyCode.T)) {
            _threatVisualizer ??= new Pathfinder.ThreatMapVisualizer(_pathfinder);
            _visualizeThreat = !_visualizeThreat;
        }

        if (_visualizeThreat) {
            _threatVisualizer?.Display();
        } else {
            _threatVisualizer?.Hide();
        }

        if (InputHelper.JustPressed(KeyCode.M)) {
            _visualizeNode = !_visualizeNode;
        }

        if (_visualizeNode) {
            var node = CurrentNode();
            if (node is null) {
                _nodeVisualizer.ResetSprites();
            } else {
                _nodeVisualizer.VisualizeNode(node);
            }
        } else {
            _nodeVisualizer.ResetSprites();
        }
    }

    private void FindEscapePathAndDestination() {
        if (!_performingAirMovement && _currentNode is not null) {
            var result = _pathfinder.FindPathFrom(
                _currentNode.GridPos,
                _escapeFinder
            );
            if (result is (IVec2, PathConnection) tuple) {
                _destination = tuple.destination;
                _currentConnection = tuple.connection;
                _visualizer?.UpdatePath();
            } else {
                Plugin.Logger!.LogDebug("failed to find escape route");
            }
        }
    }

    private PathConnection? FindPath(bool forceReset = false) {
        if (_currentNode is null || _destination is null) {
            return null;
        } else {
            return _pathfinder.FindPathTo(
                _currentNode.GridPos,
                _destination.Value,
                forceReset
            );
        }
    }

    private bool KeepFalling(GraphNode startNode) {
        _visualizer?.ResetPredictionSprites();
        if (startNode is null) {
            return true;
        }
        if (_currentConnection is null || _destination is null) {
            return false;
        }
        var startPathNode = _pathfinder.PathNodePool[startNode.GridPos];
        if (startPathNode is null) {
            return true;
        }

        var sharedGraph = _slugcat.room.GetCWT().SharedGraph!;
        IVec2 headPos = RoomHelper.TilePosition(_slugcat.bodyChunks[0].pos);
        int x = headPos.x;
        int y = headPos.y;
        if (x < 0 || y < 0 || x >= sharedGraph.Width || y >= sharedGraph.Height) {
            return false;
        }
        Vector2 v0 = _slugcat.mainBodyChunk.vel;
        if (v0.x == 0) {
            while (y > 0) {
                y--;
                var currentNode = sharedGraph.Nodes[x, y];
                var currentPathNode = _pathfinder.PathNodePool[x, y];
                if (currentNode is null || currentPathNode is null) {
                    continue;
                }

                _visualizer?.AddPredictionSprite(x, y);

                if (!_pathfinder.ClosedNodes[x, y]) {
                    _pathfinder.FindPathTo(
                        new IVec2(x, y),
                        _destination.Value
                    );
                }
                if (currentPathNode.PathCost < startPathNode.PathCost) {
                    return true;
                }
                if (currentNode.Type is NodeType.Floor or NodeType.Slope) {
                    break;
                }
            }
        } else {
            int direction = v0.x > 0 ? 1 : -1;
            int xOffset = (direction + 1) / 2;
            var pathOffset = RoomHelper.MiddleOfTile(headPos);

            while (true) {
                float t = (20 * (x + xOffset) - pathOffset.x) / v0.x;
                float result = DynamicGraph.Parabola(pathOffset.y, v0, _room!.gravity, t) / 20;
                if (result > y + 1) {
                    y++;
                } else if (result < y) {
                    y--;
                } else {
                    x += direction;
                }

                if (x < 0 || y < 0 || x >= sharedGraph.Width || y >= sharedGraph.Height
                    || _room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Solid
                    || _room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Slope) {
                    break;
                }

                var currentPathNode = _pathfinder.PathNodePool[x, y];
                if (currentPathNode is null) {
                    continue;
                }

                _visualizer?.AddPredictionSprite(x, y);

                if (!_pathfinder.ClosedNodes[x, y]) {
                    _pathfinder.FindPathTo(
                        new IVec2(x, y),
                        _destination.Value
                    );
                }
                if (currentPathNode.PathCost < startPathNode.PathCost) {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Find the node the requested slugcat is currently at.
    /// </summary>
    /// <returns>
    /// Null if the slugcat is not located at any node in the graph.
    /// </returns>
    public GraphNode? CurrentNode() {
        var sharedGraph = _slugcat.room.GetCWT().SharedGraph!;
        IVec2 headPos = RoomHelper.TilePosition(_slugcat.bodyChunks[0].pos);
        IVec2 footPos = RoomHelper.TilePosition(_slugcat.bodyChunks[1].pos);
        if (_slugcat.bodyMode == Player.BodyModeIndex.Stand
            || _slugcat.animation == Player.AnimationIndex.StandOnBeam
            || _slugcat.animation == Player.AnimationIndex.BeamTip
            || _slugcat.animation == Player.AnimationIndex.StandUp
        ) {
            return sharedGraph.GetNode(footPos) is GraphNode node
                ? node
                : sharedGraph.GetNode(footPos.x, footPos.y - 1);
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.Crawl
            || _slugcat.bodyMode == Player.BodyModeIndex.CorridorClimb
        ) {
            return sharedGraph.GetNode(headPos) is GraphNode node
                ? node
                : sharedGraph.GetNode(footPos);
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.Default) {
            if (sharedGraph.GetNode(headPos) is GraphNode node) {
                return node;
            } else if (sharedGraph.GetNode(footPos.x, footPos.y - 1) is GraphNode footNode) {
                return footNode.Type is NodeType.Slope ? footNode : null;
            }
        }
        return sharedGraph.GetNode(headPos);
    }

    private Input Move() {
        _currentNode = CurrentNode();
        if (_currentNode is null) {
            if (_currentConnection?.Type is ConnectionType.Drop) {
                _performingAirMovement = true;
            } else {
                _currentConnection = null;
                _visualizer?.UpdatePath();
            }
            return default;
        } else if (_currentNode.GridPos == _destination) {
            _currentConnection = null;
            _destination = null;
            _visualizer?.UpdatePath();
            return default;
        } else if (_currentNode.HasPlatform && _slugcat.bodyMode == Player.BodyModeIndex.Default) {
            _performingAirMovement = true;
            return default;
        } else {
            _currentConnection = FindPath();
            _visualizer?.UpdatePath();
            return GenerateInputs();
        }
    }

    private Input MoveInAir() {
        var node = CurrentNode();
        if (node is null || node == _currentNode) {
            if (_currentConnection is null) {
                _performingAirMovement = false;
                return Input.DoNothing;
            } else {
                var currentConnection = _currentConnection!.Value;
                if (currentConnection.Type is ConnectionType.Jump(int jumpDir)) {
                    bool jump;
                    if (_slugcat.jumpBoost > 0
                        || _slugcat.bodyMode == Player.BodyModeIndex.ClimbingOnBeam
                    ) {
                        jump = true;
                    } else {
                        jump = false;
                    }
                    return new Input {
                        Direction = new IVec2(jumpDir, 0),
                        Jump = jump,
                    };
                } else if (currentConnection.Type is ConnectionType.JumpUp) {
                    if (_slugcat.jumpBoost > 0
                        || _slugcat.bodyMode == Player.BodyModeIndex.ClimbingOnBeam
                    ) {
                        return new Input {
                            Direction = Consts.IVec2.Up,
                            Jump = true,
                        };
                    } else {
                        return Input.DoNothing;
                    }
                } else if (currentConnection.Type is ConnectionType.WalkOffEdge(int ledgeDir)) {
                    return new Input {
                        Direction = new IVec2(ledgeDir, 0),
                        Jump = false,
                    };
                } else if (currentConnection.Type is ConnectionType.Pounce(int pounceDir)) {
                    return new Input {
                        Direction = new IVec2(pounceDir, 0),
                        Jump = false,
                    };
                } else if (currentConnection.Type is ConnectionType.Drop
                    && node?.Type is NodeType.Floor
                ) {
                    return new Input {
                        Direction = Consts.IVec2.Down,
                        Jump = false,
                    };
                } else {
                    return Input.DoNothing;
                }
            }
        } else {
            var connection = _currentConnection?.FindInPath(node.GridPos);
            if (connection is not null) {
                _currentNode = node;
                _currentConnection = connection;
                _performingAirMovement = false;
                return GenerateInputs();
            } else if (KeepFalling(node)) {
                var currentConnection = _currentConnection!.Value;
                if (currentConnection.Type is ConnectionType.Jump(int jumpDir)) {
                    bool jump;
                    if (_slugcat.jumpBoost > 0) {
                        jump = true;
                    } else {
                        jump = false;
                    }
                    return new Input {
                        Direction = new IVec2(jumpDir, 0),
                        Jump = jump,
                    };
                } else if (currentConnection.Type is ConnectionType.WalkOffEdge(int walkDir)) {
                    return new Input {
                        Direction = new IVec2(walkDir, 0),
                        Jump = false,
                    };
                } else if (currentConnection.Type is ConnectionType.Pounce(int pounceDir)) {
                    return new Input {
                        Direction = new IVec2(pounceDir, 0),
                        Jump = false,
                    };
                } else {
                    return Input.DoNothing;
                }
            } else {
                _performingAirMovement = false;
                _currentNode = node;
                _currentConnection = FindPath();
                _visualizer?.UpdatePath();
                return GenerateInputs();
            }
        }
    }

    private Input GenerateInputs() {
        if (_currentNode is null) {
            return Input.DoNothing;
        }
        if (_currentConnection is null) {
            if (_currentNode?.HasBeam == true && _slugcat.bodyMode != Player.BodyModeIndex.ClimbingOnBeam) {
                return new Input {
                    Direction = Consts.IVec2.Up,
                    Jump = false,
                };
            } else {
                return new Input {
                    Direction = Consts.IVec2.Zero,
                    Jump = false,
                };
            }
        }
        var currentConnection = _currentConnection.Value;
        if (currentConnection.Type is ConnectionType.Walk) {
            return Walk(currentConnection, _currentNode);
        } else if (currentConnection.Type is ConnectionType.Crawl) {
            return Crawl(currentConnection, _currentNode);
        } else if (currentConnection.Type is ConnectionType.Climb) {
            return Climb(currentConnection, _currentNode);
        } else if (currentConnection.Type is ConnectionType.Drop) {
            return Drop();
        } else if (currentConnection.Type is ConnectionType.Jump(int jumpDir)) {
            return Jump(jumpDir);
        } else if (currentConnection.Type is ConnectionType.JumpUp) {
            return JumpUp();
        } else if (currentConnection.Type is ConnectionType.WalkOffEdge(int walkDir1)) {
            return WalkOffEdge(walkDir1);
        } else if (currentConnection.Type is ConnectionType.SlideOnWall(int wallDir)) {
            if (currentConnection.PeekType(1) is ConnectionType.Walk(int walkDir2)) {
                return new Input {
                    Direction = new IVec2(walkDir2, 0),
                    Jump = false,
                };
            }
            return new Input {
                Direction = new IVec2(wallDir, 0),
                Jump = false,
            };
        } else if (currentConnection.Type is ConnectionType.Pounce(int pounceDir)) {
            return Pounce(pounceDir);
        } else {
            Plugin.Logger!.LogWarning($"trying to follow connection of type {currentConnection.Type} but no logic exists to handle it");
            return Input.DoNothing;
        }
    }

    private Input Walk(PathConnection currentConnection, GraphNode currentNode) {
        int walkDir = ((ConnectionType.Walk)currentConnection.Type).Direction;
        IVec2 headPos = RoomHelper.TilePosition(_slugcat.bodyChunks[0].pos);
        IVec2 footPos = RoomHelper.TilePosition(_slugcat.bodyChunks[1].pos);
        var sharedGraph = _room.GetCWT().SharedGraph!;
        if (_slugcat.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
            if (sharedGraph.GetNode(footPos)?.Type is NodeType.Corridor) {
                return new Input {
                    Direction = new IVec2(walkDir, 0),
                    Jump = false,
                };
            } else if (currentNode.GridPos.y == headPos.y) {
                return new Input {
                    Direction = Consts.IVec2.Up,
                    Jump = false,
                };
            } else if (_slugcat.animation == Player.AnimationIndex.ClimbOnBeam) {
                return new Input {
                    Direction = Consts.IVec2.Down,
                    Jump = true,
                };
            } else if (_slugcat.animation == Player.AnimationIndex.BeamTip) {
                return new Input {
                    Direction = new IVec2(walkDir, 0),
                    Jump = false,
                };
            }
            Plugin.Logger!.LogError($"missing movement logic: ConnectionType.Walk, mode: {_slugcat.bodyMode}, animation: {_slugcat.animation}");
            return Input.DoNothing;
        } else if (currentConnection.PeekType(1) is ConnectionType.Crawl(IVec2 crawlDir)) {
            if (crawlDir.y < 0) {
                if (_slugcat.animation != Player.AnimationIndex.DownOnFours
                    && _slugcat.bodyMode != Player.BodyModeIndex.Default
                ) {
                    return new Input {
                        Direction = new IVec2(walkDir, -1),
                        Jump = false,
                    };
                }
                return new Input {
                    Direction = Consts.IVec2.Down,
                    Jump = false,
                };
            } else {
                int yDir;
                if (crawlDir.x != 0 && currentConnection.PeekPos(2)?.y == currentNode.GridPos.y) {
                    yDir = -1;
                } else {
                    yDir = 0;
                }
                return new Input {
                    Direction = new IVec2(walkDir, yDir),
                    Jump = false,
                };
            }
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.Default && currentNode.Beam == GraphNode.BeamType.Above) {
            return new Input {
                Direction = new IVec2(walkDir, 0),
                Jump = false,
            };
        } else {
            int yDir = 0;
            if (currentConnection.PeekType(1) is ConnectionType.Drop
                || currentConnection.PeekType(2) is ConnectionType.Drop
            ) {
                if (_slugcat.bodyMode == Player.BodyModeIndex.Stand) {
                    yDir = -1;
                }
            } else if (_slugcat.bodyMode == Player.BodyModeIndex.Crawl) {
                yDir = 1;
            }
            return new Input {
                Direction = new IVec2(walkDir, yDir),
                Jump = false,
            };
        }
    }

    private Input Crawl(PathConnection currentConnection, GraphNode currentNode) {
        IVec2 crawlDir = ((ConnectionType.Crawl)currentConnection.Type).Direction;
        // the sign of the dot product indicates whether the angle between two vectors is greater or lesser than 90°
        bool backwards = (_slugcat.bodyChunks[0].pos - _slugcat.bodyChunks[1].pos).Dot(crawlDir.ToVector2()) < 0;
        // turn around if going backwards
        // should not trigger when in a corner because that can lock the ai into switching forever when trying to go up an inverse T junction
        if (_slugcat.bodyMode != Player.BodyModeIndex.WallClimb) {
            if (_slugcat.bodyMode == Player.BodyModeIndex.Default
                && _slugcat.animation == Player.AnimationIndex.StandUp
            ) {
                _waitOneTick = true;
                return Input.DoNothing;
            } else if (currentConnection.PeekType(1) is ConnectionType.Crawl(IVec2 nextDir)
                && currentNode.Type is not NodeType.Floor
                && backwards
            ) {
                if (crawlDir == nextDir) {
                    return new Input {
                        Direction = crawlDir,
                        Jump = true,
                    };
                } else if (crawlDir.Dot(nextDir) == 0) {
                    return new Input {
                        Direction = nextDir,
                        Jump = false,
                    };
                }
            }
        }
        return new Input {
            Direction = crawlDir,
            Jump = false,
        };
    }

    private Input Climb(PathConnection currentConnection, GraphNode currentNode) {
        IVec2 climbDir = ((ConnectionType.Climb)currentConnection.Type).Direction;
        IVec2 footPos = RoomHelper.TilePosition(_slugcat.bodyChunks[1].pos);
        if (_slugcat.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
            if (currentConnection.PeekType(1) is ConnectionType.Walk) {
                if (climbDir.y < 0) {
                    return new Input {
                        Direction = Consts.IVec2.Down,
                        Jump = true,
                    };
                } else if (climbDir.y > 0) {
                    return new Input {
                        Direction = Consts.IVec2.Up,
                        Jump = false,
                    };
                } else {
                    return new Input {
                        Direction = climbDir with { y = 0 },
                        Jump = false,
                    };
                }
            } else {
                int yDir = 0;
                if (climbDir.x != 0 && (_slugcat.flipDirection != climbDir.x || _slugcat.animation == Player.AnimationIndex.ClimbOnBeam)) {
                    _waitOneTick = true;
                } else if (_slugcat.animation == Player.AnimationIndex.StandOnBeam) {
                    if (climbDir.y != 0) {
                        yDir = 1;
                    }
                    if (climbDir.x != 0) {
                        var nextPos = currentConnection.PeekPos(2);
                        if (nextPos is not null) {
                            var terrain = _room!.GetTile(nextPos.Value.x, nextPos.Value.y + 1).Terrain;
                            if (terrain == Room.Tile.TerrainType.Solid
                                || terrain == Room.Tile.TerrainType.Slope
                            ) {
                                yDir = -1;
                            }
                        }
                    }
                } else if (_slugcat.animation != Player.AnimationIndex.GetUpOnBeam
                    && currentNode.Beam == GraphNode.BeamType.Horizontal
                    && _room!.GetTile(currentNode.GridPos.x, currentNode.GridPos.y + 1).Terrain == Room.Tile.TerrainType.Air
                    && _slugcat.input[1].y != 1
                ) {
                    var nextPos = currentConnection.PeekPos(2);
                    if (nextPos is not null) {
                        var terrain = _room!.GetTile(nextPos.Value.x, nextPos.Value.y + 1).Terrain;
                        if (terrain != Room.Tile.TerrainType.Solid
                            && terrain != Room.Tile.TerrainType.Slope
                        ) {
                            yDir = 1;
                        }
                    }
                } else {
                    yDir = climbDir.y;
                }
                return new Input {
                    Direction = climbDir with { y = yDir },
                    Jump = false,
                };
            }
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.CorridorClimb) {
            return new Input {
                Direction = climbDir,
                Jump = false,
            };
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.Default) {
            if (currentConnection.PeekType(1) is ConnectionType.Walk(int walkDir1) && climbDir.y < 0) {
                return new Input {
                    Direction = new IVec2(walkDir1, 0),
                    Jump = false,
                };
            } else {
                return new Input {
                    Direction = climbDir with { y = 1 },
                    Jump = false,
                };
            }
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.Stand
            && (currentNode.GridPos == footPos
                || currentNode.GridPos == new IVec2(footPos.x, footPos.y - 1))
            && climbDir.x != 0
        ) {
            return new Input {
                Direction = climbDir with { y = 0 },
                Jump = false,
            };
        } else {
            if (currentNode.Beam != GraphNode.BeamType.None) {
                _waitOneTick = true;
                return new Input {
                    Direction = climbDir with { y = 1 },
                    Jump = false,
                };
            } else {
                Plugin.Logger!.LogWarning("trying to climb on node without pole");
                return new Input {
                    Direction = climbDir,
                    Jump = false,
                };
            }
        }
    }
    private Input Drop() {
        if (Mathf.Abs(_slugcat.mainBodyChunk.vel.x) < 0.5f) {
            if (_slugcat.animation == Player.AnimationIndex.HangUnderVerticalBeam) {
                _waitOneTick = true;
                return new Input {
                    Direction = Consts.IVec2.Down,
                    Jump = false,
                };
            } else if (_slugcat.animation == Player.AnimationIndex.ClimbOnBeam) {
                _performingAirMovement = true;
                return new Input {
                    Direction = Consts.IVec2.Down,
                    Jump = true,
                };
            } else {
                return new Input {
                    Direction = Consts.IVec2.Down,
                    Jump = false,
                };
            }
        }
        return Input.DoNothing;
    }
    private Input Jump(int jumpDir) {
        IVec2 headPos = RoomHelper.TilePosition(_slugcat.bodyChunks[0].pos);
        IVec2 footPos = RoomHelper.TilePosition(_slugcat.bodyChunks[1].pos);
        if (_currentNode is null) {
            return Input.DoNothing;
        }
        if (_slugcat.bodyMode == Player.BodyModeIndex.Stand) {
            bool jump;
            if (_slugcat.flipDirection == jumpDir) {
                _performingAirMovement = true;
                jump = true;
            } else {
                jump = false;
            }
            return new Input {
                Direction = new IVec2(jumpDir, 0),
                Jump = jump,
            };
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.WallClimb) {
            _performingAirMovement = true;
            return new Input {
                Direction = new IVec2(jumpDir, 0),
                Jump = true,
            };
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
            if (_slugcat.animation == Player.AnimationIndex.ClimbOnBeam) {
                bool jump;
                if (_slugcat.flipDirection == jumpDir) {
                    jump = true;
                    _performingAirMovement = true;
                } else {
                    jump = false;
                }
                return new Input {
                    Direction = new IVec2(jumpDir, 0),
                    Jump = jump,
                };
            } else if (_slugcat.animation == Player.AnimationIndex.HangFromBeam) {
                _waitOneTick = true;
                return new Input {
                    Direction = Consts.IVec2.Up,
                    Jump = false,
                };
            } else if (_slugcat.animation == Player.AnimationIndex.StandOnBeam) {
                if (headPos.x == footPos.x
                    && headPos.y == footPos.y + 1
                    && _slugcat.bodyChunks[0].vel.x < 5f
                ) {
                    _performingAirMovement = true;
                    return new Input {
                        Direction = new IVec2(jumpDir, 0),
                        Jump = true,
                    };
                } else {
                    return new Input {
                        Direction = Consts.IVec2.Up,
                        Jump = false,
                    };
                }
            } else if (_slugcat.animation == Player.AnimationIndex.BeamTip) {
                return new Input {
                    Direction = new IVec2(jumpDir, 0),
                    Jump = true,
                };
            } else {
                Plugin.Logger!.LogError($"missing movement logic: ConnectionType.Jump, mode: {_slugcat.bodyMode}, animation: {_slugcat.animation}");
                return Input.DoNothing;
            }
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.Default) {
            if (_currentNode.Type is NodeType.Wall(int wallDir)) {
                return new Input {
                    Direction = new IVec2(wallDir, 0),
                    Jump = true,
                };
            } else if (_currentNode.HasBeam) {
                return new Input {
                    Direction = Consts.IVec2.Up,
                    Jump = true,
                };
            } else {
                return Input.DoNothing;
            }
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.Crawl) {
            if (footPos.x == _currentNode.GridPos.x - jumpDir) {
                return new Input {
                    Direction = new IVec2(jumpDir, 1),
                    Jump = false,
                };
            }
            return new Input {
                Direction = Consts.IVec2.Up,
                Jump = false,
            };
        } else {
            Plugin.Logger!.LogError($"missing movement logic: ConnectionType.Jump, mode: {_slugcat.bodyMode}, animation: {_slugcat.animation}");
            return Input.DoNothing;
        }
    }

    private Input JumpUp() {
        IVec2 headPos = RoomHelper.TilePosition(_slugcat.bodyChunks[0].pos);
        IVec2 footPos = RoomHelper.TilePosition(_slugcat.bodyChunks[1].pos);
        if (_slugcat.bodyMode == Player.BodyModeIndex.Stand) {
            _performingAirMovement = true;
            return new Input {
                Direction = Consts.IVec2.Zero,
                Jump = true,
            };
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
            if (_slugcat.animation == Player.AnimationIndex.StandOnBeam
                || _slugcat.animation == Player.AnimationIndex.BeamTip
            ) {
                if (headPos.x == footPos.x
                    && headPos.y == footPos.y + 1
                    && _slugcat.bodyChunks[0].vel.x < 5f
                ) {
                    _performingAirMovement = true;
                    return new Input {
                        Direction = Consts.IVec2.Zero,
                        Jump = true,
                    };
                } else {
                    return new Input {
                        Direction = Consts.IVec2.Up,
                        Jump = false,
                    };
                }
            } else if (_slugcat.animation == Player.AnimationIndex.HangFromBeam) {
                _waitOneTick = true;
                return new Input {
                    Direction = Consts.IVec2.Up,
                    Jump = false,
                };
            } else {
                Plugin.Logger!.LogError($"missing movement logic: ConnectionType.JumpUp, mode: {_slugcat.bodyMode}, animation: {_slugcat.animation}");
                return Input.DoNothing;
            }
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.Crawl) {
            return new Input {
                Direction = Consts.IVec2.Up,
                Jump = false,
            };
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.WallClimb) {
            return new Input {
                Direction = new IVec2(-_slugcat.flipDirection, 0),
                Jump = false,
            };
        } else {
            Plugin.Logger!.LogError($"missing movement logic: ConnectionType.JumpUp, mode: {_slugcat.bodyMode}, animation: {_slugcat.animation}");
            return Input.DoNothing;
        }
    }

    private Input WalkOffEdge(int walkDir) {
        IVec2 headPos = RoomHelper.TilePosition(_slugcat.bodyChunks[0].pos);
        IVec2 footPos = RoomHelper.TilePosition(_slugcat.bodyChunks[1].pos);
        if (_slugcat.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
            if (_slugcat.animation == Player.AnimationIndex.ClimbOnBeam) {
                _performingAirMovement = true;
                return new Input {
                    Direction = new IVec2(walkDir, -1),
                    Jump = true,
                };
            } else if (_slugcat.animation == Player.AnimationIndex.StandOnBeam) {
                if (headPos.x == footPos.x
                    && headPos.y == footPos.y + 1
                    && _slugcat.bodyChunks[0].vel.x < 5f
                ) {
                    _performingAirMovement = true;
                    return new Input {
                        Direction = new IVec2(walkDir, -1),
                        Jump = false,
                    };
                } else {
                    return Input.DoNothing;
                }
            } else {
                _performingAirMovement = true;
                return new Input {
                    Direction = Consts.IVec2.Down,
                    Jump = false,
                };
            }
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.Stand) {
            _performingAirMovement = true;
            return new Input {
                Direction = new IVec2(walkDir, 0),
                Jump = false,
            };
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.Crawl) {
            return new Input {
                Direction = Consts.IVec2.Up,
                Jump = false,
            };
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.CorridorClimb) {
            return new Input {
                Direction = new IVec2(walkDir, 0),
                Jump = false,
            };
        } else {
            Plugin.Logger!.LogError($"missing movement logic: ConnectionType.WalkOffEdge, mode: {_slugcat.bodyMode}, animation: {_slugcat.animation}");
            return Input.DoNothing;
        }
    }

    private Input Pounce(int pounceDir) {
        if (_slugcat.bodyMode == Player.BodyModeIndex.Stand) {
            if (_slugcat.flipDirection != pounceDir) {
                return new Input {
                    Direction = new IVec2(pounceDir, -1),
                    Jump = false,
                };
            } else {
                return new Input {
                    Direction = Consts.IVec2.Down,
                    Jump = false,
                };
            }
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.Crawl) {
            if (_slugcat.flipDirection != pounceDir) {
                return new Input {
                    Direction = new IVec2(pounceDir, 0),
                    Jump = false,
                };
            } else if (_slugcat.superLaunchJump < 20) {
                // hold down jump button to set up pounce
                return new Input {
                    Direction = Consts.IVec2.Zero,
                    Jump = true,
                };
            } else {
                // release jump button to perform pounce
                _performingAirMovement = true;
                return Input.DoNothing;
            }
        } else if (_slugcat.bodyMode == Player.BodyModeIndex.Default) {
            return Input.DoNothing;
        } else {
            Plugin.Logger!.LogError($"missing movement logic: ConnectionType.Pounce, mode: {_slugcat.bodyMode}, animation: {_slugcat.animation}");
            return Input.DoNothing;
        }
    }

    private struct Input {
        public static readonly Input DoNothing = new Input {
            Direction = Consts.IVec2.Zero,
            Jump = false,
        };
        public IVec2 Direction;
        public bool Jump;
        public Input(IVec2 direction, bool jump) {
            Direction = direction;
            Jump = jump;
        }
    }

    private class Visualizer {
        private const int MAX_SPRITES = 16;
        private readonly JumpSlugAI _ai;
        private Room _room;
        private readonly PathVisualizer _pathVisualizer;
        private readonly DebugSprite _inputDirSprite;
        private readonly DebugSprite _currentNodeSprite;
        private int _spriteIndex;
        private readonly DebugSprite[] _predictedIntersectionSprites;
        private readonly FLabel _currentConnectionLabel;
        private readonly FLabel _jumpBoostLabel;
        public bool Active { get; private set; }
        public Visualizer(JumpSlugAI ai) {
            _ai = ai;
            _room = _ai._room!;
            _pathVisualizer = new PathVisualizer(_room, _ai._pathfinder);
            _inputDirSprite = new DebugSprite(Vector2.zero, TriangleMesh.MakeLongMesh(1, false, true), _room);
            _inputDirSprite.sprite.color = Color.red;
            _inputDirSprite.sprite.isVisible = false;
            _currentNodeSprite = new DebugSprite(
                Vector2.zero,
                new FSprite("pixel") {
                    scale = 10f,
                    color = Color.blue,
                    isVisible = false
                },
                _room
            );
            _spriteIndex = 0;
            _predictedIntersectionSprites = new DebugSprite[MAX_SPRITES];
            for (int i = 0; i < MAX_SPRITES; i++) {
                _predictedIntersectionSprites[i] = new DebugSprite(
                    Vector2.zero,
                    new FSprite("pixel") {
                        scale = 10f,
                        color = Color.green,
                        isVisible = false,
                    },
                    _room
                );
            }
            _currentConnectionLabel = new FLabel(Custom.GetFont(), "None") {
                alignment = FLabelAlignment.Center,
                color = Color.white,
            };

            _jumpBoostLabel = new FLabel(Custom.GetFont(), "None") {
                alignment = FLabelAlignment.Center,
                color = Color.white,
            };

            var container = _room!.game.cameras[0].ReturnFContainer("Foreground");
            container.AddChild(_currentConnectionLabel);
            container.AddChild(_jumpBoostLabel);
            _room.AddObject(_inputDirSprite);
            _room.AddObject(_currentNodeSprite);
            foreach (var sprite in _predictedIntersectionSprites) {
                _room.AddObject(sprite);
            }
        }

        public void NewRoom(Room room) {
            if (room != _room) {
                _room = room;
                _pathVisualizer.NewRoom(room);
                _room.AddObject(_inputDirSprite);
                _room.AddObject(_currentNodeSprite);
                foreach (var sprite in _predictedIntersectionSprites) {
                    _room.AddObject(sprite);
                }
            }
        }

        public void Update() {
            if (Active) {
                if (_ai._currentConnection is PathConnection connection) {
                    _currentConnectionLabel.text = connection.Type.ToString();
                } else {
                    _currentConnectionLabel.text = "None";
                }
                if (_ai._performingAirMovement) {
                    _currentConnectionLabel.color = Color.green;
                } else {
                    ResetPredictionSprites();
                    _currentConnectionLabel.color = Color.white;
                }

                _jumpBoostLabel.text = _ai._slugcat.jumpBoost.ToString();

                var labelPos = _ai._slugcat.bodyChunks[0].pos - _room.game.cameras[0].pos;
                labelPos.y += 60;
                _currentConnectionLabel.SetPosition(labelPos);
                labelPos.y += 20;
                _jumpBoostLabel.SetPosition(labelPos);

                if (_ai._currentNode is not null) {
                    _currentNodeSprite.sprite.isVisible = true;
                    _currentNodeSprite.pos = RoomHelper.MiddleOfTile(_ai._currentNode.GridPos);
                } else {
                    _currentNodeSprite.sprite.isVisible = false;
                }

                if (_ai._slugcat.input[0].x == 0 && _ai._slugcat.input[0].y == 0) {
                    _inputDirSprite.sprite.isVisible = false;
                } else {
                    _inputDirSprite.pos = _ai._slugcat.mainBodyChunk.pos;
                    _inputDirSprite.sprite.isVisible = true;
                    if (_ai._slugcat.input[0].jmp == true) {
                        _inputDirSprite.sprite.color = Color.green;
                    } else {
                        _inputDirSprite.sprite.color = Color.red;
                    }
                    LineHelper.ReshapeLine(
                        (TriangleMesh)_inputDirSprite.sprite,
                        _ai._slugcat.mainBodyChunk.pos,
                        new Vector2(
                            _ai._slugcat.mainBodyChunk.pos.x + _ai._slugcat.input[0].x * 50,
                            _ai._slugcat.mainBodyChunk.pos.y + _ai._slugcat.input[0].y * 50
                        )
                    );
                }
            }
        }

        public void UpdatePath() {
            if (!Active || _ai._currentConnection is null || _ai._currentNode is null) {
                _pathVisualizer.ClearPath();
            } else if (_room.GetCWT().DynamicGraphs.TryGetValue(new SlugcatDescriptor(_ai._slugcat), out var graph)) {
                _pathVisualizer.DisplayPath(
                    _ai._currentNode.GridPos,
                    _ai._currentConnection.Value,
                    graph
                );
            }
        }

        public void AddPredictionSprite(int x, int y) {
            if (_spriteIndex < MAX_SPRITES) {
                var sprite = _predictedIntersectionSprites[_spriteIndex];
                sprite.pos = RoomHelper.MiddleOfTile(x, y);
                sprite.sprite.isVisible = true;
                _spriteIndex++;
            }
        }

        public void ResetPredictionSprites() {
            if (_spriteIndex > 0) {
                foreach (var sprite in _predictedIntersectionSprites) {
                    sprite.sprite.isVisible = false;
                }
                _spriteIndex = 0;
            }
        }

        public void Activate() {
            Active = true;
            if (_ai._currentConnection is null || _ai._currentNode is null) {
                _pathVisualizer.ClearPath();
            } else if (_room.GetCWT().DynamicGraphs.TryGetValue(new SlugcatDescriptor(_ai._slugcat), out var graph)) {
                _pathVisualizer.DisplayPath(
                    _ai._currentNode.GridPos,
                    _ai._currentConnection.Value,
                    graph
                );
            }
            _jumpBoostLabel.isVisible = true;
            _currentConnectionLabel.isVisible = true;
            _inputDirSprite.sprite.isVisible = true;
            _currentNodeSprite.sprite.isVisible = true;
        }

        public void Deactivate() {
            Active = false;
            _pathVisualizer.ClearPath();
            _jumpBoostLabel.isVisible = false;
            _currentConnectionLabel.isVisible = false;
            _inputDirSprite.sprite.isVisible = false;
            _currentNodeSprite.sprite.isVisible = false;
            ResetPredictionSprites();
        }
    }
}

class EscapeFinder : IDestinationFinder {
    private PathNode? _destinationCandidate;
    public void Reset() {
        _destinationCandidate = null;
    }
    bool IDestinationFinder.StopSearching(PathNode node) {
        if (node.PathCost > 15) {
            _destinationCandidate = node;
            return true;
        }
        if (_destinationCandidate is null || node.Threat < _destinationCandidate.Threat) {
            _destinationCandidate = node;
        }
        return false;
    }

    IVec2? IDestinationFinder.Destination() {
        var pos = _destinationCandidate?.GridPos;
        _destinationCandidate = null;
        return pos;
    }
}

static class ThreatTrackerExtension {

    public static float ThreatAlongPath(this ThreatTracker self, IVec2 startPos, PathConnection startConnection, int maxLookahead) {
        var pos = startPos;
        PathConnection? cursor = startConnection;
        float totalThreat = 0;
        for (int i = 0; i < maxLookahead; i++) {
            if (cursor is null) {
                break;
            }
            totalThreat += self.ThreatAtTile(pos);
            pos = cursor.Value.Next.GridPos;
            cursor = cursor.Value.Next.Connection;
        }
        return totalThreat;
    }

    public static float ThreatAtTile(this ThreatTracker self, IVec2 pos) {
        float totalThreat = 0;
        foreach (var threat in self.threatPoints) {
            if (self.aiMap.TileAccessibleToCreature(pos, threat.crit)) {
                var threatPos = threat.pos.Tile;
                float distance = pos.FloatDist(threatPos);
                bool visualContact = self.AI.creature.Room.realizedRoom.VisualContact(pos, threatPos);
                float visibilityFactor = visualContact && distance <= threat.crit.visualRadius ? 4 : 1;
                float flightFactor = threat.crit.canFly ? 2 : 1;
                float danger = 5 * threat.severity * visibilityFactor * flightFactor;
                totalThreat += danger / (1 + distance * distance / danger);
            }
        }
        return totalThreat;
    }
}

static class AIHooks {
    public static void RegisterHooks() {
        IL.Player.checkInput += IL_Player_checkInput;
    }

    public static void UnregisterHooks() {
        IL.Player.checkInput -= IL_Player_checkInput;
    }

    private static void IL_Player_checkInput(ILContext il) {
        try {
            ILCursor cursor = new(il);
            ILLabel? elseBody = null;
            // find condition
            cursor.GotoNext(
                i => i.MatchLdarg(0),
                i => i.MatchCall(nameof(Player), "get_AI"),
                i => i.MatchBrfalse(out elseBody));
            cursor.Index += 2;
            ILLabel oldBranch = cursor.DefineLabel();
            cursor.MarkLabel(oldBranch);
            ILLabel? consoleDebugCondition = null;
            cursor.GotoNext(i => i.MatchBr(out consoleDebugCondition));
            cursor.GotoLabel(elseBody, MoveType.Before);
            // new condition
            ILLabel condition = cursor.DefineLabel();
            cursor.MarkLabel(condition);
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate((Player self) => self.abstractCreature?.abstractAI?.RealAI is JumpSlugAI);
            cursor.Emit(OpCodes.Brfalse, elseBody);
            // body
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate((Player self) => (self.abstractCreature.abstractAI.RealAI as JumpSlugAI)!.Update());
            cursor.Emit(OpCodes.Br_S, consoleDebugCondition);
            // replace branch instruction in previous else if
            cursor.GotoLabel(oldBranch);
            cursor.Remove();
            cursor.Emit(OpCodes.Brfalse, condition);
        } catch (Exception e) {
            Plugin.Logger!.LogError(e);
        }
    }
}