namespace AIMod;

class JumpSlugAbstractAI : AbstractCreatureAI
{
    public JumpSlugAbstractAI(AbstractCreature abstractCreature, World world) : base(world, abstractCreature)
    {

    }
}

class JumpSlugAI : ArtificialIntelligence
{
    public JumpSlugAI(AbstractCreature abstractCreature, World world) : base(abstractCreature, world)
    {
    }
}