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

        private static PlayerProfile CreateDefaultProfile()
        {
            var profile = new PlayerProfile();
            profile.unlockedLevelIds.Add("level_01");
            return profile;
        }
    }
}
