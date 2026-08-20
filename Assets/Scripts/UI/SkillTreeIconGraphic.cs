using UnityEngine;
using UnityEngine.UI;

namespace TowerDefense.UI
{
    internal enum SkillTreeIconKind
    {
        Generic,
        Bow,
        Tower,
        Damage,
        Speed,
        Range,
        Capacity,
        Health,
        Cooldown,
        Fire,
        Slow,
        Defense,
        Economy,
        Barracks,
        Wall,
        Splash,
        Pierce,
        Era
    }

    internal sealed class SkillTreeIconGraphic : MaskableGraphic
    {
        public SkillTreeIconKind Kind { get; set; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var half = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.42f;
            switch (Kind)
            {
                case SkillTreeIconKind.Bow: DrawBow(vh, half); break;
                case SkillTreeIconKind.Tower: DrawTower(vh, half); break;
                case SkillTreeIconKind.Damage: DrawSword(vh, half); break;
                case SkillTreeIconKind.Speed: DrawSpeed(vh, half); break;
                case SkillTreeIconKind.Range: DrawRange(vh, half); break;
                case SkillTreeIconKind.Capacity: DrawCapacity(vh, half); break;
                case SkillTreeIconKind.Health: DrawHeart(vh, half); break;
                case SkillTreeIconKind.Cooldown: DrawClock(vh, half); break;
                case SkillTreeIconKind.Fire: DrawFire(vh, half); break;
                case SkillTreeIconKind.Slow: DrawSnowflake(vh, half); break;
                case SkillTreeIconKind.Defense: DrawShield(vh, half); break;
                case SkillTreeIconKind.Economy: DrawCoin(vh, half); break;
                case SkillTreeIconKind.Barracks: DrawBarracks(vh, half); break;
                case SkillTreeIconKind.Wall: DrawWall(vh, half); break;
                case SkillTreeIconKind.Splash: DrawSplash(vh, half); break;
                case SkillTreeIconKind.Pierce: DrawPierce(vh, half); break;
                case SkillTreeIconKind.Era: DrawHourglass(vh, half); break;
                default: DrawDiamond(vh, half); break;
            }
        }

        private void DrawBow(VertexHelper vh, float h)
        {
            AddArc(vh, Vector2.zero, h * 0.88f, -70f, 140f, 10, h * 0.09f);
            AddLine(vh, new Vector2(h * 0.3f, -h * 0.82f), new Vector2(h * 0.3f, h * 0.82f), h * 0.07f);
            AddLine(vh, new Vector2(-h * 0.72f, 0f), new Vector2(h * 0.82f, 0f), h * 0.09f);
            AddTriangle(vh, new Vector2(h, 0f), new Vector2(h * 0.65f, h * 0.2f), new Vector2(h * 0.65f, -h * 0.2f));
        }

        private void DrawTower(VertexHelper vh, float h)
        {
            AddRect(vh, new Rect(-h * 0.58f, -h * 0.72f, h * 1.16f, h * 1.08f));
            AddRect(vh, new Rect(-h * 0.82f, h * 0.28f, h * 0.34f, h * 0.42f));
            AddRect(vh, new Rect(-h * 0.16f, h * 0.28f, h * 0.32f, h * 0.42f));
            AddRect(vh, new Rect(h * 0.48f, h * 0.28f, h * 0.34f, h * 0.42f));
        }

        private void DrawSword(VertexHelper vh, float h)
        {
            AddLine(vh, new Vector2(-h * 0.58f, -h * 0.58f), new Vector2(h * 0.55f, h * 0.55f), h * 0.16f);
            AddTriangle(vh, new Vector2(h * 0.82f, h * 0.82f), new Vector2(h * 0.34f, h * 0.62f), new Vector2(h * 0.62f, h * 0.34f));
            AddLine(vh, new Vector2(-h * 0.68f, -h * 0.28f), new Vector2(-h * 0.28f, -h * 0.68f), h * 0.13f);
        }

        private void DrawSpeed(VertexHelper vh, float h)
        {
            AddChevron(vh, -h * 0.42f, h);
            AddChevron(vh, h * 0.22f, h);
        }

        private void DrawRange(VertexHelper vh, float h)
        {
            AddCircle(vh, Vector2.zero, h * 0.72f, 18, h * 0.1f);
            AddCircle(vh, Vector2.zero, h * 0.3f, 14, h * 0.1f);
            AddLine(vh, new Vector2(-h, 0f), new Vector2(-h * 0.48f, 0f), h * 0.08f);
            AddLine(vh, new Vector2(h * 0.48f, 0f), new Vector2(h, 0f), h * 0.08f);
            AddLine(vh, new Vector2(0f, -h), new Vector2(0f, -h * 0.48f), h * 0.08f);
            AddLine(vh, new Vector2(0f, h * 0.48f), new Vector2(0f, h), h * 0.08f);
        }

        private void DrawCapacity(VertexHelper vh, float h)
        {
            AddCircle(vh, new Vector2(-h * 0.42f, h * 0.25f), h * 0.22f, 12, h * 0.12f);
            AddCircle(vh, new Vector2(h * 0.42f, h * 0.25f), h * 0.22f, 12, h * 0.12f);
            AddCircle(vh, new Vector2(0f, h * 0.48f), h * 0.24f, 12, h * 0.12f);
            AddLine(vh, new Vector2(-h * 0.78f, -h * 0.58f), new Vector2(-h * 0.12f, -h * 0.18f), h * 0.22f);
            AddLine(vh, new Vector2(h * 0.78f, -h * 0.58f), new Vector2(h * 0.12f, -h * 0.18f), h * 0.22f);
        }

        private void DrawHeart(VertexHelper vh, float h)
        {
            AddCircle(vh, new Vector2(-h * 0.32f, h * 0.25f), h * 0.42f, 14, h * 0.28f);
            AddCircle(vh, new Vector2(h * 0.32f, h * 0.25f), h * 0.42f, 14, h * 0.28f);
            AddTriangle(vh, new Vector2(-h * 0.72f, h * 0.18f), new Vector2(h * 0.72f, h * 0.18f), new Vector2(0f, -h * 0.9f));
        }

        private void DrawClock(VertexHelper vh, float h)
        {
            AddCircle(vh, Vector2.zero, h * 0.76f, 20, h * 0.11f);
            AddLine(vh, Vector2.zero, new Vector2(0f, h * 0.48f), h * 0.11f);
            AddLine(vh, Vector2.zero, new Vector2(h * 0.42f, -h * 0.2f), h * 0.11f);
        }

        private void DrawFire(VertexHelper vh, float h)
        {
            AddTriangle(vh, new Vector2(0f, h), new Vector2(-h * 0.72f, -h * 0.72f), new Vector2(h * 0.72f, -h * 0.72f));
            AddTriangle(vh, new Vector2(-h * 0.72f, h * 0.34f), new Vector2(-h * 0.62f, -h * 0.72f), new Vector2(h * 0.35f, -h * 0.62f));
            AddTriangle(vh, new Vector2(h * 0.58f, h * 0.58f), new Vector2(-h * 0.2f, -h * 0.72f), new Vector2(h * 0.72f, -h * 0.72f));
        }

        private void DrawSnowflake(VertexHelper vh, float h)
        {
            for (var i = 0; i < 3; i++)
            {
                var angle = i * 60f * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                AddLine(vh, -direction * h * 0.85f, direction * h * 0.85f, h * 0.1f);
            }
        }

        private void DrawShield(VertexHelper vh, float h)
        {
            AddLine(vh, new Vector2(-h * 0.72f, h * 0.72f), new Vector2(h * 0.72f, h * 0.72f), h * 0.14f);
            AddLine(vh, new Vector2(-h * 0.72f, h * 0.72f), new Vector2(-h * 0.52f, -h * 0.35f), h * 0.14f);
            AddLine(vh, new Vector2(h * 0.72f, h * 0.72f), new Vector2(h * 0.52f, -h * 0.35f), h * 0.14f);
            AddLine(vh, new Vector2(-h * 0.52f, -h * 0.35f), new Vector2(0f, -h * 0.88f), h * 0.14f);
            AddLine(vh, new Vector2(h * 0.52f, -h * 0.35f), new Vector2(0f, -h * 0.88f), h * 0.14f);
        }

        private void DrawCoin(VertexHelper vh, float h)
        {
            AddCircle(vh, Vector2.zero, h * 0.76f, 20, h * 0.18f);
            AddLine(vh, new Vector2(-h * 0.28f, h * 0.38f), new Vector2(h * 0.28f, h * 0.38f), h * 0.12f);
            AddLine(vh, new Vector2(0f, h * 0.62f), new Vector2(0f, -h * 0.62f), h * 0.12f);
            AddLine(vh, new Vector2(-h * 0.28f, -h * 0.38f), new Vector2(h * 0.28f, -h * 0.38f), h * 0.12f);
        }

        private void DrawBarracks(VertexHelper vh, float h)
        {
            AddTriangle(vh, new Vector2(-h * 0.92f, h * 0.15f), new Vector2(0f, h * 0.88f), new Vector2(h * 0.92f, h * 0.15f));
            AddRect(vh, new Rect(-h * 0.72f, -h * 0.75f, h * 1.44f, h * 0.9f));
            AddRect(vh, new Rect(-h * 0.18f, -h * 0.75f, h * 0.36f, h * 0.56f), Color.clear);
        }

        private void DrawWall(VertexHelper vh, float h)
        {
            var brick = new Vector2(h * 0.72f, h * 0.42f);
            for (var row = -1; row <= 1; row++)
            {
                var offset = (row & 1) == 0 ? 0f : brick.x * 0.5f;
                for (var column = -1; column <= 1; column++)
                {
                    AddRect(vh, new Rect(column * brick.x + offset - brick.x * 0.45f, row * brick.y - brick.y * 0.42f, brick.x * 0.88f, brick.y * 0.84f));
                }
            }
        }

        private void DrawSplash(VertexHelper vh, float h)
        {
            AddCircle(vh, Vector2.zero, h * 0.22f, 12, h * 0.18f);
            for (var i = 0; i < 8; i++)
            {
                var angle = i * 45f * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                AddLine(vh, direction * h * 0.42f, direction * h * 0.92f, h * 0.12f);
            }
        }

        private void DrawPierce(VertexHelper vh, float h)
        {
            AddLine(vh, new Vector2(-h, 0f), new Vector2(h * 0.72f, 0f), h * 0.13f);
            AddTriangle(vh, new Vector2(h, 0f), new Vector2(h * 0.58f, h * 0.28f), new Vector2(h * 0.58f, -h * 0.28f));
            AddCircle(vh, new Vector2(-h * 0.35f, 0f), h * 0.28f, 14, h * 0.09f);
            AddCircle(vh, new Vector2(h * 0.25f, 0f), h * 0.28f, 14, h * 0.09f);
        }

        private void DrawHourglass(VertexHelper vh, float h)
        {
            AddLine(vh, new Vector2(-h * 0.7f, h * 0.82f), new Vector2(h * 0.7f, h * 0.82f), h * 0.13f);
            AddLine(vh, new Vector2(-h * 0.7f, -h * 0.82f), new Vector2(h * 0.7f, -h * 0.82f), h * 0.13f);
            AddLine(vh, new Vector2(-h * 0.58f, h * 0.72f), new Vector2(h * 0.52f, -h * 0.72f), h * 0.12f);
            AddLine(vh, new Vector2(h * 0.58f, h * 0.72f), new Vector2(-h * 0.52f, -h * 0.72f), h * 0.12f);
        }

        private void DrawDiamond(VertexHelper vh, float h)
        {
            AddTriangle(vh, new Vector2(0f, h * 0.9f), new Vector2(-h * 0.78f, 0f), new Vector2(h * 0.78f, 0f));
            AddTriangle(vh, new Vector2(0f, -h * 0.9f), new Vector2(-h * 0.78f, 0f), new Vector2(h * 0.78f, 0f));
        }

        private void AddChevron(VertexHelper vh, float x, float h)
        {
            AddLine(vh, new Vector2(x - h * 0.28f, h * 0.72f), new Vector2(x + h * 0.34f, 0f), h * 0.15f);
            AddLine(vh, new Vector2(x + h * 0.34f, 0f), new Vector2(x - h * 0.28f, -h * 0.72f), h * 0.15f);
        }

        private void AddArc(VertexHelper vh, Vector2 center, float radius, float startDegrees, float sweepDegrees, int segments, float thickness)
        {
            var previous = center + Direction(startDegrees) * radius;
            for (var i = 1; i <= segments; i++)
            {
                var next = center + Direction(startDegrees + sweepDegrees * i / segments) * radius;
                AddLine(vh, previous, next, thickness);
                previous = next;
            }
        }

        private static Vector2 Direction(float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        private void AddCircle(VertexHelper vh, Vector2 center, float radius, int segments, float thickness)
        {
            AddArc(vh, center, radius, 0f, 360f, segments, thickness);
        }

        private void AddRect(VertexHelper vh, Rect rect)
        {
            AddRect(vh, rect, color);
        }

        private void AddRect(VertexHelper vh, Rect rect, Color rectangleColor)
        {
            var start = vh.currentVertCount;
            vh.AddVert(new Vector3(rect.xMin, rect.yMin), rectangleColor, Vector2.zero);
            vh.AddVert(new Vector3(rect.xMin, rect.yMax), rectangleColor, Vector2.up);
            vh.AddVert(new Vector3(rect.xMax, rect.yMax), rectangleColor, Vector2.one);
            vh.AddVert(new Vector3(rect.xMax, rect.yMin), rectangleColor, Vector2.right);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float thickness)
        {
            var direction = (b - a).normalized;
            var normal = new Vector2(-direction.y, direction.x) * thickness * 0.5f;
            var start = vh.currentVertCount;
            vh.AddVert(a - normal, color, Vector2.zero);
            vh.AddVert(a + normal, color, Vector2.up);
            vh.AddVert(b + normal, color, Vector2.one);
            vh.AddVert(b - normal, color, Vector2.right);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c)
        {
            var start = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.up);
            vh.AddVert(c, color, Vector2.one);
            vh.AddTriangle(start, start + 1, start + 2);
        }
    }
}
