using System.Collections.Generic;
using RWCustom;
using UnityEngine;

namespace AIMod;

class JumpTracer
{
    private IntVector2 lastStart;
    private int lastDirection;
    private DebugSprite sprite;
    private int cullTimer;
    const int CULL_MAX = 100;
    private List<DebugSprite> connectionSprites;
    private List<DebugSprite> cullSprites;
    public Player player;
    public JumpTracer(Player player)
    {
        this.player = player;
        connectionSprites = new();
        cullSprites = new();
        cullTimer = 0;
    }

    public void Update()
    {
        var start = player.room.GetTilePosition(player.bodyChunks[0].pos);
        if (start == lastStart && player.flipDirection == lastDirection)
        {
            return;
        }
        lastStart = start;
        lastDirection = player.flipDirection;
        int x_offset = (player.flipDirection + 1) / 2;
        if (player.room is null)
        {
            return;
        }
        var tiles = player.room.Tiles;

        var pathOffset = player.room.MiddleOfTile(start);
        float JumpBoost(float boost)
        {
            float t = Mathf.Ceil(boost / 1.5f);
            float result = 0.3f * ((boost - 0.5f) * t - 0.75f * t * t);
            float forResult = 0f;
            for (float x = boost; x > 0; x--)
            {
                forResult += 0.3f * (boost + 1 - 1.5f * x);
            }
            return result;
        }
        Vector2 v0;
        if (player.room.GetTile(start).verticalBeam)
        {
            if (player.isRivulet)
            {
                v0 = new Vector2(9f * player.flipDirection, 9f) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
            }
            else if (player.isSlugpup)
            {
                v0 = new Vector2(5f * player.flipDirection, 7f) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
            }
            else
            {
                v0 = new Vector2(6f * player.flipDirection, 8f) * Mathf.Lerp(1, 1.15f, player.Adrenaline);
            }
        }
        else if (tiles[start.x, start.y - 1].horizontalBeam
            || player.GetCWT().pathfinder.graph?[start.x, start.y - 1]?.type is Pathfinder.NodeType.Floor)
        {
            v0 = new Vector2(
                4.2f * player.flipDirection * player.slugcatStats.runspeedFac * Mathf.Lerp(1, 1.5f, player.Adrenaline),
                (player.isRivulet ? 6f : 4f) * Mathf.Lerp(1, 1.15f, player.Adrenaline) + JumpBoost(player.isSlugpup ? 7 : 8));
        }
        else
        {
            return;
        }
        CullUpdate();
        float Parabola(float t) => v0.y * t - 0.5f * player.gravity * t * t + pathOffset.y;
        Vector2 lastPos = pathOffset;
        for (float t = 0; t < 100; t += 5)
        {
            var nextPos = new Vector2(pathOffset.x + v0.x * t, Parabola(t));
            var sprite = new DebugSprite(lastPos, Visualizer.MakeLine(lastPos, nextPos), player.room);
            connectionSprites.Add(sprite);
            player.room.AddObject(sprite);
            lastPos = nextPos;
        }
        var currentTile = start;
        /*while (true)
        {
            if (start.x < 0 || start.y < 0 || start.x >= tiles.GetLength(0) || start.y >= tiles.GetLength(1))
            {
                // ideally, partially out of bounds paths would still be traced instead of aborted
                break;
            }
            AddConnection(currentTile.x, currentTile.y);
            float result = Parabola(20 * (currentTile.x + x_offset) - pathOffset.x);
            // passes through bottom
            if (result < currentTile.y)
            {
                // bottom room edge
                if (currentTile.y - 1 < 0)
                {
                    sprite?.Destroy();
                    break;
                }
                if (tiles[currentTile.x, currentTile.y - 1].Solid)
                {
                    if (sprite is null || currentTile != player.room.GetTilePosition(sprite.pos))
                    {
                        ReplaceSprite(currentTile.x, currentTile.y);
                    }
                    break;
                }
                currentTile.y--;
            }
            // passes through top
            else if (result > currentTile.y + 1)
            {
                if (currentTile.y + 1 >= tiles.GetLength(1))
                {
                    sprite?.Destroy();
                    break;
                }
                if (tiles[currentTile.x, currentTile.y + 1].Solid)
                {
                    if (sprite is null || currentTile != player.room.GetTilePosition(sprite.pos))
                    {
                        ReplaceSprite(currentTile.x, currentTile.y);
                    }
                    break;
                }
                currentTile.y++;
            }
            // passes through side
            else
            {
                if (currentTile.x + player.flipDirection < 0 || currentTile.x + player.flipDirection >= tiles.GetLength(0))
                {
                    sprite?.Destroy();
                    break;
                }
                if (tiles[currentTile.x + player.flipDirection, currentTile.y].Solid)
                {
                    if (sprite is null || currentTile != player.room.GetTilePosition(sprite.pos))
                    {
                        ReplaceSprite(currentTile.x, currentTile.y);
                    }
                    break;
                }
                currentTile.x += player.flipDirection;
            }
        }*/
    }

    private void ReplaceSprite(int x, int y)
    {
        sprite?.Destroy();
        var pos = player.room.MiddleOfTile(x, y);
        var fs = new FSprite("pixel")
        {
            color = Color.white,
            scale = 20f,
        };
        sprite = new DebugSprite(pos, fs, player.room);
        player.room.AddObject(sprite);
    }

    private void AddConnection(int x, int y)
    {
        var pos = player.room.MiddleOfTile(x, y);
        var fs = new FSprite("pixel")
        {
            alpha = 0.4f,
            color = Color.white,
            scale = 20f,
        };
        var connection = new DebugSprite(pos, fs, player.room);
        connectionSprites.Add(connection);
        player.room.AddObject(connection);
    }

    private void CullUpdate()
    {
        foreach (var connection in connectionSprites)
        {
            connection.sprite.isVisible = false;
            cullSprites.Add(connection);
        }
        connectionSprites.Clear();
        if (cullTimer < CULL_MAX)
        {
            cullTimer++;
        }
        else
        {
            cullTimer = 0;
            foreach (var connection in cullSprites)
            {
                connection.Destroy();
            }
            cullSprites.Clear();
        }
    }
}

static class JumpTracerHooks
{
    public static void RegisterHooks()
    {
        On.Player.ctor += Player_ctor;
        On.Player.Update += Player_Update;
    }
    private static void Player_ctor(On.Player.orig_ctor orig, Player self, AbstractCreature abstractCreature, World world)
    {
        orig(self, abstractCreature, world);
        self.GetCWT().jumpTracer = new JumpTracer(self);
    }
    private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
    {
        orig(self, eu);
        self.GetCWT().jumpTracer.Update();
    }
}