using System;
using System.Collections.Generic;
using System.Linq;

namespace AIMod;

partial class Plugin
{
    private void InitCustomTemplates(On.StaticWorld.orig_InitCustomTemplates orig)
    {
        // values copied from StandardGroundCreature
        var typeResistances = new List<TileTypeResistance> {
            new TileTypeResistance(AItile.Accessibility.OffScreen, 1f, PathCost.Legality.Allowed),
            new TileTypeResistance(AItile.Accessibility.Floor, 1f, PathCost.Legality.Allowed),
            new TileTypeResistance(AItile.Accessibility.Corridor, 1f, PathCost.Legality.Allowed),
            new TileTypeResistance(AItile.Accessibility.Climb, 2.5f, PathCost.Legality.Allowed),
        };
        var connectionResistances = new List<TileConnectionResistance> {
            new TileConnectionResistance(MovementConnection.MovementType.Standard, 1f, PathCost.Legality.Allowed),
            new TileConnectionResistance(MovementConnection.MovementType.OpenDiagonal, 3f, PathCost.Legality.Allowed),
            new TileConnectionResistance(MovementConnection.MovementType.ReachOverGap, 3f, PathCost.Legality.Allowed),
            new TileConnectionResistance(MovementConnection.MovementType.ReachUp, 2f, PathCost.Legality.Allowed),
            new TileConnectionResistance(MovementConnection.MovementType.ReachDown, 2f, PathCost.Legality.Allowed),
            new TileConnectionResistance(MovementConnection.MovementType.SemiDiagonalReach, 2f, PathCost.Legality.Allowed),
            new TileConnectionResistance(MovementConnection.MovementType.DropToFloor, 20f, PathCost.Legality.Allowed),
            new TileConnectionResistance(MovementConnection.MovementType.DropToWater, 20f, PathCost.Legality.Allowed),
            new TileConnectionResistance(MovementConnection.MovementType.ShortCut, 1.5f, PathCost.Legality.Allowed),
            new TileConnectionResistance(MovementConnection.MovementType.NPCTransportation, 25f, PathCost.Legality.Allowed),
            new TileConnectionResistance(MovementConnection.MovementType.OffScreenMovement, 1f, PathCost.Legality.Allowed),
            new TileConnectionResistance(MovementConnection.MovementType.BetweenRooms, 10f, PathCost.Legality.Allowed),
            new TileConnectionResistance(MovementConnection.MovementType.Slope, 1.5f, PathCost.Legality.Allowed)
        };
        var defaultRelationship = new CreatureTemplate.Relationship(
            CreatureTemplate.Relationship.Type.Ignores,
            1f
        );
        var template = new CreatureTemplate(
            AITestCreatureTemplateType.AITestType,
            null, typeResistances,
            connectionResistances,
            defaultRelationship
        )
        {
            name = "AI Test Creature",
            doesNotUseDens = true,
            canSwim = true,
        };
        // foreach is immutable
        for (int i = 0; i < StaticWorld.creatureTemplates.Length; i++)
        {
            if (StaticWorld.creatureTemplates[i] == null)
            {
                StaticWorld.creatureTemplates[i] = template;
                Logger.LogDebug($"inserted custom template at index {i}");
                break;
            }
        }
        orig();
    }
}

static class AITestCreatureTemplateType
{
    public static CreatureTemplate.Type AITestType;

    public static void RegisterValues()
    {
        AITestType = new CreatureTemplate.Type("AITestCreature", true);
    }

    public static void UnregisterValues()
    {
        AITestType?.Unregister();
        AITestType = null;
    }
}