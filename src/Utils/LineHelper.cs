using ArenaBehaviors;

using RWCustom;

using UnityEngine;

namespace JumpSlug;

static class LineHelper {
    public static TriangleMesh MakeLine(Vector2 start, Vector2 end, Color color) {
        var mesh = TriangleMesh.MakeLongMesh(1, false, true);
        ReshapeLine(mesh, start, end);
        mesh.color = color;
        return mesh;
    }

    public static void ReshapeLine(TriangleMesh line, Vector2 start, Vector2 end) {
        var distVec = end - start;
        line.MoveVertice(0, new Vector2(0, 0));
        line.MoveVertice(1, new Vector2(0, distVec.magnitude));
        line.MoveVertice(2, new Vector2(1, 0));
        line.MoveVertice(3, new Vector2(1, distVec.magnitude));
        line.rotation = Custom.VecToDeg(distVec);
    }
}