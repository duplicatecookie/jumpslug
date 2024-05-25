using System.Runtime.CompilerServices;

namespace JumpSlug;

static class PlayerCWT {
    public class PlayerExtension {
        public Pathfinding.DebugPathfinder? DebugPathfinder;
        public PlayerExtension() {
        }
    }
    public static ConditionalWeakTable<Player, PlayerExtension> CWT = new();
    public static PlayerExtension GetCWT(this Player player) => CWT.GetValue(player, _ => new());
}