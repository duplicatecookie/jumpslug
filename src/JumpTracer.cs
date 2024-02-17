using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using RWCustom;
using UnityEngine;

namespace AIMod;

class JumpTracer
{
    // y distance from stating poing to max height
    const float jumpHeight = 1.5f;
    // x distance from starting point where height is max
    const float jumpWidth = 3f;
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
        var start = player.room.GetTilePosition(player.bodyChunks[1].pos);
        int xDirection = player.flipDirection;
        if (start == lastStart && xDirection == lastDirection)
        {
            return;
        }
        lastStart = start;
        lastDirection = xDirection;
        CullUpdate();

        int x_offset = xDirection < 0 ? 0 : 1;
        if (player.room is null)
        {
            return;
        }
        var tiles = player.room.Tiles;
        var currentTile = start;
        float m = jumpHeight / (jumpWidth * jumpWidth);
        float Parabola(float x) => -m * (float)Math.Pow(x - xDirection * jumpWidth - start.x - 0.5, 2) + jumpHeight + start.y;
        while (true)
        {
            if (start.x < 0 || start.y < 0 || start.x >= tiles.GetLength(0) || start.y >= tiles.GetLength(1))
            {
                // ideally, partially out of bounds paths would still be traced instead of aborted
                break;
            }
            AddConnection(currentTile.x, currentTile.y);
            float result = Parabola(currentTile.x + x_offset);
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
                if (currentTile.x + xDirection < 0 || currentTile.x + xDirection >= tiles.GetLength(0))
                {
                    sprite?.Destroy();
                    break;
                }
                if (tiles[currentTile.x + xDirection, currentTile.y].Solid)
                {
                    if (sprite is null || currentTile != player.room.GetTilePosition(sprite.pos))
                    {
                        ReplaceSprite(currentTile.x, currentTile.y);
                    }
                    break;
                }
                currentTile.x += xDirection;
            }
        }
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
            color = Color.white,
            scale = 5f,
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