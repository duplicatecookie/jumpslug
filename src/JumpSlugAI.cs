using System.Collections.Generic;
using UnityEngine;

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
    Pathfinder pathfinder;
    Pathfinder.Visualizer visualizer;
    List<Pathfinder.PathNode> path;
    Player player => this.creature.realizedCreature as Player;
    public JumpSlugAI(AbstractCreature abstractCreature, World world) : base(abstractCreature, world)
    {
        pathfinder = new Pathfinder(player);
        visualizer = new Pathfinder.Visualizer(pathfinder);
    }

    public override void NewRoom(Room room)
    {
        base.NewRoom(room);
        pathfinder.NewRoom();
    }

    public override void Update()
    {
        base.Update();
        var mousePos = (Vector2)Input.mousePosition + player.room.game.cameras[0].pos;
        pathfinder.Update();
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
                var start = player.room.ToWorldCoordinate(player.mainBodyChunk.pos);
                var destination = player.room.ToWorldCoordinate(mousePos);
                path = pathfinder.FindPath(start, destination);
                if (visualizer.visualizingPath)
                {
                    visualizer.TogglePath(path);
                    if (path is not null)
                    {
                        visualizer.TogglePath(path);
                    }
                }
                else if (path is not null)
                {
                    visualizer.TogglePath(path);
                }
                break;
            case (false, true):
                justPressedLeft = false;
                break;
            default:
                break;
        }
       
    }
}

static class AIHooks
{
    public static void RegisterHooks()
    {
        On.Player.Update += Player_Update;
    }

    private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
    {
        orig(self, eu);
        if (self.abstractCreature?.abstractAI?.RealAI is JumpSlugAI)
        {
            (self.abstractCreature.abstractAI.RealAI as JumpSlugAI).Update();
        }
    }
}