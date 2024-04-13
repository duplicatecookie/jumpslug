using Unity.Rendering.HybridV2;

using IVec2 = RWCustom.IntVector2;

namespace JumpSlug;

static class IVec2Extension {
    public static int Dot(this IVec2 self, IVec2 other) {
        return self.x * other.x + self.y * other.y;
    }
}