using System;

using Mono.Cecil.Cil;

using MonoMod.Cil;

using IVec2 = RWCustom.IntVector2;
using TileType = Room.Tile.TerrainType;

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
    private IVec2? _destination;
    private readonly Pathfinder _pathfinder;
    private bool _waitOneTick;
    private (IVec2, PathConnection)? _currentAirConnection;
    private readonly EscapeFinder _escapeFinder;
    private Visualizer? _visualizer;
    private bool _visualizeNode;
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
        _pathfinder = new Pathfinder(
            _room!,
            dynGraph,
            threatTracker,
            Pathfinder.FindDivingLimit(3f, _slugcat.slugcatStats.lungsFac, 0.5f)
        );
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
            // TODO: add restrictions for water and ambush predators (white lizards, dropwigs, pole plants)
            return 1;
        }
    }

    public override void NewRoom(Room room) {
        base.NewRoom(room);
        if (_room.abstractRoom.index != room.abstractRoom.index) {
            _room = room;
            var graphs = _room.GetCWT().DynamicGraphs;
            var descriptor = new SlugcatDescriptor(_slugcat);
            if (!graphs.TryGetValue(descriptor, out var dynGraph)) {
                dynGraph = new DynamicGraph(_room, descriptor.ToJumpVectors());
                graphs.Add(descriptor, dynGraph);
            }
            _pathfinder.NewRoom(room, dynGraph);
            _visualizer?.NewRoom(room);
            _threatVisualizer?.NewRoom(room);
        }
    }

    public override void Update() {
        if (Timers.Active) {
            Timers.JumpSlugAI_Update.Start();
        }
        base.Update();
        if (_room is null) {
            if (Timers.Active) {
                Timers.JumpSlugAI_Update.Stop();
            }
            return;
        }
        if (InputHelper.JustPressedMouseButton(0)) {
            var mousePos = (Vector2)UnityEngine.Input.mousePosition + _room!.game.cameras[0].pos;
            _destination = _room.GetTilePosition(mousePos);
            _visualizer?.UpdatePath();
        }

        var input = GenerateInputs();
        _slugcat.input[0] = default(Player.InputPackage) with {
            x = input.Direction.x,
            y = input.Direction.y,
            jmp = input.Jump,
        };
        UpdateVisualization();
        if (Timers.Active) {
            Timers.JumpSlugAI_Update.Stop();
        }
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
    }

    bool KeepFalling(IVec2 headPos) {
        return false; // TODO: implement
    }

    private Input GenerateInputs() {
        // do this before destination check so _waitOneTick always gets reset
        if (_waitOneTick) {
            _waitOneTick = false;
            return Input.DoNothing;
        }

        if (_destination is null) {
            return Input.DoNothing;
        }

        var headPos = RoomHelper.TilePosition(_slugcat.bodyChunks[0].pos);
        var footPos = RoomHelper.TilePosition(_slugcat.bodyChunks[1].pos);

        if (_destination == headPos || _destination == footPos) {
            _destination = null;
            return Input.DoNothing;
        }

        var orientation = headPos - footPos;
        var sharedGraph = _room.GetCWT().SharedGraph!;
        var headNode = sharedGraph.GetNode(headPos);
        var footNode = sharedGraph.GetNode(footPos);

        // correct positions on slopes
        if (headNode is null) {
            var pos = headPos + Consts.IVec2.Down;
            var node = sharedGraph.GetNode(pos);
            if (node?.Type is NodeType.Slope) {
                headPos = pos;
                headNode = node;
            }
        }

        if (footNode is null) {
            var pos = footPos + Consts.IVec2.Down;
            var node = sharedGraph.GetNode(pos);
            if (node?.Type is NodeType.Slope) {
                footPos = pos;
                footNode = node;
            }
        }

        if (_currentAirConnection is (var startPos, var connection)) {
            switch (connection.Type) {
                case ConnectionType.Jump jump:
                    if (headNode is not null) {
                        if (headPos == startPos) {
                            return new Input(new IVec2(jump.Direction, 0), true);
                        }
                        if (headNode.Beam != GraphNode.BeamType.None) {
                            if (_slugcat.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
                                _currentAirConnection = null;
                                return Input.DoNothing;
                            }
                            
                            // grab onto pole if its closer to the destination than the next tile along the jump trajectory
                            if (!KeepFalling(headPos)) {
                                _currentAirConnection = null;
                                return new Input(Consts.IVec2.Up, false);
                            }
                        }

                        // hold onto wall if in contact
                        if (headNode.Type is NodeType.Wall wall
                            && _slugcat.bodyChunks[0].contactPoint != Consts.IVec2.Zero
                        ) {
                            _currentAirConnection = null;
                            return new Input(new IVec2(wall.Direction, 0), false);
                        }
                    }

                    // check for contact with floor
                    if (footNode is not null) {
                        if (footPos == startPos) {
                            return new Input(new IVec2(jump.Direction, 0), true);
                        }

                        if (footNode.Type is NodeType.Floor or NodeType.Slope
                            && _slugcat.standing
                        ) {
                            _currentAirConnection = null;
                            return Input.DoNothing;
                        }
                    }

                    // keep holding down jump for a few ticks to get full jump height
                    bool doJump = _slugcat.jumpBoost > 0;
                    return new Input(new IVec2(jump.Direction, 0), doJump);
                default:
                    _currentAirConnection = null;
                    return Input.DoNothing;
            }
        }

        var headConnection = _pathfinder.FindPathTo(headPos, _destination.Value);
        var footConnection = _pathfinder.FindPathTo(footPos, _destination.Value);
        _visualizer?.UpdatePath();

        if (headConnection is null) {
            if (footConnection is null) {
                return Input.DoNothing;
            }
            switch (footConnection.Value.Type) {
                case ConnectionType.Walk walk:
                    if (AnySolidAtHeadLevel(footPos, footConnection.Value, 4)) {
                        return new Input(new IVec2(walk.Direction, -1), false);
                    }
                    // get down before crawling down into corridor
                    if (footConnection.Value.PeekType(1) is ConnectionType.Crawl({ x: 0, y: -1 })) {
                        return new Input(Consts.IVec2.Down, false);
                    }
                    return new Input(new IVec2(walk.Direction, 0), false);
                case ConnectionType.Climb climb: // climbing up onto beam tip, standing on horizontal pole
                    // drop down to avoid getting stuck on wall or when entering corridor
                    if (climb.Direction.x != 0 && AnySolidAtHeadLevel(footPos, footConnection.Value, 4)) {
                        return new Input(climb.Direction with { y = -1 }, false);
                    }
                    return new Input(climb.Direction, false);
                case ConnectionType.Crawl crawl: // should only be triggered when crawling out of or into corridors
                    return new Input(crawl.Direction, false);
                case ConnectionType.Jump jump:
                    if (orientation != Consts.IVec2.Up) {
                        _waitOneTick = true;
                        return new Input(Consts.IVec2.Up, false);
                    }

                    // contact with floor
                    if (_slugcat.standing) {
                        _currentAirConnection = (footPos, footConnection.Value);
                        return new Input(new IVec2(jump.Direction, 0), true);
                    }

                    // wait for contact
                    return Input.DoNothing;
                default: // not implementing other stuff for now
                    return Input.DoNothing;
            }
        }

        switch (headConnection.Value.Type) {
            case ConnectionType.Walk walk: // crawling up out of corridor or from pole, crawling horizontally
                // climbing up onto platform
                if (footConnection?.Type is ConnectionType.Climb(IVec2 { x: 0, y: 1 })
                    && footNode?.Type is not NodeType.Corridor
                ) {
                    return new Input(Consts.IVec2.Up, false);
                }

                // don't get up if you should be staying down
                if (orientation is IVec2 { x: not 0, y: 0 }
                    && !AnySolidAtHeadLevel(headPos, headConnection.Value, 4)
                ) {
                    return new Input(new IVec2(walk.Direction, 1), false);
                }

                return new Input(new IVec2(walk.Direction, 0), false);
            case ConnectionType.Climb climb:
                if (climb.Direction == Consts.IVec2.Down) {
                    if (footConnection?.Type is ConnectionType.Jump) {
                        // prevents cycle when climbing down corridor onto corridor and immediately jumping
                        var floorNode = sharedGraph.GetNode(footPos + Consts.IVec2.Down);
                        if (floorNode?.Type is NodeType.Corridor) {
                            _waitOneTick = true;
                            return new Input(Consts.IVec2.Down, true);
                        } 
                    }
                    
                    if (footConnection?.Type is ConnectionType.Walk nextWalk) {
                        if (_slugcat.bodyMode == Player.BodyModeIndex.ClimbingOnBeam) {
                            // let go of pole
                            return new Input(Consts.IVec2.Down, true);
                        }
                        // skip the head connection and start walking
                        return new Input(new IVec2(nextWalk.Direction, 0), false);
                    }

                    // don't grab onto pole right before going into corridor
                    if (footConnection?.Type is ConnectionType.Crawl crawl) {
                        _waitOneTick = true; // gets stuck otherwise;
                        return new Input(crawl.Direction, false);
                    }

                    // make sure no code path past this point tries to let go of the pole,
                    // it will result in a cycle.
                    // if you do need to do that, move this statement somewhere else
                    if (_slugcat.bodyMode != Player.BodyModeIndex.ClimbingOnBeam
                        && footNode?.Type is null or not NodeType.Corridor
                    ) {
                        // grab onto pole
                        return new Input(Consts.IVec2.Up, false);
                    }

                    if (footConnection?.Type is ConnectionType.Climb nextClimb
                        && nextClimb.Direction.x != 0
                    ) {
                        // drop down to horizontal pole level normally to prevent running into wall
                        if (_room.GetTile(headPos + nextClimb.Direction).Terrain is not TileType.Air
                            // moving to the side here is going to lead to the wrong beam
                            || headNode!.Beam == GraphNode.BeamType.Cross
                        ) {
                            return new Input(climb.Direction, false);
                        }

                        if (_slugcat.flipDirection != nextClimb.Direction.x) {
                            // make sure there is a gap between the inputs for changing flip direction
                            // and for moving sideways. it gets stuck otherwise
                            _waitOneTick = true;
                            return new Input(nextClimb.Direction, false);
                        }

                        // switch to horizontal pole while it is at foot level
                        return new Input(nextClimb.Direction, false);
                    }

                    return new Input(climb.Direction, false);
                }

                // make sure no code path past this point tries to let go of the pole,
                // it will result in a cycle.
                // if you do need to do that, move this statement somewhere else
                if (_slugcat.bodyMode != Player.BodyModeIndex.ClimbingOnBeam) {
                    _waitOneTick = true; // prevents getting stuck when moving up out of corridor
                    // grab onto pole
                    return new Input(Consts.IVec2.Up, false);
                }

                if (climb.Direction.x != 0) {
                    if (headNode!.Beam == GraphNode.BeamType.Cross) {
                        // make sure there is a gap between the inputs for changing flip direction
                        // and for moving sideways. it gets stuck otherwise
                        _waitOneTick = true;
                        return new Input(climb.Direction, false);
                    }

                    // get up when hanging from beam
                    if (!AnySolidAtHeadLevel(headPos, headConnection.Value, 4)) {
                        return new Input(climb.Direction with { y = 1 }, false);
                    }
                }

                return new Input(climb.Direction, false);
            case ConnectionType.Crawl crawl:
                // try to get unstuck if movement into corridor isn't working
                if (crawl.Direction == Consts.IVec2.Down
                    && headNode!.Type is NodeType.Floor
                    && _slugcat.bodyChunks[0].vel.magnitude < 0.1f
                ) {
                    _waitOneTick = true;
                    if (_slugcat.input[1].x == 0 && footConnection?.Type is ConnectionType.Walk walk) {
                        return new Input(new IVec2(walk.Direction, -1), false);
                    }
                    return new Input(Consts.IVec2.Down, false);
                }
                // turn around if going backwards
                bool backwards = orientation == crawl.Direction.Inverse();
                bool doJump = backwards && orientation.y != -1;
                return new Input(crawl.Direction, doJump);
            case ConnectionType.Jump jump:
                if (orientation != Consts.IVec2.Up) {
                    _waitOneTick = true;
                    return new Input(Consts.IVec2.Up, false);
                }

                // contact with floor or wall
                if (_slugcat.standing || _slugcat.bodyChunks[0].contactPoint != Consts.IVec2.Zero) {
                    _currentAirConnection = (headPos, headConnection.Value);
                    return new Input(new IVec2(jump.Direction, 0), true);
                }

                // wait for contact
                return Input.DoNothing;
            default: // implement later
                return Input.DoNothing;
        }
    }

    private bool AnySolidAtHeadLevel(IVec2 position, PathConnection connection, int lookAhead) {
        PathConnection? cursor = connection;
        bool anySolidAtHeadLevel = false;
        var sharedGraph = _room.GetCWT().SharedGraph!;
        for (int i = 0; i < lookAhead; i++) {
            if (cursor is not null) {
                var headLevel = cursor.Value.Next.GridPos + Consts.IVec2.Up;
                if (position.y == cursor.Value.Next.GridPos.y
                    && _room.GetTile(headLevel).Terrain is not TileType.Air
                ) {
                    anySolidAtHeadLevel = true;
                    break;
                }
                position = cursor.Value.Next.GridPos;
                if (sharedGraph.GetNode(position)?.Type is NodeType.Corridor) {
                    break;
                }
                cursor = cursor.Value.Next.Connection;
            } else {
                break;
            }
        }
        return anySolidAtHeadLevel;
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
        private readonly DynamicGraphVisualizer _dynGraphVisualizer;
        private readonly DebugSprite _inputDirSprite;
        private readonly DebugSprite[] _currentPosSprites;
        private int _spriteIndex;
        private readonly DebugSprite[] _predictedIntersectionSprites;
        private readonly FLabel[] _currentConnectionLabels;
        public bool Active { get; private set; }
        public Visualizer(JumpSlugAI ai) {
            _ai = ai;
            _room = _ai._room!;
            _pathVisualizer = new PathVisualizer(_room, _ai._pathfinder.DynamicGraph);
            _dynGraphVisualizer = new DynamicGraphVisualizer(_room, _ai._pathfinder.DynamicGraph, new ConnectionType.Jump(1));
            _inputDirSprite = new DebugSprite(Vector2.zero, TriangleMesh.MakeLongMesh(1, false, true), _room);
            _inputDirSprite.sprite.color = Color.red;
            _inputDirSprite.sprite.isVisible = false;
            _currentPosSprites = new DebugSprite[2];
            for (int i = 0; i < 2; i++) {
                _currentPosSprites[i] = new DebugSprite(
                    Vector2.zero,
                    new FSprite("pixel") {
                        scale = 10f,
                        color = Color.blue,
                        isVisible = false
                    },
                    _room
                );
            }
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

            _currentConnectionLabels = new FLabel[2];
            for (int i = 0; i < 2; i++) {
                _currentConnectionLabels[i] = new FLabel(Custom.GetFont(), "None") {
                    alignment = FLabelAlignment.Center,
                    color = Color.white,
                };
            }

            var container = _room!.game.cameras[0].ReturnFContainer("Foreground");
            foreach (var label in _currentConnectionLabels) {
                container.AddChild(label);
            }
            _room.AddObject(_inputDirSprite);
            foreach (var sprite in _currentPosSprites) {
                _room.AddObject(sprite);
            }
            foreach (var sprite in _predictedIntersectionSprites) {
                _room.AddObject(sprite);
            }
        }

        public void NewRoom(Room room) {
            if (room.abstractRoom.index != _room.abstractRoom.index) {
                _room = room;
                _pathVisualizer.NewRoom(room);
                _dynGraphVisualizer.NewRoom(room);
                _dynGraphVisualizer.NewGraph(_ai._pathfinder.DynamicGraph);
                _dynGraphVisualizer.Clear();
                _room.AddObject(_inputDirSprite);
                foreach (var sprite in _currentPosSprites) {
                    _room.AddObject(sprite);
                }
                foreach (var sprite in _predictedIntersectionSprites) {
                    _room.AddObject(sprite);
                }
            }
        }

        public void Update() {
            if (!Active) {
                return;
            }

            var headPos = RoomHelper.TilePosition(_ai._slugcat.bodyChunks[0].pos);
            var footPos = RoomHelper.TilePosition(_ai._slugcat.bodyChunks[1].pos);

            PathConnection? headConnection = null;
            PathConnection? footConnection = null;
            if (_ai._destination is not null) {
                headConnection = _ai._pathfinder.FindPathTo(headPos, _ai._destination.Value);
                footConnection = _ai._pathfinder.FindPathTo(footPos, _ai._destination.Value);
            }

            _currentConnectionLabels[0].text = headConnection is null ? "None" : headConnection.Value.Type.ToString();
            _currentConnectionLabels[1].text = footConnection is null ? "None" : footConnection.Value.Type.ToString();

            var labelPos = _ai._slugcat.bodyChunks[0].pos - _room.game.cameras[0].pos;
            labelPos += new Vector2(30f, 10f);
            _currentConnectionLabels[0].SetPosition(labelPos);
            labelPos.y -= 20f;
            _currentConnectionLabels[1].SetPosition(labelPos);

            _currentPosSprites[0].pos = RoomHelper.MiddleOfTile(headPos);
            _currentPosSprites[1].pos = RoomHelper.MiddleOfTile(footPos);

            if (InputHelper.JustPressed(KeyCode.Alpha1)) {
                _dynGraphVisualizer.SetType(new ConnectionType.Jump(1));
            } else if (InputHelper.JustPressed(KeyCode.Alpha2)) {
                _dynGraphVisualizer.SetType(new ConnectionType.JumpToLedge(1));
            } else if (InputHelper.JustPressed(KeyCode.Alpha3)) {
                _dynGraphVisualizer.SetType(new ConnectionType.JumpUp());
            } else if (InputHelper.JustPressed(KeyCode.Alpha4)) {
                _dynGraphVisualizer.SetType(new ConnectionType.JumpUpToLedge(1));
            } else if (InputHelper.JustPressed(KeyCode.Alpha5)) {
                _dynGraphVisualizer.SetType(new ConnectionType.WalkOffEdge(1));
            } else if (InputHelper.JustPressed(KeyCode.Alpha6)) {
                _dynGraphVisualizer.SetType(new ConnectionType.WalkOffEdgeOntoLedge(1));
            } else if (InputHelper.JustPressed(KeyCode.Alpha7)) {
                _dynGraphVisualizer.SetType(new ConnectionType.Pounce(1));
            } else if (InputHelper.JustPressed(KeyCode.Alpha8)) {
                _dynGraphVisualizer.SetType(new ConnectionType.PounceOntoLedge(1));
            } else if (InputHelper.JustPressed(KeyCode.Alpha9)) {
                _dynGraphVisualizer.SetType(new ConnectionType.Drop());
            } else if (InputHelper.JustPressed(KeyCode.Alpha0)) {
                _dynGraphVisualizer.Clear();
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

        public void UpdatePath() {
            if (!Active || _ai._destination is null) {
                _pathVisualizer.ClearPath();
                return;
            }

            var pos = RoomHelper.TilePosition(_ai._slugcat.bodyChunks[0].pos);
            PathConnection? connection = _ai._pathfinder.FindPathTo(pos, _ai._destination.Value);
            if (connection is null) {
                pos = RoomHelper.TilePosition(_ai._slugcat.bodyChunks[1].pos);
                connection = _ai._pathfinder.FindPathTo(pos, _ai._destination.Value);
            }

            if (connection is not null) {
                _pathVisualizer.DisplayPath(
                    pos,
                    connection.Value
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
            UpdatePath();

            _inputDirSprite.sprite.isVisible = true;
            foreach (var label in _currentConnectionLabels) {
                label.isVisible = true;
            }
            foreach (var sprite in _currentPosSprites) {
                sprite.sprite.isVisible = true;
            }
        }

        public void Deactivate() {
            Active = false;
            _pathVisualizer.ClearPath();
            _dynGraphVisualizer.Clear();
            _inputDirSprite.sprite.isVisible = false;
            foreach (var label in _currentConnectionLabels) {
                label.isVisible = false;
            }
            foreach (var sprite in _currentPosSprites) {
                sprite.sprite.isVisible = false;
            }
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