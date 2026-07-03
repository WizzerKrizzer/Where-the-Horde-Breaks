using UnityEngine;

namespace TowerDefense.Data
{
    [CreateAssetMenu(menuName = "Tower Defense/Skill Tree")]
    public sealed class SkillTreeDefinition : ScriptableObject
    {
        public string id = "core_tree";
        public SkillNodeDefinition[] nodes;
    }

    [System.Serializable]
    public sealed class SkillNodeDefinition
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        public Vector2 radialPosition;
        public string[] prerequisiteNodeIds;
        public CurrencyAmount[] costs;
        public UpgradeEffect[] effects;
        public bool isMajorUnlock;
    }
}
