using UnityEngine;

using IVec2 = RWCustom.IntVector2;

namespace JumpSlug;

static class IVec2Extension {
    public static int Dot(this IVec2 self, IVec2 other) {
        return self.x * other.x + self.y * other.y;
    }

    public static Vector2 ToVector2(this IVec2 self) {
        return new Vector2(self.x, self.y);
    }
}

static class Vector2Extension {
    public static float Dot(this Vector2 self, Vector2 other) {
        return self.x * other.x + self.y * other.y;
    }
}