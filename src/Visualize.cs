using UnityEngine;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RWCustom;

namespace AIMod;

class Visualizer
{
    private int cullTimer;
    const int CULL_MAX = 200;
    private PathFinder pathfinder;
    private SimpleSprite destinationSprite;
    private SimpleSprite nextDestinationSprite;
    private List<Connection> currentPath;
    private List<DebugSprite> cullList;
    private class SimpleSprite
    {
        public DebugSprite sprite;
        public IntVector2 tilePos;

        public SimpleSprite(DebugSprite sprite, IntVector2 tilePos)
        {
            this.sprite = sprite;
            this.tilePos = tilePos;
        }
    }
    private class Connection
    {
        public DebugSprite sprite;
        public IntVector2 start;
        public IntVector2 end;

        public Connection(DebugSprite sprite, IntVector2 start, IntVector2 end)
        {
            this.sprite = sprite;
            this.start = start;
            this.end = end;
        }
    }
    public Visualizer(PathFinder pathfinder)
    {
        this.pathfinder = pathfinder;
        currentPath = new();
        cullList = new();
        cullTimer = 0;
    }

    public void Update()
    {
        if (pathfinder.realizedRoom is not null)
        {
            if (cullTimer < CULL_MAX)
            {
                cullTimer++;
            }
            else
            {
                cullTimer = 0;
                foreach (var sprite in cullList)
                {
                    sprite.Destroy();
                }
                cullList.Clear();
            }
            UpdateCurrentPath();
            UpdateDestination();
            UpdateNextDestination();
        }
    }

    public void Reset()
    {
        ResetCurrentPath();
        ResetDestination();
        ResetNextDestination();
    }
    private void ResetDestination()
    {
        destinationSprite?.sprite.Destroy();
    }
    private void ResetNextDestination()
    {
        nextDestinationSprite?.sprite?.Destroy();
    }
    private void ResetCurrentPath()
    {
        if (currentPath is not null)
        {
            foreach (var connection in currentPath)
            {
                connection.sprite.Destroy();
            }
            currentPath.Clear();
        }
    }
    private void UpdateDestination()
    {
        if (destinationSprite?.tilePos == pathfinder.destination.Tile)
        {
            return;
        }
        ResetDestination();
        var sprite = new FSprite("pixel")
        {
            color = Color.red,
            scale = 12f,
        };
        var pos = pathfinder.realizedRoom.MiddleOfTile(new Vector2(pathfinder.destination.x, pathfinder.destination.y) * 20);
        var debugSprite = new DebugSprite(pos, sprite, pathfinder.realizedRoom);
        destinationSprite = new SimpleSprite(debugSprite, pathfinder.destination.Tile);
        pathfinder.realizedRoom.AddObject(debugSprite);
    }
    private void UpdateNextDestination()
    {
        if (pathfinder.nextDestination is null)
        {
            return;
        }
        var nextDest = ((WorldCoordinate)pathfinder.nextDestination).Tile;
        if (nextDestinationSprite?.tilePos == nextDest)
        {
            return;
        }
        ResetNextDestination();
        var sprite = new FSprite("pixel")
        {
            color = Color.magenta,
            scale = 12f,
        };
        var pos = pathfinder.realizedRoom.MiddleOfTile(Custom.IntVector2ToVector2(nextDest) * 20);
        var debugSprite = new DebugSprite(pos, sprite, pathfinder.realizedRoom);
        nextDestinationSprite = new SimpleSprite(debugSprite, nextDest);
        pathfinder.realizedRoom.AddObject(debugSprite);
    }
    private void UpdateCurrentPath()
    {
        var quickPathfinder = new QuickPathFinder(
            pathfinder.creature.pos.Tile,
            pathfinder.destination.Tile,
            pathfinder.realizedRoom.aimap,
            pathfinder.creature.creatureTemplate);
        while (quickPathfinder.status == 0)
        {
            quickPathfinder.Update();
        }
        var quickPath = quickPathfinder.ReturnPath();
        var newPath = FindConnections(quickPath);
        if (newPath is null)
        {
            ResetCurrentPath();
            return;
        }
        foreach (var currentConnection in currentPath)
        {
            foreach (var newConnection in newPath)
            {
                if (newConnection.sprite is not null)
                {
                    continue;
                }
                if (newConnection.start == currentConnection.start && newConnection.end == currentConnection.end)
                {
                    newConnection.sprite = currentConnection.sprite;
                    goto continue_outer;
                }
            }
            currentConnection.sprite.sprite.isVisible = false;
            cullList.Add(currentConnection.sprite);
        continue_outer:
            continue;
        }
        foreach (var connection in newPath)
        {
            if (connection.sprite is null)
            {
                var startVec2 = Custom.IntVector2ToVector2(connection.start * 20);
                var endVec2 = Custom.IntVector2ToVector2(connection.end * 20);
                var mesh = MakeLine(startVec2, endVec2, Color.white);
                var debugSprite = new DebugSprite(pathfinder.realizedRoom.MiddleOfTile(startVec2), mesh, pathfinder.realizedRoom);
                connection.sprite = debugSprite;
                pathfinder.realizedRoom.AddObject(debugSprite);
            }
        }
        currentPath = newPath;
    }

    private List<Connection> FindConnections(QuickPath quickPath)
    {
        if (quickPath is null || quickPath.tiles.Length < 2)
        {
            return null;
        }
        var segmentStart = Custom.IntVector2ToVector2(quickPath.tiles[0]);
        var segmentEnd = Custom.IntVector2ToVector2(quickPath.tiles[1]);
        var lastDirection = (segmentEnd - segmentStart).normalized;
        var newPath = new List<Connection>();
        for (int i = 1; i < quickPath.tiles.Length; i++)
        {
            if (i + 1 >= quickPath.tiles.Length)
            {
                newPath.Add(new Connection(
                    null,
                    IntVector2.FromVector2(segmentStart),
                    IntVector2.FromVector2(segmentEnd)));
                break;
            }
            var tile = quickPath.tiles[i];
            var nextTile = quickPath.tiles[i + 1];
            var direction = Custom.IntVector2ToVector2(nextTile - tile);
            if (direction.normalized == lastDirection)
            {
                segmentEnd += direction;
            }
            else
            {
                newPath.Add(new Connection(
                    null,
                    IntVector2.FromVector2(segmentStart),
                    IntVector2.FromVector2(segmentEnd)));
                segmentStart = segmentEnd;
                segmentEnd += direction;
                lastDirection = direction.normalized;
            }
        }
        return newPath;
    }
    public static TriangleMesh MakeLine(Vector2 start, Vector2 end, Color color)
    {
        var mesh = TriangleMesh.MakeLongMesh(1, false, true);
        var distVec = end - start;
        mesh.MoveVertice(0, new Vector2(0, 0));
        mesh.MoveVertice(1, new Vector2(0, distVec.magnitude));
        mesh.MoveVertice(2, new Vector2(1, 0));
        mesh.MoveVertice(3, new Vector2(1, distVec.magnitude));
        mesh.rotation = Custom.VecToDeg(distVec);
        mesh.color = color;
        return mesh;
    }
}

static class PathfinderCWT
{
    public class PathfinderExtension
    {
        public Visualizer visualizer;
        public PathfinderExtension()
        {
        }
    }
    public static ConditionalWeakTable<PathFinder, PathfinderExtension> cwt = new();
    public static PathfinderExtension GetCWT(this PathFinder pathfinder) => cwt.GetValue(pathfinder, _ => new());
}

static class VisualizerHooks
{
    public static void RegisterHooks()
    {
        On.PathFinder.ctor += PathFinder_ctor;
        On.PathFinder.Update += PathFinder_Update;
        On.AbstractCreature.Die += AbstractCreature_Die;
    }
    private static void PathFinder_ctor(On.PathFinder.orig_ctor orig, PathFinder self, ArtificialIntelligence AI, World world, AbstractCreature creature)
    {
        orig(self, AI, world, creature);
        self.GetCWT().visualizer = new Visualizer(self);
    }
    private static void PathFinder_Update(On.PathFinder.orig_Update orig, PathFinder self)
    {
        orig(self);
        self.GetCWT().visualizer?.Update();
    }

    private static void AbstractCreature_Die(On.AbstractCreature.orig_Die orig, AbstractCreature self)
    {
        self.abstractAI?.RealAI?.pathFinder?.GetCWT().visualizer?.Reset();
        orig(self);
    }
}