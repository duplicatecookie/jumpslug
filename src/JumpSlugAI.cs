using System;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RWCustom;
using UnityEngine;
using UnityEngine.Windows.WebCam;

namespace JumpSlug;

class JumpSlugAbstractAI : AbstractCreatureAI
{
    public JumpSlugAbstractAI(AbstractCreature abstractCreature, World world) : base(world, abstractCreature)
    {
    }
}

class JumpSlugAI : ArtificialIntelligence
{
    private bool justPressedLeft;
    private bool justPressedN;
    private bool justPressedC;
    IntVector2? destination;
    Pathfinder pathfinder;
    Pathfinder.Visualizer visualizer;
    Pathfinder.Path? path;
    Player? Player => creature.realizedCreature as Player;
    private DebugSprite currentNodeSprite;
    private DebugSprite cursorSprite;
    public JumpSlugAI(AbstractCreature abstractCreature, World world) : base(abstractCreature, world)
    {
        pathfinder = new Pathfinder(Player!);
        visualizer = new Pathfinder.Visualizer(pathfinder);
        cursorSprite = new DebugSprite(
            Vector2.zero,
            new FSprite("pixel")
            {
                scale = 10f,
                color = Color.red,
                isVisible = false,
            },
            abstractCreature.Room.realizedRoom);
        currentNodeSprite = new DebugSprite(
            Vector2.zero,
            new FSprite("pixel")
            {
                scale = 10f,
                color = Color.white,
                isVisible = false,
            },
            abstractCreature.Room.realizedRoom);
    }

    public override void NewRoom(Room room)
    {
        base.NewRoom(room);
        pathfinder.NewRoom();
    }

    public override void Update()
    {
        base.Update();
        pathfinder.Update();
        var mousePos = (Vector2)Input.mousePosition + Player!.room.game.cameras[0].pos;
        switch ((Input.GetKey(KeyCode.N), justPressedN))
        {
            case (true, false):
                justPressedN = true;
                visualizer.ToggleNodes();
                break;
            case (false, true):
                justPressedN = false;
                break;
            default:
                break;
        }
        switch ((Input.GetKey(KeyCode.C), justPressedC))
        {
            case (true, false):
                justPressedC = true;
                visualizer.ToggleConnections();
                break;
            case (false, true):
                justPressedC = false;
                break;
            default:
                break;
        }
        switch ((Input.GetMouseButton(0), justPressedLeft))
        {
            case (true, false):
                justPressedLeft = true;
                IntVector2? start = pathfinder.CurrentNode()?.gridPos;
                destination = Player!.room.GetTilePosition(mousePos);
                path = start is null || destination is null ? null : pathfinder.FindPath(start.Value, destination.Value);
                if (visualizer.visualizingPath)
                {
                    visualizer.TogglePath(path?.start);
                    if (path is not null)
                    {
                        visualizer.TogglePath(path?.start);
                    }
                }
                else if (path is not null)
                {
                    visualizer.TogglePath(path?.start);
                }
                break;
            case (false, true):
                justPressedLeft = false;
                break;
            default:
                break;
        }
        if (path is not null)
        {
            FollowPath();
        }
        if (Player.slatedForDeletetion)
        {
            currentNodeSprite.slatedForDeletetion = true;
            cursorSprite.slatedForDeletetion = true;
        }
    }

    private void FollowPath()
    {
        if (path is null)
        {
            return;
        }
        Player.InputPackage input = default;
        IntVector2? currentNodePos = pathfinder.CurrentNode()?.gridPos;
        if (Player!.bodyMode == Player.BodyModeIndex.Crawl)
        {
            var pos = Player.room.GetTilePosition(Player.bodyChunks[1].pos);
            if (Player.room.GetTile(pos.x, pos.y + 1).Terrain == Room.Tile.TerrainType.Air)
            {
                input.y = 1;
            }
        }
        if (currentNodePos is null)
        {
            // can't move on non-existant node, wait instead
            currentNodeSprite.sprite.isVisible = false;
            return;
        }
        else if (currentNodePos == path.cursor.gridPos)
        {
            if (path.cursor.connection is null)
            {
                path = null;
            }
            else
            {
                switch (path.cursor.connection.Value.type)
                {
                    case Pathfinder.ConnectionType.Walk(int direction):
                        input.x = direction;
                        break;
                }
            }
        }
        else
        {
            for (var cursor = path.cursor; cursor.connection is not null; cursor = cursor.connection.Value.next)
            {
                if (currentNodePos == cursor.gridPos)
                {
                    path.cursor = cursor;
                    return;
                }
            }
            path = destination is null ? null : pathfinder.FindPath(currentNodePos.Value, destination.Value);
        }
        currentNodeSprite.sprite.isVisible = true;
        currentNodeSprite.pos = Player.room.MiddleOfTile(currentNodePos.Value);
        if (path is not null)
        {
            cursorSprite.sprite.isVisible = true;
            cursorSprite.pos = Player.room.MiddleOfTile(path.cursor.gridPos);
        }
        else
        {
            cursorSprite.sprite.isVisible = false;
        }
        Player.input[0] = input;
    }
}

static class AIHooks
{
    public static void RegisterHooks()
    {
        On.Player.Update += Player_Update;
        On.Player.checkInput += Player_checkInput;
        IL.Player.checkInput += IL_Player_checkInput;
    }

    public static void UnregisterHooks()
    {
        On.Player.Update -= Player_Update;
        On.Player.checkInput -= Player_checkInput;
        //IL.Player.checkInput -= IL_Player_checkInput;
    }

    private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
    {
        orig(self, eu);
        if (self.abstractCreature?.abstractAI?.RealAI is JumpSlugAI ai)
        {
            ai.Update();
        }
    }

    private static void Player_checkInput(On.Player.orig_checkInput orig, Player self)
    {
        orig(self);
    }

    private static void IL_Player_checkInput(ILContext il)
    {
        try
        {
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
        }
        catch (Exception e)
        {
            Plugin.Logger!.LogError(e);
            throw;
        }
    }
}