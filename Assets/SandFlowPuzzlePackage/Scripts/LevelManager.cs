using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace SandFlowPuzzle
{
    public static class LevelManager
    {
        private const string PREFS_KEY_CURRENT_LEVEL = "SandFlowPuzzle_CurrentLevel";
        private static List<LevelData> allLevels = new List<LevelData>();
        private static bool loaded;

        public static int CurrentLevelIndex
        {
            // get => PlayerPrefs.GetInt(PREFS_KEY_CURRENT_LEVEL, 0);
            get => 2;
            private set
            {
                PlayerPrefs.SetInt(PREFS_KEY_CURRENT_LEVEL, value);
                PlayerPrefs.Save();
            }
        }

        public static int LevelCount => allLevels.Count;
        public static bool HasNextLevel => CurrentLevelIndex < allLevels.Count - 1;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;

            allLevels.Clear();

            // Load all JSON files from Resources/Levels/
            TextAsset[] assets = Resources.LoadAll<TextAsset>("Levels");
            if (assets != null && assets.Length > 0)
            {
                // Sort by filename (Level_1, Level_2, etc.)
                var sorted = assets.OrderBy(a => ExtractLevelNumber(a.name)).ToArray();
                foreach (var asset in sorted)
                {
                    try
                    {
                        LevelData data = JsonUtility.FromJson<LevelData>(asset.text);
                        if (data != null)
                        {
                            ResolveSandSource(data);
                            allLevels.Add(data);
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[LevelManager] Failed to parse {asset.name}: {e.Message}");
                    }
                }
            }

            // Fallback: create default lighthouse level if no files found
            if (allLevels.Count == 0)
            {
                allLevels.Add(CreateDefaultLighthouseLevel());
                Debug.Log("[LevelManager] No level files found, using default lighthouse level");
            }

            // Clamp current index
            if (CurrentLevelIndex >= allLevels.Count)
                CurrentLevelIndex = 0;

            Debug.Log($"[LevelManager] Loaded {allLevels.Count} level(s), current index: {CurrentLevelIndex}");
        }

        public static void ForceReload()
        {
            loaded = false;
            Load();
        }

        public static LevelData GetCurrentLevel()
        {
            if (!loaded) Load();
            int idx = Mathf.Clamp(CurrentLevelIndex, 0, allLevels.Count - 1);
            return allLevels[idx];
        }

        public static LevelData GetLevel(int index)
        {
            if (!loaded) Load();
            if (index < 0 || index >= allLevels.Count) return null;
            return allLevels[index];
        }

        public static List<LevelData> GetAllLevels()
        {
            if (!loaded) Load();
            return allLevels;
        }

        public static void AdvanceLevel()
        {
            if (HasNextLevel)
                CurrentLevelIndex = CurrentLevelIndex + 1;
            else
                CurrentLevelIndex = 0; // loop back
        }

        public static void GoToPreviousLevel()
        {
            if (CurrentLevelIndex > 0)
                CurrentLevelIndex = CurrentLevelIndex - 1;
        }

        public static void GoToLevel(int index)
        {
            if (index >= 0 && index < allLevels.Count)
                CurrentLevelIndex = index;
        }

        public static void ResetProgress()
        {
            CurrentLevelIndex = 0;
        }

        private static int ExtractLevelNumber(string name)
        {
            // Parse "Level_N" format
            string[] parts = name.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[parts.Length - 1], out int num))
                return num;
            return 0;
        }

        private static void ResolveSandSource(LevelData level)
        {
            ResolvePictureSource(level);
            if (level.sandPictures != null)
                foreach (SandPictureData picture in level.sandPictures)
                    ResolvePictureSource(picture);
        }

        private static void ResolvePictureSource(SandPictureData level)
        {
            if (level == null || level.sandSourceMode != SandSourceMode.Pattern)
                return;

            if (string.IsNullOrEmpty(level.sandPatternResourcePath))
            {
                Debug.LogWarning($"[LevelManager] Pattern source is selected for '{level.levelName}', but no pattern file is assigned.");
                return;
            }

            SandPatternAsset pattern = Resources.Load<SandPatternAsset>(level.sandPatternResourcePath);
            if (pattern == null)
            {
                Debug.LogWarning(
                    $"[LevelManager] Could not load sand pattern '{level.sandPatternResourcePath}' for '{level.levelName}'.");
                return;
            }

            int expectedCount = pattern.gridSize * pattern.gridSize;
            if (pattern.gridSize <= 0 || pattern.pixels == null || pattern.pixels.Count != expectedCount)
            {
                Debug.LogWarning(
                    $"[LevelManager] Sand pattern '{pattern.name}' has an invalid grid. Expected {expectedCount} pixels.");
                return;
            }

            level.gridSize = pattern.gridSize;
            level.sandGrid = new List<byte>(pattern.pixels);
            level.palette = ClonePalette(pattern.palette);
        }

        private static List<SerializableColor> ClonePalette(List<SerializableColor> source)
        {
            List<SerializableColor> result = new List<SerializableColor>();
            if (source == null) return result;

            for (int i = 0; i < source.Count; i++)
            {
                SerializableColor color = source[i];
                if (color != null)
                    result.Add(new SerializableColor(color.r, color.g, color.b));
            }
            return result;
        }

        private static LevelData CreateDefaultLighthouseLevel()
        {
            LevelData level = new LevelData();
            level.levelName = "Lighthouse";
            level.gridSize = SandSimulator.GRID_SIZE;
            level.desiredColorCount = 4;

            // Palette: Empty(0 - not stored), Blue(1), White(2), Red(3), Orange(4)
            level.palette = new List<SerializableColor>
            {
                new SerializableColor(33f/255f, 150f/255f, 243f/255f),  // 1: Blue
                new SerializableColor(253f/255f, 251f/255f, 247f/255f), // 2: White
                new SerializableColor(244f/255f, 67f/255f, 54f/255f),   // 3: Red
                new SerializableColor(255f/255f, 202f/255f, 40f/255f)   // 4: Orange
            };

            // Generate lighthouse grid (same algorithm as SandSimulator.GenerateLighthouseImage)
            const int designSize = 80;
            int gs = SandSimulator.GRID_SIZE;
            level.sandGrid = new List<byte>(gs * gs);
            for (int y = 0; y < gs; y++)
            {
                int designY = Mathf.FloorToInt(y * designSize / (float)gs);
                for (int x = 0; x < gs; x++)
                {
                    int designX = Mathf.FloorToInt(x * designSize / (float)gs);
                    byte color = 1; // BLUE default sky

                    if (designY >= 65)
                    {
                        color = 4; // ORANGE beach
                    }
                    else if (designY >= 60 && designX >= 26 && designX <= 54)
                    {
                        color = 2; // WHITE base platform
                    }
                    else if (designX >= 32 && designX <= 48 && designY >= 25 && designY < 60)
                    {
                        if (designY >= 50) color = 3;      // RED
                        else if (designY >= 40) color = 2;  // WHITE
                        else if (designY >= 30) color = 3;  // RED
                        else color = 2;                // WHITE
                    }
                    else if (designX >= 30 && designX <= 50 && designY >= 18 && designY < 25)
                    {
                        color = 4; // ORANGE lantern room
                        if (designY >= 20 && designY <= 23 && ((designX >= 32 && designX <= 36) || (designX >= 44 && designX <= 48)))
                            color = 2; // WHITE windows
                    }
                    else if (designY >= 6 && designY < 18)
                    {
                        int w = Mathf.FloorToInt((designY - 6) * 1.5f);
                        if (Mathf.Abs(designX - 40) <= w) color = 3; // RED roof
                    }
                    else
                    {
                        // Clouds
                        if (designY >= 15 && designY <= 45 && designX < 28)
                        {
                            if (Mathf.Sin(designY * 0.4f) * 6f + designX < 18) color = 2;
                            if (designY > 20 && designY < 35 && designX < 24) color = 2;
                        }
                        if (designY >= 25 && designY <= 55 && designX > 52)
                        {
                            if (Mathf.Cos(designY * 0.3f) * 6f + (designSize - designX) < 20) color = 2;
                            if (designY > 35 && designY < 45 && designX > 56) color = 2;
                        }
                    }

                    level.sandGrid.Add(color);
                }
            }

            // Bucket grid: 5x5 with 22 enabled cells (rows 0-3 full, row 4 only col 1 and 3)
            level.bucketRows = 5;
            level.bucketColumns = 5;
            level.bucketSpacingX = 0f;
            level.bucketSpacingY = 0f;
            level.gridCellEnabled = new List<bool>();
            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    if (r < 4)
                        level.gridCellEnabled.Add(true);
                    else
                        level.gridCellEnabled.Add(c == 1 || c == 3);
                }
            }

            // Bucket assignments matching LAYOUT_DEFS
            level.buckets = new List<BucketDef>
            {
                new BucketDef { row=0, col=0, colorId=4, isMystery=false },  // OR
                new BucketDef { row=0, col=1, colorId=0, isMystery=true },   // MY
                new BucketDef { row=0, col=2, colorId=4, isMystery=false },  // OR
                new BucketDef { row=0, col=3, colorId=0, isMystery=true },   // MY
                new BucketDef { row=0, col=4, colorId=4, isMystery=false },  // OR

                new BucketDef { row=1, col=0, colorId=1, isMystery=false },  // BL
                new BucketDef { row=1, col=1, colorId=3, isMystery=false },  // RE
                new BucketDef { row=1, col=2, colorId=1, isMystery=false },  // BL
                new BucketDef { row=1, col=3, colorId=3, isMystery=false },  // RE
                new BucketDef { row=1, col=4, colorId=1, isMystery=false },  // BL

                new BucketDef { row=2, col=0, colorId=2, isMystery=false },  // WH
                new BucketDef { row=2, col=1, colorId=0, isMystery=true },   // MY
                new BucketDef { row=2, col=2, colorId=2, isMystery=false },  // WH
                new BucketDef { row=2, col=3, colorId=0, isMystery=true },   // MY
                new BucketDef { row=2, col=4, colorId=2, isMystery=false },  // WH

                new BucketDef { row=3, col=0, colorId=3, isMystery=false },  // RE
                new BucketDef { row=3, col=1, colorId=4, isMystery=false },  // OR
                new BucketDef { row=3, col=2, colorId=3, isMystery=false },  // RE
                new BucketDef { row=3, col=3, colorId=4, isMystery=false },  // OR
                new BucketDef { row=3, col=4, colorId=3, isMystery=false },  // RE

                new BucketDef { row=4, col=1, colorId=0, isMystery=true },   // MY
                new BucketDef { row=4, col=3, colorId=0, isMystery=true },   // MY
            };

            level.maxBeltSlots = 5;
            level.slotBoosterCount = 3;
            level.shuffleBoosterCount = 3;
            level.magicBoosterCount = 3;

            return level;
        }

#if UNITY_EDITOR
        public static string GetLevelsFolderPath()
        {
            return System.IO.Path.Combine(Application.dataPath, "SandFlowPuzzlePackage", "Resources", "Levels");
        }

        public static string GetLevelFilePath(int index)
        {
            return System.IO.Path.Combine(GetLevelsFolderPath(), $"Level_{index + 1}.json");
        }

        public static void SaveLevel(int index, LevelData level)
        {
            string folder = GetLevelsFolderPath();
            if (!System.IO.Directory.Exists(folder))
                System.IO.Directory.CreateDirectory(folder);

            string json = JsonUtility.ToJson(level, true);
            string path = GetLevelFilePath(index);
            System.IO.File.WriteAllText(path, json);
        }

        public static void SaveAllLevels()
        {
            string folder = GetLevelsFolderPath();
            if (!System.IO.Directory.Exists(folder))
                System.IO.Directory.CreateDirectory(folder);

            // Delete existing level files
            foreach (string file in System.IO.Directory.GetFiles(folder, "Level_*.json"))
                System.IO.File.Delete(file);

            for (int i = 0; i < allLevels.Count; i++)
            {
                string json = JsonUtility.ToJson(allLevels[i], true);
                string path = GetLevelFilePath(i);
                System.IO.File.WriteAllText(path, json);
            }

            UnityEditor.AssetDatabase.Refresh();
        }

        public static void DeleteLevelFile(int index)
        {
            string path = GetLevelFilePath(index);
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }

        public static void LoadAllLevelsFromDisk()
        {
            loaded = false;
            allLevels.Clear();

            string folder = GetLevelsFolderPath();
            if (!System.IO.Directory.Exists(folder))
            {
                allLevels.Add(CreateDefaultLighthouseLevel());
                loaded = true;
                return;
            }

            string[] files = System.IO.Directory.GetFiles(folder, "Level_*.json");
            var sorted = files.OrderBy(f => ExtractLevelNumber(System.IO.Path.GetFileNameWithoutExtension(f))).ToArray();

            foreach (string file in sorted)
            {
                try
                {
                    string json = System.IO.File.ReadAllText(file);
                    LevelData data = JsonUtility.FromJson<LevelData>(json);
                    if (data != null)
                    {
                        ResolveSandSource(data);
                        allLevels.Add(data);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[LevelManager] Failed to parse {file}: {e.Message}");
                }
            }

            if (allLevels.Count == 0)
                allLevels.Add(CreateDefaultLighthouseLevel());

            loaded = true;
        }
#endif
    }
}
