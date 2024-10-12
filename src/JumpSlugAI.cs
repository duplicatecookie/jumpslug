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

class JumpSlugAI : ArtificialIntelligence {
    private Player Player => (Player)creature.realizedCreature;
    private Room? Room => creature.Room.realizedRoom;
    private bool _waitOneTick;
    private IVec2? _destination;
    private readonly Pathfinder _pathfinder;
    private GraphNode? _currentNode;
    private PathConnection? _currentConnection;
    private bool _performingAirMovement;
    private int _offPathCounter;
    private const int MAX_TICKS_NOT_FALLING_TOWARDS_PATH = 5;
    private const int MAX_SPRITES = 16;
    private bool _visualize;
    private bool _visualizeNode;
    private readonly PathVisualizer _pathVisualizer;
    private readonly NodeVisualizer _nodeVisualizer;
    private readonly DebugSprite _inputDirSprite;
    private readonly DebugSprite _currentNodeSprite;
    private readonly DebugSprite[] _predictedIntersectionSprites;
    private readonly FLabel _currentConnectionLabel;
    private readonly FLabel _jumpBoostLabel;

    public JumpSlugAI(AbstractCreature abstractCreature, World world) : base(abstractCreature, world) {
        _pathfinder = new Pathfinder(Room!, new SlugcatDescriptor(Player));
        _pathVisualizer = new PathVisualizer(Room!, _pathfinder);
        _nodeVisualizer = new NodeVisualizer(Room!, _pathfinder.DynamicGraph);
        _inputDirSprite = new DebugSprite(Vector2.zero, TriangleMesh.MakeLongMesh(1, false, true), Room);
        _inputDirSprite.sprite.color = Color.red;
        _inputDirSprite.sprite.isVisible = false;
        _currentNodeSprite = new DebugSprite(
            Vector2.zero,
            new FSprite("pixel") {
                scale = 10f,
                color = Color.blue,
                isVisible = false
            },
            Room
        );
        _predictedIntersectionSprites = new DebugSprite[MAX_SPRITES];
        for (int i = 0; i < MAX_SPRITES; i++) {
            _predictedIntersectionSprites[i] = new DebugSprite(
                Vector2.zero,
                new FSprite("pixel") {
                    scale = 10f,
                    color = Color.green,
                    isVisible = false,
                },
                Room
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

        var container = Room!.game.cameras[0].ReturnFContainer("Foreground");
        container.AddChild(_currentConnectionLabel);
        container.AddChild(_jumpBoostLabel);
        Room!.AddObject(_inputDirSprite);
        Room!.AddObject(_currentNodeSprite);
        foreach (var sprite in _predictedIntersectionSprites) {
            Room.AddObject(sprite);
        }
    }

    public override void NewRoom(Room room) {
        base.NewRoom(room);
        _pathfinder.NewRoom(room);
        _pathVisualizer.NewRoom(room);
        _nodeVisualizer.NewRoom(room, _pathfinder.DynamicGraph);
        room.AddObject(_inputDirSprite);
        Room!.AddObject(_currentNodeSprite);
        foreach (var sprite in _predictedIntersectionSprites) {
            Room.AddObject(sprite);
        }
    }

    public override void Update() {
        base.Update();
        if (Room is null) {
            return;
        }
        if (InputHelper.JustPressedMouseButton(0)) {
            var mousePos = (Vector2)Input.mousePosition + Room!.game.cameras[0].pos;
            _destination = Room.GetTilePosition(mousePos);
            FindPath();
        }
        if (InputHelper.JustPressed(KeyCode.P)) {
            _visualize = !_visualize;
            if (!_visualize || _currentConnection is null || _currentNode is null) {
                _pathVisualizer.ClearPath();
            } else {
                _pathVisualizer.DisplayPath(
                    _currentNode.GridPos,
                    _currentConnection.Value,
                    new SlugcatDescriptor(Player)
                );
            }
            _jumpBoostLabel.isVisible = _visualize;
            _currentConnectionLabel.isVisible = _visualize;
            _inputDirSprite.sprite.isVisible = _visualize;
            _currentNodeSprite.sprite.isVisible = _visualize;
            foreach (var sprite in _predictedIntersectionSprites) {
                sprite.sprite.isVisible = false;
            }
        }
        if (InputHelper.JustPressed(KeyCode.M)) {
            _visualizeNode = !_visualizeNode;
        }

        Move();
        if (_visualize) {
            UpdateVisualization();
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

    private void UpdateVisualization() {
        foreach (var sprite in _predictedIntersectionSprites) {
            sprite.sprite.isVisible = false;
        }
        _currentConnectionLabel.text = _currentConnection?.Type switch {
            null => "None",
            ConnectionType.Climb(IVec2 dir) => $"Climb({dir})",
            ConnectionType.Crawl(IVec2 dir) => $"Crawl({dir})",
            ConnectionType.Drop => "Drop",
            ConnectionType.Jump(int dir) => $"Jump({dir})",
            ConnectionType.Pounce(int dir) => $"Pounce({dir})",
            ConnectionType.Shortcut => "Shortcut",
            ConnectionType.Walk(int dir) => $"Walk({dir})",
            ConnectionType.WalkOffEdge(int dir) => $"WalkOffEdge({dir})",
            ConnectionType.SlideOnWall(int dir) => $"SlideOnWall({dir})",
            _ => throw new InvalidUnionVariantException(),
        };

        _jumpBoostLabel.text = Player.jumpBoost.ToString();

        var labelPos = Player.bodyChunks[0].pos - Room!.game.cameras[0].pos;
        labelPos.y += 60;
        _currentConnectionLabel.SetPosition(labelPos);
        labelPos.y += 20;
        _jumpBoostLabel.SetPosition(labelPos);

        if (_currentNode is not null) {
            _currentNodeSprite.sprite.isVisible = true;
            _currentNodeSprite.pos = RoomHelper.MiddleOfTile(_currentNode.GridPos);
        } else {
            _currentNodeSprite.sprite.isVisible = false;
        }

        if (Player.input[0].x == 0 && Player.input[0].y == 0) {
            _inputDirSprite.sprite.isVisible = false;
        } else {
            _inputDirSprite.pos = Player.mainBodyChunk.pos;
            _inputDirSprite.sprite.isVisible = true;
            if (Player.input[0].jmp == true) {
                _inputDirSprite.sprite.color = Color.green;
            } else {
                _inputDirSprite.sprite.color = Color.red;
            }
            LineHelper.ReshapeLine(
                (TriangleMesh)_inputDirSprite.sprite,
                Player.mainBodyChunk.pos,
                new Vector2(
                    Player.mainBodyChunk.pos.x + Player.input[0].x * 50,
                    Player.mainBodyChunk.pos.y + Player.input[0].y * 50
                )
            );
        }
    }

    private void FindPath() {
        if (_currentNode is null || _destination is null) {
            _currentConnection = null;
            return;
        } else {
            _currentConnection = _pathfinder.FindPath(
                _currentNode.GridPos,
                _destination.Value,
                new SlugcatDescriptor(Player)
            );

        }
        if (_visualize) {
            if (_currentConnection is null) {
                _pathVisualizer.ClearPath();
            } else {
                _pathVisualizer.DisplayPath(
                    _currentNode.GridPos,
                    _currentConnection.Value,
                    new SlugcatDescriptor(Player)
                );
            }
        }
    }

    private void FindPathIfNotFallingTowardsPathForTooLong() {
        if (!FallingTowardsPath()) {
            if (++_offPathCounter > MAX_TICKS_NOT_FALLING_TOWARDS_PATH) {
                FindPath();
                _offPathCounter = 0;
            }
        } else if (_offPathCounter > 0) {
            _offPathCounter -= 1;
        }
    }

    private bool FallingTowardsPath() {
        if (_currentConnection is null) {
            return false;
        }
        var currentConnection = _currentConnection.Value;
        var sharedGraph = Player.room.GetCWT().SharedGraph!;
        IVec2 headPos = RoomHelper.TilePosition(Player.bodyChunks[0].pos);
        int x = headPos.x;
        int y = headPos.y;
        if (x < 0 || y < 0 || x >= sharedGraph.Width || y >= sharedGraph.Height) {
            return false;
        }
        Vector2 v0 = Player.mainBodyChunk.vel;
        int spriteIndex = 0;
        if (v0.x == 0) {
            while (y > 0) {
                y--;
                var currentNode = sharedGraph.Nodes[x, y];
                if (currentNode is null) {
                    continue;
                }

                if (spriteIndex < MAX_SPRITES) {
                    var sprite = _predictedIntersectionSprites[spriteIndex];
                    sprite.pos = RoomHelper.MiddleOfTile(x, y);
                    sprite.sprite.isVisible = true;
                    spriteIndex++;
                }

                // TODO: extenally messing with the cursor like this is bad, fix when reworking how nodes are found inside the path 
                if (currentConnection.FindInPath(new IVec2(x, y)) is not null) {
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
                float result = DynamicGraph.Parabola(pathOffset.y, v0, Room!.gravity, t) / 20;
                if (result > y + 1) {
                    y++;
                } else if (result < y) {
                    y--;
                } else {
                    x += direction;
                }

                if (x < 0 || y < 0 || x >= sharedGraph.Width || y >= sharedGraph.Height
                    || Room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Solid
                    || Room.Tiles[x, y].Terrain == Room.Tile.TerrainType.Slope) {
                    break;
                }

                var shiftedNode = sharedGraph.Nodes[x, y];
                if (shiftedNode is null) {
                    continue;
                }

                if (_visualize && spriteIndex < MAX_SPRITES) {
                    var sprite = _predictedIntersectionSprites[spriteIndex];
                    sprite.pos = RoomHelper.MiddleOfTile(x, y);
                    sprite.sprite.isVisible = true;
                    spriteIndex++;
                }

                if (currentConnection.FindInPath(new IVec2(x, y)) is not null) {
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
        var sharedGraph = Player.room.GetCWT().SharedGraph!;
        IVec2 headPos = RoomHelper.TilePosition(Player.bodyChunks[0].pos);
        IVec2 footPos = RoomHelper.TilePosition(Player.bodyChunks[1].pos);
        if (Player.bodyMode == Player.BodyModeIndex.Stand
            || Player.animation == Player.AnimationIndex.StandOnBeam
            || Player.animation == Player.AnimationIndex.BeamTip
        ) {
            return sharedGraph.GetNode(footPos) is GraphNode node
                ? node
                : sharedGraph.GetNode(footPos.x, footPos.y - 1);
        }
        return sharedGraph.GetNode(headPos);
    }

    private void Move() {
        Player.InputPackage input = default;
        if (_waitOneTick || _destination is null) {
            Player.input[0] = input;
            _waitOneTick = false;
            return;
        }

        if (Timers.Active) {
            Timers.FollowPath.Start();
        }

        IVec2 headPos = RoomHelper.TilePosition(Player.bodyChunks[0].pos);
        IVec2 footPos = RoomHelper.TilePosition(Player.bodyChunks[1].pos);

        if (_performingAirMovement) {
            var node = CurrentNode();
            if (node is null || node == _currentNode) {
                if (_currentConnection is null) {
                    _performingAirMovement = false;
                } else {
                    var currentConnection = _currentConnection!.Value;
                    if (currentConnection.Type is ConnectionType.Jump jump) {
                        input.x = jump.Direction;
                        if (Player.jumpBoost > 0) {
                            input.jmp = true;
                        }
                    } else if (currentConnection.Type is ConnectionType.WalkOffEdge edgeWalk) {
                        input.x = edgeWalk.Direction;
                    } else if (currentConnection.Type is ConnectionType.Pounce pounce) {
                        input.x = pounce.Direction;
                    } else if (currentConnection.Type is ConnectionType.Drop
                        && node?.Type is NodeType.Floor
                    ) {
                        input.y = -1;
                    }
                }
                Player.input[0] = input;
                if (Timers.Active) {
                    Timers.FollowPath.Stop();
                }
                return;
            } else {
                var connection = _currentConnection!.Value.FindInPath(node.GridPos);
                if (connection is not null) {
                    _currentNode = node;
                    _currentConnection = connection;
                    _performingAirMovement = false;
                } else if (FallingTowardsPath()) {
                    var currentConnection = _currentConnection.Value;
                    if (currentConnection.Type is ConnectionType.Jump jump) {
                        input.x = jump.Direction;
                        if (Player.jumpBoost > 0) {
                            input.jmp = true;
                        }
                    } else if (currentConnection.Type is ConnectionType.WalkOffEdge edgeWalk) {
                        input.x = edgeWalk.Direction;
                    } else if (currentConnection.Type is ConnectionType.Pounce pounce) {
                        input.x = pounce.Direction;
                    }

                    if (Timers.Active) {
                        Timers.FollowPath.Stop();
                    }
                    return;
                } else {
                    _performingAirMovement = false;
                    _currentNode = node;
                    FindPath();
                }
            }
        } else {
            _currentNode = CurrentNode();
            if (_currentNode is null || _currentNode.GridPos == _destination) {
                _currentConnection = null;
                Player.input[0] = input;
                if (Timers.Active) {
                    Timers.FollowPath.Stop();
                }
                return;
            }
            FindPath();
        }

        if (_currentConnection is null) {
            if (_currentNode.HasBeam && Player.bodyMode != Player.BodyModeIndex.ClimbingOnBeam) {
                input.y = 1;
            }
        } else {
            var currentConnection = _currentConnection.Value;
            if (currentConnection.Type is ConnectionType.Walk(int direction)) {
                if (Player.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
                    if (_currentNode.GridPos.y == headPos.y) {
                        input.y = 1;
                    } else {
                        input.y = -1;
                        input.jmp = true;
                    }
                } else {
                    if (currentConnection.PeekType(1) is ConnectionType.Crawl(IVec2 crawlDir)) {
                        if (crawlDir.y < 0) {
                            input.y = -1;
                            if (Player.animation != Player.AnimationIndex.DownOnFours
                                && Player.bodyMode != Player.BodyModeIndex.Default
                            ) {
                                input.x = direction;
                            }
                        } else {
                            input.x = direction;
                            if (crawlDir.x != 0 && currentConnection.PeekPos(2)?.y == _currentNode.GridPos.y) {
                                input.y = -1;
                            }
                        }
                    } else {
                        input.x = direction;
                        if (currentConnection.PeekType(1) is ConnectionType.Drop
                            || currentConnection.PeekType(2) is ConnectionType.Drop
                        ) {
                            if (Player.bodyMode == Player.BodyModeIndex.Stand) {
                                input.y = -1;
                            }
                        } else if (Player.bodyMode == Player.BodyModeIndex.Crawl) {
                            input.y = 1;
                        }
                    }
                }
            } else if (currentConnection.Type is ConnectionType.Crawl(IVec2 dir)) {
                input.x = dir.x;
                input.y = dir.y;
                bool backwards = (Player.bodyChunks[0].pos - Player.bodyChunks[1].pos).Dot(dir.ToVector2()) < 0;
                if (Player.bodyMode != Player.BodyModeIndex.WallClimb
                    && currentConnection.PeekType(1) is ConnectionType.Crawl(IVec2 nextDir)
                ) {
                    if (backwards) {
                        // turn around if going backwards
                        // should not trigger when in a corner because that can lock it into switching forever when trying to go up an inverse T junction
                        if (dir == nextDir) {
                            input.jmp = true;
                        } else if (dir.Dot(nextDir) == 0) {
                            input.x = nextDir.x;
                            input.y = nextDir.y;
                        }
                    }
                }
            } else if (currentConnection.Type is ConnectionType.Climb(IVec2 climbDir)) {
                if (Player.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
                    if (currentConnection.PeekType(1) is ConnectionType.Walk) {
                        if (climbDir.y < 0) {
                            input.y = -1;
                            input.jmp = true;
                        } else {
                            input.y = 1;
                        }
                    } else {
                        input.x = climbDir.x;
                        if (climbDir.x != 0 && (Player.flipDirection != climbDir.x || Player.animation == Player.AnimationIndex.ClimbOnBeam)) {
                            _waitOneTick = true;
                        } else if (Player.animation == Player.AnimationIndex.StandOnBeam) {
                            if (climbDir.y != 0) {
                                input.y = 1;
                            }
                            if (climbDir.x != 0) {
                                var nextPos = currentConnection.PeekPos(2);
                                if (nextPos is not null) {
                                    var terrain = Room!.GetTile(nextPos.Value.x, nextPos.Value.y + 1).Terrain;
                                    if (terrain == Room.Tile.TerrainType.Solid
                                        || terrain == Room.Tile.TerrainType.Slope
                                    ) {
                                        input.y = -1;
                                    }
                                }
                            }
                        } else if (Player.animation != Player.AnimationIndex.GetUpOnBeam
                            && _currentNode.Beam == GraphNode.BeamType.Horizontal
                            && Room!.GetTile(_currentNode.GridPos.x, _currentNode.GridPos.y + 1).Terrain == Room.Tile.TerrainType.Air
                            && Player.input[1].y != 1
                        ) {
                            var nextPos = currentConnection.PeekPos(2);
                            if (nextPos is not null) {
                                var terrain = Room!.GetTile(nextPos.Value.x, nextPos.Value.y + 1).Terrain;
                                if (terrain != Room.Tile.TerrainType.Solid
                                    && terrain != Room.Tile.TerrainType.Slope
                                ) {
                                    input.y = 1;
                                }
                            }
                        } else {
                            input.y = climbDir.y;
                        }
                    }
                } else if (Player.bodyMode == Player.BodyModeIndex.CorridorClimb) {
                    input.x = climbDir.x;
                    input.y = climbDir.y;
                } else if (Player.bodyMode == Player.BodyModeIndex.Default) {
                    if (currentConnection.PeekType(1) is ConnectionType.Walk(int walkDir) && climbDir.y < 0) {
                        input.x = walkDir;
                    } else {
                        input.x = climbDir.x;
                        input.y = 1;
                    }
                } else {
                    input.x = climbDir.x;
                    if (_currentNode.HasBeam) {
                        input.y = 1;
                    } else {
                        Plugin.Logger!.LogWarning("trying to climb on node without pole");
                    }
                }
            } else if (currentConnection.Type is ConnectionType.Drop) {
                if (Mathf.Abs(Player.mainBodyChunk.vel.x) < 0.5f) {
                    input.y = -1;
                    if (Player.animation == Player.AnimationIndex.HangUnderVerticalBeam) {
                        _waitOneTick = true;
                    }
                    _performingAirMovement = true;
                }
            } else if (currentConnection.Type is ConnectionType.Jump(int jumpDir)) {
                if (Player.bodyMode == Player.BodyModeIndex.Stand) {
                    if (Player.flipDirection == jumpDir) {
                        _performingAirMovement = true;
                        input.jmp = true;
                    }
                    input.x = jumpDir;
                } else if (Player.bodyMode == Player.BodyModeIndex.WallClimb) {
                    input.jmp = true;
                    input.x = jumpDir;
                    _performingAirMovement = true;
                } else if (Player.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
                    if (Player.animation == Player.AnimationIndex.ClimbOnBeam) {
                        if (Player.flipDirection == jumpDir) {
                            input.jmp = true;
                            _performingAirMovement = true;
                        }
                        input.x = jumpDir;
                    } else if (Player.animation == Player.AnimationIndex.HangFromBeam) {
                        input.y = 1;
                        _waitOneTick = true;
                    } else if (Player.animation == Player.AnimationIndex.StandOnBeam) {
                        if (headPos.x == footPos.x
                            && headPos.y == footPos.y + 1
                            && Player.bodyChunks[0].vel.x < 5f
                        ) {
                            input.jmp = true;
                            input.x = jumpDir;
                            _performingAirMovement = true;
                        } else {
                            input.y = 1;
                        }
                    }
                } else if (Player.bodyMode == Player.BodyModeIndex.Default) {
                    if (_currentNode.HasBeam) {
                        input.y = 1;
                    }
                }
            } else if (currentConnection.Type is ConnectionType.WalkOffEdge(int walkDir)) {
                input.x = walkDir;
                if (Player.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
                    input.y = -1;
                    input.jmp = true;
                    _performingAirMovement = true;
                } else if (Player.bodyMode == Player.BodyModeIndex.Stand) {
                    _performingAirMovement = true;
                } else if (Player.bodyMode == Player.BodyModeIndex.Crawl) {
                    input.y = 1;
                }
            } else if (currentConnection.Type is ConnectionType.SlideOnWall(int wallDir)) {
                input.x = wallDir;
            }
            Player.input[0] = input;
            if (Timers.Active) {
                Timers.FollowPath.Stop();
            }
        }
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
            throw;
        }
    }
}