using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefense.Runtime
{
    public static class BootstrapMaterials
    {
        private static readonly Dictionary<Color, Material> Materials = new();
        private static readonly Dictionary<Color, Material> UnlitMaterials = new();
        private static readonly string[] OpaqueShaderNames =
        {
            "Standard",
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Unlit",
            "Legacy Shaders/Diffuse",
            "Unlit/Color"
        };

        private static readonly string[] TransparentShaderNames =
        {
            "Standard",
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Unlit",
            "Unlit/Transparent",
            "Sprites/Default"
        };

        private static readonly string[] UnlitShaderNames =
        {
            "Unlit/Color",
            "Universal Render Pipeline/Unlit",
            "Sprites/Default",
            "Hidden/Internal-Colored",
            "Standard",
            "Legacy Shaders/Diffuse"
        };

        private static Shader opaqueShader;
        private static Shader transparentShader;
        private static Shader unlitShader;
        private static Material builtinFallback;
        private static bool loggedMissingShader;

        public static Material Get(Color color)
        {
            if (Materials.TryGetValue(color, out var material))
            {
                return material;
            }

            material = CreateMaterial(color);
            material.enableInstancing = true;
            SetMaterialColor(material, color);

            if (color.a < 0.99f)
            {
                ConfigureTransparent(material);
            }

            Materials[color] = material;
            return material;
        }

        public static Material GetUnlit(Color color)
        {
            if (UnlitMaterials.TryGetValue(color, out var material))
            {
                return material;
            }

            material = CreateUnlitMaterial(color);
            material.enableInstancing = true;
            SetMaterialColor(material, color);
            if (color.a < 0.99f)
            {
                ConfigureTransparent(material);
            }

            UnlitMaterials[color] = material;
            return material;
        }

        private static Material CreateMaterial(Color color)
        {
            var shader = ResolveShader(color.a < 0.99f);
            if (shader != null)
            {
                return new Material(shader);
            }

            builtinFallback ??= Resources.GetBuiltinResource<Material>("Default-Material.mat");
            if (builtinFallback != null)
            {
                return new Material(builtinFallback);
            }

            if (!loggedMissingShader)
            {
                loggedMissingShader = true;
                Debug.LogError("No runtime material shader was available. Check Graphics Settings > Always Included Shaders.");
            }

            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        private static Material CreateUnlitMaterial(Color color)
        {
            var shader = ResolveUnlitShader();
            if (shader != null)
            {
                return new Material(shader);
            }

            return CreateMaterial(color);
        }

        private static Shader ResolveShader(bool transparent)
        {
            if (transparent && transparentShader != null)
            {
                return transparentShader;
            }

            if (!transparent && opaqueShader != null)
            {
                return opaqueShader;
            }

            var candidates = transparent ? TransparentShaderNames : OpaqueShaderNames;
            foreach (var shaderName in candidates)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    continue;
                }

                if (transparent)
                {
                    transparentShader = shader;
                }
                else
                {
                    opaqueShader = shader;
                }

                return shader;
            }

            return null;
        }

        private static Shader ResolveUnlitShader()
        {
            if (unlitShader != null)
            {
                return unlitShader;
            }

            foreach (var shaderName in UnlitShaderNames)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    continue;
                }

                unlitShader = shader;
                return shader;
            }

            return null;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            material.color = color;
        }

        private static void ConfigureTransparent(Material material)
        {
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
            }

            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
