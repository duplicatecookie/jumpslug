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
    private Path? _path;
    private readonly PathVisualizer _visualizer;
    private readonly DebugSprite _inputDirSprite;
    private readonly DebugSprite _currentNodeSprite;
    private readonly FLabel _currentConnectionLabel;
    private readonly FLabel _bodyModeLabel;
    private readonly FLabel _animationLabel;

    public JumpSlugAI(AbstractCreature abstractCreature, World world) : base(abstractCreature, world) {
        _pathfinder = new Pathfinder(Room!, new SlugcatDescriptor(Player));
        _visualizer = new PathVisualizer(Room!);
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
        _currentConnectionLabel = new FLabel(Custom.GetFont(), "None") {
            alignment = FLabelAlignment.Center,
            color = Color.white,
        };
        _bodyModeLabel = new FLabel(Custom.GetFont(), "None") {
            alignment = FLabelAlignment.Center,
            color = Color.white,
        };
        _animationLabel = new FLabel(Custom.GetFont(), "None") {
            alignment = FLabelAlignment.Center,
            color = Color.white,
        };
        var container = Room!.game.cameras[0].ReturnFContainer("Foreground");
        container.AddChild(_currentConnectionLabel);
        container.AddChild(_bodyModeLabel);
        container.AddChild(_animationLabel);
        Room!.AddObject(_inputDirSprite);
        Room!.AddObject(_currentNodeSprite);
    }

    public override void NewRoom(Room room) {
        base.NewRoom(room);
        _pathfinder.NewRoom(room);
        _visualizer.NewRoom(room);
        room.AddObject(_inputDirSprite);
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

        FollowPath();

        _currentConnectionLabel.text = _path?.CurrentConnection() switch {
            null => "None",
            ConnectionType.Climb(IVec2 dir) => $"Climb({dir})",
            ConnectionType.Crawl(IVec2 dir) => $"Crawl({dir})",
            ConnectionType.Drop => "Drop",
            ConnectionType.Jump(int dir) => $"Jump({dir})",
            ConnectionType.Pounce(int dir) => $"Pounce({dir})",
            ConnectionType.Shortcut => "Shortcut",
            ConnectionType.Walk(int dir) => $"Walk({dir})",
            ConnectionType.WalkOffEdge(int dir) => $"WalkOffEdge({dir})",
            _ => throw new InvalidUnionVariantException(),
        };
        _bodyModeLabel.text = Player.bodyMode.value;
        _animationLabel.text = Player.animation.value;

        var labelPos = Player.bodyChunks[0].pos - Room.game.cameras[0].pos;
        labelPos.y += 20;
        _currentConnectionLabel.SetPosition(labelPos);
        labelPos.y += 20;
        _bodyModeLabel.SetPosition(labelPos);
        labelPos.y += 20;
        _animationLabel.SetPosition(labelPos);

        if (_path?.CurrentNode() is IVec2 current) {
            _currentNodeSprite.sprite.isVisible = true;
            _currentNodeSprite.pos = RoomHelper.MiddleOfTile(current);
        } else {
            _currentNodeSprite.sprite.isVisible = false;
        }

        if (Player.input[0].x == 0 && Player.input[0].y == 0) {
            _inputDirSprite.sprite.isVisible = false;
        } else {
            _inputDirSprite.pos = Player.mainBodyChunk.pos;
            _inputDirSprite.sprite.isVisible = true;
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
        var start = CurrentNode();
        _path = start is null || _destination is null
            ? null
            : _pathfinder.FindPath(
                start.GridPos,
                _destination.Value,
                new SlugcatDescriptor(Player)
            );

        if (_visualizer.VisualizingPath) {
            _visualizer.TogglePath(_path, new SlugcatDescriptor(Player));
            if (_path is not null) {
                _visualizer.TogglePath(_path, new SlugcatDescriptor(Player));
            }
        } else if (_path is not null) {
            _visualizer.TogglePath(_path, new SlugcatDescriptor(Player));
        }
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
        ) {
            return sharedGraph.GetNode(footPos) is GraphNode node
                ? node
                : sharedGraph.GetNode(footPos.x, footPos.y - 1);
        }
        return sharedGraph.GetNode(headPos);
    }

    private void FollowPath() {
        Player.InputPackage input = default;
        if (_path is null || _waitOneTick) {
            Player.input[0] = input;
            _waitOneTick = false;
            return;
        }
        if (Timers.Active) {
            Timers.FollowPath.Start();
        }
        // checked in outher scope
        var sharedGraph = Room!.GetCWT().SharedGraph!;
        IVec2 headPos = RoomHelper.TilePosition(Player.bodyChunks[0].pos);
        IVec2 footPos = RoomHelper.TilePosition(Player.bodyChunks[1].pos);

        bool shouldIgnoreNode = false;
        if (Player.bodyMode == Player.BodyModeIndex.ClimbingOnBeam
            && Player.animation != Player.AnimationIndex.StandOnBeam
            || Player.bodyMode == Player.BodyModeIndex.CorridorClimb
            || Player.bodyMode == Player.BodyModeIndex.WallClimb
        ) {
            var result = _path.FindNode(headPos);
            if (result == Path.NodeSearchResult.NotFound) {
                result = _path.FindNode(footPos);
                if (result == Path.NodeSearchResult.NotFound) {
                    FindPath();
                }
            }
            shouldIgnoreNode = result == Path.NodeSearchResult.ShouldIgnore;
        } else {
            var result = _path.FindEitherNode(footPos, new IVec2(footPos.x, footPos.y - 1));
            if (result == Path.NodeSearchResult.NotFound) {
                FindPath();
            } else if (result == Path.NodeSearchResult.ShouldIgnore) {
                shouldIgnoreNode = true;
            }
        }

        GraphNode? currentNode;
        if (shouldIgnoreNode || _path?.CurrentNode() is null
            || (currentNode = sharedGraph.GetNode(_path.CurrentNode()!.Value)) is null
        ) {
            Player.input[0] = input;
            // can't move on non-existent node, wait instead
            if (Timers.Active) {
                Timers.FollowPath.Stop();
            }
            return;
        }

        var currentPathPos = _path.CurrentNode()!.Value;
        var currentConnection = _path.CurrentConnection();

        if (currentConnection is null) {
            _path = null;
        } else if (currentConnection is ConnectionType.Walk(int direction)) {
            if (Player.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
                if (currentPathPos.y == headPos.y) {
                    input.y = 1;
                } else {
                    input.y = -1;
                    input.jmp = true;
                }
            } else {
                input.x = direction;
                if (currentNode.Type is NodeType.Floor) {
                    var second = _path.PeekNode(2);
                    if (second is not null
                        && sharedGraph.GetNode(second.Value)?.Type
                        is NodeType.Corridor
                    ) {
                        var first = _path.PeekNode(1);
                        if (Player.bodyMode != Player.BodyModeIndex.Crawl
                            && second.Value.y != first!.Value.y + 1
                            && Player.input[1].y != -1
                        ) {
                            input.y = -1;
                        }
                    } else if (
                        Player.bodyMode == Player.BodyModeIndex.Crawl
                        && Player.input[1].y != 1
                    ) {
                        input.y = 1;
                    }
                }
            }
        } else if (currentConnection is ConnectionType.Crawl(IVec2 dir)) {
            input.x = dir.x;
            input.y = dir.y;
            bool backwards = (Player.bodyChunks[0].pos - Player.bodyChunks[1].pos).Dot(dir.ToVector2()) < 0;
            if (_path.PeekConnection(1) is ConnectionType.Crawl(IVec2 nextDir)) {
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
        } else if (currentConnection is ConnectionType.Climb(IVec2 climbDir)) {
            if (Player.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
                if (_path.PeekConnection(1) is ConnectionType.Walk && climbDir.y < 0) {
                    input.y = -1;
                    input.jmp = true;
                } else {
                    input.x = climbDir.x;
                    if (climbDir.x != 0 && Player.flipDirection != climbDir.x) {
                        _waitOneTick = true;
                    }

                    if (Player.animation == Player.AnimationIndex.StandOnBeam
                        && climbDir.y < 0
                        || Player.animation != Player.AnimationIndex.StandOnBeam
                        && currentNode.VerticalBeam == false
                        && Room!
                            .GetTile(currentPathPos.x, currentPathPos.y + 1)
                            .Terrain == Room.Tile.TerrainType.Air
                        && Player.input[1].y != 1
                    ) {
                        input.y = 1;
                    } else {
                        input.y = climbDir.y;
                    }
                }
            } else {
                if (currentNode.VerticalBeam) {
                    input.y = 1;
                } else if (currentNode.HorizontalBeam) {
                    input.x = Player.flipDirection;
                } else {
                    Plugin.Logger!.LogWarning("trying to climb on node without pole");
                }
            }
        } else if (currentConnection is ConnectionType.Drop) {
            if (Mathf.Abs(Player.mainBodyChunk.vel.x) < 0.5f) {
                if (Player.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
                    input.y = -1;
                    input.jmp = true;
                } else if (Player.bodyMode == Player.BodyModeIndex.CorridorClimb) {
                    input.y = -1;
                }
            }
        }
        Player.input[0] = input;
        if (Timers.Active) {
            Timers.FollowPath.Stop();
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