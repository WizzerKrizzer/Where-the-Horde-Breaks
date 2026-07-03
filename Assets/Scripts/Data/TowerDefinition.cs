using UnityEngine;

namespace TowerDefense.Data
{
    [CreateAssetMenu(menuName = "Tower Defense/Tower Definition")]
    public sealed class TowerDefinition : ScriptableObject
    {
        public string id = "tower";
        public string displayName = "Tower";
        public TowerRole role = TowerRole.ArcherLine;
        public int eraIndex;
        public int perTypeLimit = 6;
        public float range = 5f;
        public float damage = 3f;
        public float fireInterval = 0.65f;
        public float projectileSpeed = 18f;
        public Color color = Color.white;
        public TowerDefinition nextEvolution;
    }
}
