using System;

using Mono.Cecil.Cil;

using MonoMod.Cil;

using IVec2 = RWCustom.IntVector2;

using UnityEngine;

namespace JumpSlug;

class JumpSlugAbstractAI : AbstractCreatureAI {
    public JumpSlugAbstractAI(AbstractCreature abstractCreature, World world) : base(world, abstractCreature) { }
}

class JumpSlugAI : ArtificialIntelligence {
    private readonly DebugSprite inputDirSprite;
    private bool waitOneTick;
    private IVec2? destination;
    private readonly Pathfinder pathfinder;
    private readonly PathfindingVisualizer visualizer;
    private Pathfinder.Path? path;
    private Player Player => (Player)creature.realizedCreature;
    public JumpSlugAI(AbstractCreature abstractCreature, World world) : base(abstractCreature, world) {
        pathfinder = new Pathfinder(Player);
        visualizer = new PathfindingVisualizer(pathfinder);
        inputDirSprite = new DebugSprite(Vector2.zero, TriangleMesh.MakeLongMesh(1, false, true), Player.room);
        inputDirSprite.sprite.color = Color.red;
        inputDirSprite.sprite.isVisible = false;
        Player.room.AddObject(inputDirSprite);
    }

    public override void NewRoom(Room room) {
        base.NewRoom(room);
        pathfinder.NewRoom();
        Player.room.AddObject(inputDirSprite);
    }

    public override void Update() {
        base.Update();
        pathfinder.Update();
        var mousePos = (Vector2)Input.mousePosition + Player.room.game.cameras[0].pos;
        if (InputHelper.JustPressed(KeyCode.N)) {
            visualizer.ToggleNodes();
        }
        if (InputHelper.JustPressed(KeyCode.C)) {
            visualizer.ToggleConnections();
        }
        if (InputHelper.JustPressedMouseButton(0)) {
            IVec2? start = pathfinder.CurrentNode()?.gridPos;
            destination = Player.room.GetTilePosition(mousePos);
            path = start is null || destination is null ? null : pathfinder.FindPath(start.Value, destination.Value);
            if (visualizer.visualizingPath) {
                visualizer.TogglePath(path?.start);
                if (path is not null) {
                    visualizer.TogglePath(path?.start);
                }
            } else if (path is not null) {
                visualizer.TogglePath(path?.start);
            }
        }
        FollowPath();
        if (Player.input[0].x == 0 && Player.input[0].y == 0) {
            inputDirSprite.sprite.isVisible = false;
        } else {
            inputDirSprite.sprite.isVisible = true;
            inputDirSprite.pos = Player.mainBodyChunk.pos;
            LineHelper.ReshapeLine(
                (TriangleMesh)inputDirSprite.sprite,
                Player.mainBodyChunk.pos,
                new Vector2(
                    Player.mainBodyChunk.pos.x + Player.input[0].x * 20,
                    Player.mainBodyChunk.pos.y + Player.input[0].y * 20
                )
            );
        }
    }

    private void FollowPath() {
        Player.InputPackage input = default;
        if (path is null || waitOneTick) {
            Player.input[0] = input;
            waitOneTick = false;
            return;
        }
        if (Timers.active) {
            Timers.followPath.Start();
        }
        var currentNode = pathfinder.CurrentNode();
        if (currentNode is null) {
            Player.input[0] = input;
            // can't move on non-existent node, wait instead
            if (Timers.active) {
                Timers.followPath.Stop();
            }
            return;
        } else if (currentNode.gridPos == path.cursor.gridPos) {
            if (path.cursor.connection is null) {
                path = null;
            } else if (path.cursor.connection.Value.type
                is Pathfinder.ConnectionType.Walk(int direction)
            ) {
                input.x = direction;
                if (pathfinder.CurrentNode()?.type is Pathfinder.NodeType.Floor) {
                    var first = path.cursor.connection?.next;
                    var second = first?.connection?.next;
                    if (second?.GetGraphNode(pathfinder)?.type is Pathfinder.NodeType.Corridor
                    ) {
                        if (Player.bodyMode != Player.BodyModeIndex.Crawl
                            && first!.gridPos.y == second.gridPos.y
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
            } else if (path.cursor.connection.Value.type
                is Pathfinder.ConnectionType.Crawl(IVec2 dir)
            ) {
                input.x = dir.x;
                input.y = dir.y;
                if (path.cursor.connection.Value.next.connection?.type
                    is Pathfinder.ConnectionType.Crawl(IVec2 nextDir)
                ) {
                    if ((Player.mainBodyChunk.pos - Player.bodyChunks[1].pos).Dot(dir.ToVector2()) < 0) {
                        // turn around if going backwards
                        // should not trigger when in a corner because that can lock it into switching forever when trying to go up an inverse T junction
                        if (dir == nextDir) {
                            input.jmp = true;
                            // prevents getting stuck when moving backwards into a corner
                        } else if (dir.Dot(nextDir) == 0) {
                            input.x = nextDir.x;
                            input.y = nextDir.y;
                        }
                    }
                }
            } else if (path.cursor.connection.Value.type
                is Pathfinder.ConnectionType.Climb(IVec2 climbDir)
            ) {
                if (Player.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
                    if (climbDir.x != 0) {
                        input.x = climbDir.x;
                        // this is required for moving from vertical to horizontal poles
                        if (Player.flipDirection != climbDir.x) {
                            waitOneTick = true;
                        }
                    }
                    if (Player.animation != Player.AnimationIndex.StandOnBeam
                        && path.cursor.GetGraphNode(pathfinder)?.verticalBeam == false
                        && Player.room
                            .GetTile(path.cursor.gridPos.x, path.cursor.gridPos.y + 1)
                            .Terrain == Room.Tile.TerrainType.Air
                        && Player.input[1].y != 1
                    ) {
                        input.y = 1;
                    } else {
                        input.y = climbDir.y;
                    }
                } else {
                    if (currentNode.verticalBeam) {
                        input.y = 1;
                    } else if (currentNode.horizontalBeam) {
                        input.x = Player.flipDirection;
                    } else {
                        Plugin.Logger!.LogWarning("trying to climb on node without pole");
                    }
                }
            }
        } else {
            for (var cursor = path.cursor;
                cursor.connection is not null;
                cursor = cursor.connection.Value.next
            ) {
                if (currentNode.gridPos == cursor.gridPos) {
                    path.cursor = cursor;
                    if (Timers.active) {
                        Timers.followPath.Stop();
                    }
                    return;
                }
            }
            path = destination is null ? null : pathfinder.FindPath(currentNode.gridPos, destination.Value);
        }
        Player.input[0] = input;
        if (Timers.active) {
            Timers.followPath.Stop();
        }
    }
}

static class AIHooks {
    public static void RegisterHooks() {
        On.Player.Update += Player_Update;
        On.Player.checkInput += Player_checkInput;
        IL.Player.checkInput += IL_Player_checkInput;
    }

    public static void UnregisterHooks() {
        On.Player.Update -= Player_Update;
        On.Player.checkInput -= Player_checkInput;
    }

    private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu) {
        orig(self, eu);
        if (self.abstractCreature?.abstractAI?.RealAI is JumpSlugAI ai) {
            ai.Update();
        }
    }

    private static void Player_checkInput(On.Player.orig_checkInput orig, Player self) {
        orig(self);
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