using System.Collections.Generic;
using System.Linq;
using TowerDefense.Data;
using TowerDefense.Save;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class TowerManager : MonoBehaviour
    {
        private readonly List<TowerActor> towers = new();
        private readonly Dictionary<string, int> perTypeLimitBonuses = new();
        private readonly Dictionary<string, float> perTypeDamageMultipliers = new();
        private EnemyManager enemies;
        private PathRoute route;
        private float towerDamageMultiplier = 1f;
        private const float MinimumPathDistance = 1.45f;

        public IReadOnlyList<TowerActor> Towers => towers;
        public IReadOnlyList<TowerDefinition> AvailableTowers { get; private set; }
        public int TowerCount => towers.Count;

        public void Initialize(EnemyManager enemyManager, PathRoute pathRoute, IReadOnlyList<TowerDefinition> towerDefinitions)
        {
            enemies = enemyManager;
            route = pathRoute;
            SetAvailableTowers(towerDefinitions);
        }

        public void SetAvailableTowers(IReadOnlyList<TowerDefinition> towerDefinitions)
        {
            AvailableTowers = towerDefinitions ?? System.Array.Empty<TowerDefinition>();
        }

        public void SetTowerDamageMultiplier(float multiplier)
        {
            towerDamageMultiplier = Mathf.Max(0.05f, multiplier);
            foreach (var tower in towers)
            {
                tower.SetDamageMultiplier(GetDamageMultiplier(tower.Definition));
            }
        }

        public void SetPerTypeDamageMultiplier(string towerId, float multiplier)
        {
            if (string.IsNullOrEmpty(towerId))
            {
                return;
            }

            perTypeDamageMultipliers[towerId] = Mathf.Max(0.05f, multiplier);
            foreach (var tower in towers)
            {
                if (tower.Definition != null && tower.Definition.id == towerId)
                {
                    tower.SetDamageMultiplier(GetDamageMultiplier(tower.Definition));
                }
            }
        }

        public void ClearPerTypeDamageMultipliers()
        {
            perTypeDamageMultipliers.Clear();
        }

        public void SetPerTypeLimitBonus(string towerId, int bonus)
        {
            if (string.IsNullOrEmpty(towerId))
            {
                return;
            }

            perTypeLimitBonuses[towerId] = Mathf.Max(0, bonus);
        }

        public void ClearPerTypeLimitBonuses()
        {
            perTypeLimitBonuses.Clear();
        }

        public int GetPerTypeLimit(TowerDefinition definition)
        {
            if (definition == null)
            {
                return 0;
            }

            return definition.perTypeLimit + (perTypeLimitBonuses.TryGetValue(definition.id, out var bonus) ? bonus : 0);
        }

        public bool CanPlace(TowerDefinition definition, Vector3 position)
        {
            return string.IsNullOrEmpty(GetPlacementBlockReason(definition, position));
        }

        public string GetPlacementBlockReason(TowerDefinition definition, Vector3 position)
        {
            if (definition == null)
            {
                return "No tower selected.";
            }

            if (OverlapsExistingTower(position))
            {
                return "Too close to another tower";
            }

            if (IsTooCloseToPath(position))
            {
                return "Too close to enemy path";
            }

            if (towers.Count(tower => tower.Definition == definition) >= GetPerTypeLimit(definition))
            {
                return $"{definition.displayName} limit reached";
            }

            return string.Empty;
        }

        public bool TryPlace(TowerDefinition definition, Vector3 position)
        {
            if (!CanPlace(definition, position))
            {
                return false;
            }

            var go = TowerVisualFactory.CreateTowerVisual(definition, $"Tower_{definition.id}");
            go.transform.SetParent(transform);
            go.transform.position = position;
            var tower = go.AddComponent<TowerActor>();
            tower.Initialize(definition, enemies, GetDamageMultiplier(definition));
            towers.Add(tower);
            return true;
        }

        public int CountOf(TowerDefinition definition)
        {
            return towers.Count(tower => tower.Definition == definition);
        }

        public bool RemoveNearest(Vector3 position, float radius = 0.8f)
        {
            TowerActor closest = null;
            var best = radius * radius;
            foreach (var tower in towers)
            {
                var distance = (tower.transform.position - position).sqrMagnitude;
                if (distance <= best)
                {
                    closest = tower;
                    best = distance;
                }
            }

            if (closest == null)
            {
                return false;
            }

            towers.Remove(closest);
            Destroy(closest.gameObject);
            return true;
        }

        public void RemoveAll()
        {
            foreach (var tower in towers)
            {
                if (tower != null)
                {
                    Destroy(tower.gameObject);
                }
            }

            towers.Clear();
        }

        public List<TowerPlacementRecord> CaptureLayout()
        {
            return towers.Select(tower => new TowerPlacementRecord
            {
                towerId = tower.Definition.id,
                x = tower.transform.position.x,
                y = tower.transform.position.y,
                z = tower.transform.position.z
            }).ToList();
        }

        public void LoadLayout(IEnumerable<TowerPlacementRecord> records)
        {
            RemoveAll();
            if (records == null || AvailableTowers == null)
            {
                return;
            }

            foreach (var record in records)
            {
                var definition = AvailableTowers.FirstOrDefault(tower => tower.id == record.towerId);
                if (definition != null)
                {
                    TryPlace(definition, new Vector3(record.x, record.y, record.z));
                }
            }
        }

        public float GetDamageDealt(TowerDefinition definition)
        {
            return towers.Where(tower => tower.Definition == definition).Sum(tower => tower.DamageDealt);
        }

        private float GetDamageMultiplier(TowerDefinition definition)
        {
            if (definition == null)
            {
                return towerDamageMultiplier;
            }

            return towerDamageMultiplier * (perTypeDamageMultipliers.TryGetValue(definition.id, out var multiplier) ? multiplier : 1f);
        }

        private bool IsTooCloseToPath(Vector3 position)
        {
            if (route?.Waypoints == null || route.Waypoints.Count < 2)
            {
                return false;
            }

            for (var i = 1; i < route.Waypoints.Count; i++)
            {
                if (DistancePointToSegment(position, route.Waypoints[i - 1], route.Waypoints[i]) < MinimumPathDistance)
                {
                    return true;
                }
            }

            return false;
        }

        private bool OverlapsExistingTower(Vector3 position)
        {
            foreach (var tower in towers)
            {
                if ((tower.transform.position - position).sqrMagnitude < 1.1f)
                {
                    return true;
                }
            }

            return false;
        }

        private static float DistancePointToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(point, a + ab * t);
        }
    }
}
