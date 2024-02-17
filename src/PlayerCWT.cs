using System.Runtime.CompilerServices;

namespace AIMod;

static class PlayerCWT
{
    public class PlayerExtension
    {
        public Pathfinder pathfinder;
        public JumpTracer jumpTracer;
        public PlayerExtension()
        {
        }
    }
    public static ConditionalWeakTable<Player, PlayerExtension> cwt = new();
    public static PlayerExtension GetCWT(this Player player) => cwt.GetValue(player, _ => new());
}