namespace JumpSlug;

static class TemplateType
{
    public static CreatureTemplate.Type? JumpSlug;

    public static void RegisterValues()
    {
        JumpSlug = new CreatureTemplate.Type("JumpSlug", true);
    }

    public static void UnregisterValues()
    {
        JumpSlug?.Unregister();
        JumpSlug = null;
    }
}

static class TemplateHooks
{
    public static void RegisterHooks()
    {
        On.StaticWorld.InitCustomTemplates += StaticWorld_InitCustomTemplates;
    }

    public static void UnregisterHooks()
    {
        On.StaticWorld.InitCustomTemplates -= StaticWorld_InitCustomTemplates;
    }

    private static void StaticWorld_InitCustomTemplates(On.StaticWorld.orig_InitCustomTemplates orig)
    {
        CreatureTemplate? stdGroundTemplate = null;
        foreach (var template in StaticWorld.creatureTemplates)
        {
            if (template.type == CreatureTemplate.Type.StandardGroundCreature)
            {
                stdGroundTemplate = template;
                break;
            }
        }
        if (stdGroundTemplate is null)
        {
            Plugin.Logger!.LogError("could not find Standard Ground Creature template");
            return;
        }
        var jumpslugTemplate = new CreatureTemplate(
            TemplateType.JumpSlug,
            stdGroundTemplate,
            new(),
            new(),
            new CreatureTemplate.Relationship(CreatureTemplate.Relationship.Type.Ignores, 1f));
        for (int i = 0; i < StaticWorld.creatureTemplates.Length; i++)
        {
            if (StaticWorld.creatureTemplates[i] is null)
            {
                StaticWorld.creatureTemplates[i] = jumpslugTemplate;
                Plugin.Logger!.LogInfo($"inserted JumpSlug creature template at index {i}");
                break;
            }
        }
        orig();
    }
}