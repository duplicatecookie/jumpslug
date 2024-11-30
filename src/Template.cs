namespace JumpSlug;

static class TemplateType {
    public static CreatureTemplate.Type? JumpSlug;

    public static void RegisterValues() {
        JumpSlug = new CreatureTemplate.Type("JumpSlug", true);
    }

    public static void UnregisterValues() {
        JumpSlug?.Unregister();
        JumpSlug = null;
    }
}

static class TemplateHooks {
    public static void RegisterHooks() {
        On.AbstractCreature.ctor += AbstractCreature_ctor;
        On.AbstractCreature.InitiateAI += AbstractCreature_InitiateAI;
        On.StaticWorld.InitCustomTemplates += StaticWorld_InitCustomTemplates;
        On.StaticWorld.InitStaticWorld += StaticWorld_InitStaticWorld;
    }

    public static void UnregisterHooks() {
        On.AbstractCreature.ctor -= AbstractCreature_ctor;
        On.AbstractCreature.InitiateAI -= AbstractCreature_InitiateAI;
        On.StaticWorld.InitCustomTemplates -= StaticWorld_InitCustomTemplates;
        On.StaticWorld.InitStaticWorld -= StaticWorld_InitStaticWorld;
    }

    private static void StaticWorld_InitCustomTemplates(On.StaticWorld.orig_InitCustomTemplates orig) {
        var jumpslugTemplate = new CreatureTemplate(
            TemplateType.JumpSlug,
            StaticWorld.GetCreatureTemplate(CreatureTemplate.Type.Slugcat),
            new(),
            new(),
            new CreatureTemplate.Relationship(CreatureTemplate.Relationship.Type.Ignores, 0f)
        );
        jumpslugTemplate.visualRadius = 700f;
        jumpslugTemplate.throughSurfaceVision = 0.8f;
        jumpslugTemplate.waterVision = 0.8f;
        Plugin.Logger!.LogInfo($"inserting JumpSlug creature template at index {TemplateType.JumpSlug!.Index}, length: {StaticWorld.creatureTemplates.Length}");
        StaticWorld.creatureTemplates[jumpslugTemplate.type.Index] = jumpslugTemplate;
        orig();
    }

    private static void StaticWorld_InitStaticWorld(On.StaticWorld.orig_InitStaticWorld orig) {
        orig();
        var playerTemplate = StaticWorld.GetCreatureTemplate(CreatureTemplate.Type.Slugcat);
        for (int i = 0; i < StaticWorld.creatureTemplates.Length; i++) {
            var template = StaticWorld.creatureTemplates[i];
            if (i == TemplateType.JumpSlug!.Index) {
                template.relationships = playerTemplate.relationships;
            } else if (template.TopAncestor().type == CreatureTemplate.Type.LizardTemplate) {
                StaticWorld.EstablishRelationship(
                    CreatureTemplate.Type.LizardTemplate,
                    TemplateType.JumpSlug,
                    new CreatureTemplate.Relationship(CreatureTemplate.Relationship.Type.Eats, 0.8f)
                );
            } else {
                template.relationships[TemplateType.JumpSlug!.Index] = template.relationships[playerTemplate.type.Index];
            }
        }
    }

    private static void AbstractCreature_ctor(
        On.AbstractCreature.orig_ctor orig,
        AbstractCreature self,
        World world,
        CreatureTemplate creatureTemplate,
        Creature realizedCreature,
        WorldCoordinate pos,
        EntityID id
    ) {
        orig(self, world, creatureTemplate, realizedCreature, pos, id);
        if (creatureTemplate.type == TemplateType.JumpSlug) {
            self.state = new PlayerState(self, 0, SlugcatStats.Name.White, false) {
                forceFullGrown = true
            };
            self.abstractAI = new JumpSlugAbstractAI(self, world);
        }
    }

    private static void AbstractCreature_InitiateAI(On.AbstractCreature.orig_InitiateAI orig, AbstractCreature self) {
        orig(self);
        if (self.creatureTemplate.type == TemplateType.JumpSlug) {
            self.abstractAI.RealAI = new JumpSlugAI(self, self.world);
        }
    }
}