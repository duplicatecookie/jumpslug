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

static class Consts {
    public static class IVec2 {
        public static readonly RWCustom.IntVector2 Left = new(-1, 0);
        public static readonly RWCustom.IntVector2 Right = new(1, 0);
        public static readonly RWCustom.IntVector2 Up = new(0, 1);
        public static readonly RWCustom.IntVector2 Down = new(0, -1);
        public static readonly RWCustom.IntVector2 Zero = new(0, 0);
    }
}

static class RoomHelper {
    public static Vector2 MiddleOfTile(int x, int y) => new Vector2(20 * x + 10, 20 * y + 10);
    public static Vector2 MiddleOfTile(IVec2 pos) => MiddleOfTile(pos.x ,pos.y);
    public static IVec2 TilePosition(float x, float y) => new IVec2((int)Mathf.Floor(x / 20), (int)Mathf.Floor(y / 20));
    public static IVec2 TilePosition(Vector2 pos) => TilePosition(pos.x, pos.y);
}