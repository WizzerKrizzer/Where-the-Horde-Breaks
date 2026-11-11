using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// Mounds the horde is drawn climbing over (a fallen mammoth's body, user 2026-11-11): up to four
    /// capsules on the ground with a height, handed to the horde's shaders (HordeMounds.cginc).
    /// Drawing only: the simulation still walks the goblins on flat ground.
    /// </summary>
    public static class HordeMounds
    {
        private const int Capacity = 4;
        private static readonly Dictionary<object, (Vector4 Segment, Vector4 Shape)> Mounds = new();
        private static readonly Vector4[] Segments = new Vector4[Capacity];
        private static readonly Vector4[] Shapes = new Vector4[Capacity];

        /// <summary>The mound `owner` holds: from `a` to `b` on the ground, `radius` wide, `height` high.</summary>
        public static void Set(object owner, Vector3 a, Vector3 b, float radius, float height)
        {
            Mounds[owner] = (new Vector4(a.x, a.z, b.x, b.z), new Vector4(radius, Mathf.Max(0f, height), 0f, 0f));
            Upload();
        }

        public static void Clear(object owner)
        {
            if (Mounds.Remove(owner))
            {
                Upload();
            }
        }

        private static void Upload()
        {
            var count = 0;
            foreach (var mound in Mounds.Values)
            {
                if (count == Capacity)
                {
                    break;
                }

                Segments[count] = mound.Segment;
                Shapes[count] = mound.Shape;
                count++;
            }

            for (var i = count; i < Capacity; i++)
            {
                Segments[i] = Vector4.zero;
                Shapes[i] = Vector4.zero;
            }

            Shader.SetGlobalVectorArray("_HordeMoundSegments", Segments);
            Shader.SetGlobalVectorArray("_HordeMoundShapes", Shapes);
            Shader.SetGlobalFloat("_HordeMoundCount", count);
        }
    }
}
