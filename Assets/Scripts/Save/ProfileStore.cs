using System.IO;
using TowerDefense.Data;
using UnityEngine;

namespace TowerDefense.Save
{
    public sealed class ProfileStore
    {
        private readonly string path;

        public ProfileStore(string fileName = "profile.json")
        {
            path = Path.Combine(Application.persistentDataPath, fileName);
        }

        public PlayerProfile LoadOrCreate()
        {
            if (!File.Exists(path))
            {
                return CreateDefaultProfile();
            }

            var json = File.ReadAllText(path);
            var profile = JsonUtility.FromJson<PlayerProfile>(json);
            return profile ?? CreateDefaultProfile();
        }

        public void Save(PlayerProfile profile)
        {
            var json = JsonUtility.ToJson(profile, true);
            File.WriteAllText(path, json);
        }

        public void SaveDevSnapshot(PlayerProfile profile, int slot)
        {
            var json = JsonUtility.ToJson(profile, true);
            File.WriteAllText(GetDevSnapshotPath(slot), json);
        }

        public bool HasDevSnapshot(int slot)
        {
            return File.Exists(GetDevSnapshotPath(slot));
        }

        public bool TryLoadDevSnapshot(int slot, out PlayerProfile profile)
        {
            var snapshotPath = GetDevSnapshotPath(slot);
            if (!File.Exists(snapshotPath))
            {
                profile = null;
                return false;
            }

            var json = File.ReadAllText(snapshotPath);
            profile = JsonUtility.FromJson<PlayerProfile>(json);
            return profile != null;
        }

        private static string GetDevSnapshotPath(int slot)
        {
            return Path.Combine(Application.persistentDataPath, $"dev_profile_slot_{Mathf.Clamp(slot, 1, 3)}.json");
        }

        private static PlayerProfile CreateDefaultProfile()
        {
            var profile = new PlayerProfile();
            profile.unlockedLevelIds.Add("level_01");
            return profile;
        }
    }
}
