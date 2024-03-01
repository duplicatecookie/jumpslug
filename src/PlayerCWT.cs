using System.Runtime.CompilerServices;

namespace JumpSlug;

static class PlayerCWT
{
    public class PlayerExtension
    {
        public Pathfinder pathfinder;
        public PlayerExtension()
        {
        }
    }
    public static ConditionalWeakTable<Player, PlayerExtension> cwt = new();
    public static PlayerExtension GetCWT(this Player player) => cwt.GetValue(player, _ => new());
}