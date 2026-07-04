using TowerDefense.Data;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public static class TowerVisualFactory
    {
        public static GameObject CreateTowerVisual(TowerDefinition definition, string objectName)
        {
            var primitiveType = GetPrimitiveType(definition);
            var go = GameObject.CreatePrimitive(primitiveType);
            go.name = objectName;
            ApplyScale(go.transform, definition);
            return go;
        }

        public static void ApplyScale(Transform target, TowerDefinition definition)
        {
            if (target == null)
            {
                return;
            }

            if (definition == null)
            {
                target.localScale = new Vector3(0.8f, 0.7f, 0.8f);
                return;
            }

            if (definition.projectilePattern == ProjectilePattern.ArcSplash)
            {
                target.localScale = new Vector3(1.05f, 0.58f, 1.05f);
                return;
            }

            switch (definition.role)
            {
                case TowerRole.ArcherLine:
                    target.localScale = new Vector3(0.72f, 0.98f, 0.72f);
                    break;
                case TowerRole.ArtilleryLine:
                    target.localScale = new Vector3(0.95f, 0.54f, 0.95f);
                    break;
                case TowerRole.ControlLine:
                    target.localScale = new Vector3(0.78f, 1.18f, 0.78f);
                    break;
                default:
                    target.localScale = new Vector3(0.8f, 0.7f, 0.8f);
                    break;
            }
        }

        private static PrimitiveType GetPrimitiveType(TowerDefinition definition)
        {
            if (definition == null)
            {
                return PrimitiveType.Cylinder;
            }

            if (definition.projectilePattern == ProjectilePattern.ArcSplash)
            {
                return PrimitiveType.Cube;
            }

            switch (definition.role)
            {
                case TowerRole.ArcherLine:
                    return PrimitiveType.Cylinder;
                case TowerRole.ArtilleryLine:
                    return PrimitiveType.Sphere;
                case TowerRole.ControlLine:
                    return PrimitiveType.Capsule;
                default:
                    return PrimitiveType.Cylinder;
            }
        }
    }
}
