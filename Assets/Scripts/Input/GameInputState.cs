using UnityEngine;

namespace TowerDefense.Input
{
    public struct GameInputState
    {
        public Vector2 Pan;
        public float Zoom;
        public bool FireActive;
        public bool PlaceTower;
        public bool RemoveTower;
        public bool RemoveAllTowers;
        public bool StartLevel;
        public bool RestartLevel;
        public int SelectedTowerIndex;
        public Vector3 PointerWorld;
    }
}
