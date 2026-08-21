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

        public void SaveBalanceBotSnapshot(PlayerProfile profile, string botName, string levelId)
        {
            var json = JsonUtility.ToJson(profile, true);
            File.WriteAllText(GetBalanceBotSnapshotPath(botName, levelId), json);
        }

        public bool HasBalanceBotSnapshot(string botName, string levelId)
        {
            return File.Exists(GetBalanceBotSnapshotPath(botName, levelId));
        }

        public bool TryLoadBalanceBotSnapshot(string botName, string levelId, out PlayerProfile profile)
        {
            var snapshotPath = GetBalanceBotSnapshotPath(botName, levelId);
            if (!File.Exists(snapshotPath))
            {
                profile = null;
                return false;
            }

            profile = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(snapshotPath));
            return profile != null;
        }

        private static string GetDevSnapshotPath(int slot)
        {
            return Path.Combine(Application.persistentDataPath, $"dev_profile_slot_{Mathf.Clamp(slot, 1, 3)}.json");
        }

        private static string GetBalanceBotSnapshotPath(string botName, string levelId)
        {
            return Path.Combine(Application.persistentDataPath, $"balance_bot_{SanitizeFilePart(botName)}_{SanitizeFilePart(levelId)}.json");
        }

        private static string SanitizeFilePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var safe = value.Trim().ToLowerInvariant();
            for (var i = 0; i < invalid.Length; i++)
            {
                safe = safe.Replace(invalid[i], '_');
            }

            return safe.Replace(' ', '_');
        }

        private static PlayerProfile CreateDefaultProfile()
        {
            var profile = new PlayerProfile();
            profile.unlockedLevelIds.Add("level_01");
            return profile;
        }
    }
}
