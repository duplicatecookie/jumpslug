using UnityEngine;
using RWCustom;

namespace JumpSlug;

static class LineHelper
{
    public static TriangleMesh MakeLine(Vector2 start, Vector2 end, Color color)
    {
        var mesh = TriangleMesh.MakeLongMesh(1, false, true);
        var distVec = end - start;
        mesh.MoveVertice(0, new Vector2(0, 0));
        mesh.MoveVertice(1, new Vector2(0, distVec.magnitude));
        mesh.MoveVertice(2, new Vector2(1, 0));
        mesh.MoveVertice(3, new Vector2(1, distVec.magnitude));
        mesh.rotation = Custom.VecToDeg(distVec);
        mesh.color = color;
        return mesh;
    }
}