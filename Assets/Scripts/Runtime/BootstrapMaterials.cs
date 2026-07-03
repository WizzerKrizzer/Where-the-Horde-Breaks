using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public static class BootstrapMaterials
    {
        private static readonly Dictionary<Color, Material> Materials = new();

        public static Material Get(Color color)
        {
            if (Materials.TryGetValue(color, out var material))
            {
                return material;
            }

            material = new Material(Shader.Find("Standard"))
            {
                color = color
            };
            if (color.a < 0.99f)
            {
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = 3000;
            }

            Materials[color] = material;
            return material;
        }
    }
}
