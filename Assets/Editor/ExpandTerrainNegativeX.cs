// Unity 6 Editor. Save as Assets/Editor/ExpandTerrainNegativeX.cs.
// Builds a NEW TerrainData; assigns it to the same terrain and shifts its origin left.
// Existing world footprint and tree world positions are retained. Raster maps are resampled.
#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FantasyForestTools
{
    public sealed class ExpandTerrainNegativeX : EditorWindow
    {
        [SerializeField] private Terrain terrain;
        [SerializeField] private float addMeters = 1000f;
        [SerializeField] private Terrain changedTerrain;
        [SerializeField] private TerrainData previousData, previousColliderData, appliedData;
        [SerializeField] private Vector3 previousPosition;
        private Vector2 scroll;
        private long clippedCells;
        private const string Title = "Expand Terrain Negative X";

        [MenuItem("Tools/Fantasy Forest/Expand Terrain Negative X")]
        private static void Open()
        {
            GetWindow<ExpandTerrainNegativeX>(Title).minSize = new Vector2(510, 440);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Expand ONE terrain toward negative world X", EditorStyles.boldLabel);
            terrain = (Terrain)EditorGUILayout.ObjectField("Terrain to expand", terrain, typeof(Terrain), true);
            addMeters = EditorGUILayout.FloatField("Add on negative X (m)", addMeters);
            EditorGUILayout.HelpBox("Select your CURRENT CenteredForest_TEST, including its copied vegetation. " +
                "A new TerrainData asset is created and assigned to the SAME scene object. " +
                "No second terrain is created. The old data asset is kept for recovery.", MessageType.Info);
            EditorGUILayout.HelpBox("Existing land and trees stay at their world positions. " +
                "Only the negative-X boundary expands; positive-X and both Z boundaries stay fixed. " +
                "The new area extends the height and paint of the old left edge, with no new trees or grass. " +
                "You can sculpt and paint it afterward.", MessageType.Info);
            EditorGUILayout.HelpBox("Unity uses square raster maps even for a rectangular terrain. " +
                "Output height resolution is 4097; paint is at least the input resolution (up to 4096). " +
                "Maps are resampled: fine height/paint/holes and individual grass positions can change. " +
                "Grass counts may be capped by Unity. World-space positions of direct child objects are retained. " +
                "Terrain references/coordinates cached by custom scripts, baked NavMesh and lighting need separate updates.", MessageType.Warning);
            if (terrain && terrain.terrainData && addMeters > 0f)
            {
                Vector3 s = terrain.terrainData.size;
                EditorGUILayout.LabelField("Current size", s.x + " x " + s.z + " m");
                EditorGUILayout.LabelField("Output size", (s.x + addMeters) + " x " + s.z + " m");
                EditorGUILayout.LabelField("New world origin X", (terrain.transform.position.x - addMeters).ToString("0.#####"));
                EditorGUILayout.LabelField("Output height spacing X / Z", ((s.x + addMeters) / 4096f).ToString("0.###") + " / " + (s.z / 4096f).ToString("0.###") + " m");
            }
            using (new EditorGUI.DisabledScope(!terrain || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Expand Negative X - Save New Data", GUILayout.Height(38))) Expand();
            using (new EditorGUI.DisabledScope(!changedTerrain || !previousData || changedTerrain.terrainData != appliedData))
                if (GUILayout.Button("Restore Previous Size And Data")) Restore();
            EditorGUILayout.HelpBox("Run once for 1000 -> 2000 m. Running again on the enlarged terrain adds ANOTHER 1000 m. " +
                "Save the scene after testing. Undo or Restore reverses the data/origin change; the saved asset remains available.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void ValidateInput()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (!terrain || !terrain.terrainData) throw new InvalidOperationException("Select the scene terrain to expand.");
            if (EditorUtility.IsPersistent(terrain) || !terrain.gameObject.scene.IsValid() || EditorSceneManager.IsPreviewScene(terrain.gameObject.scene))
                throw new InvalidOperationException("Use a normal loaded scene terrain, not a prefab asset or prefab stage.");
            if (Quaternion.Angle(terrain.transform.rotation, Quaternion.identity) > 0.01f ||
                (terrain.transform.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
                throw new InvalidOperationException("World rotation must be zero and world scale must be one (check parents).");
            if (float.IsNaN(addMeters) || float.IsInfinity(addMeters) || addMeters <= 0 || terrain.terrainData.size.x + addMeters > 100000)
                throw new InvalidOperationException("Enter a positive finite expansion; total width must not exceed 100000 m.");
            if (terrain.leftNeighbor || terrain.rightNeighbor || terrain.topNeighbor || terrain.bottomNeighbor)
                throw new InvalidOperationException("Disconnect neighboring terrain tiles first. This tool expands an isolated terrain.");
            foreach (TerrainLayer layer in terrain.terrainData.terrainLayers)
                if (!layer || layer.tileSize.x <= 0 || layer.tileSize.y <= 0)
                    throw new InvalidOperationException("Fix missing Terrain Layers or nonpositive tiling sizes before expanding.");
        }

        private void Expand()
        {
            string path = null;
            TerrainData output = null;
            bool created = false, committed = false;
            int undoGroup = -1;
            try
            {
                ValidateInput();
                path = EditorUtility.SaveFilePanelInProject("Save enlarged terrain data", "Forest_ExpandedNegativeX", "asset", "The original TerrainData is kept.");
                if (string.IsNullOrEmpty(path)) return;
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                TerrainData input = terrain.terrainData;
                Vector3 oldPosition = terrain.transform.position;
                output = new TerrainData { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                output.heightmapResolution = 4097;
                output.size = new Vector3(input.size.x + addMeters, input.size.y, input.size.z);
                output.baseMapResolution = input.baseMapResolution;
                output.enableHolesTextureCompression = input.enableHolesTextureCompression;
                AssetDatabase.CreateAsset(output, path);
                created = AssetDatabase.GetAssetPath(output) == path;
                if (!created) throw new InvalidOperationException("Could not save the new TerrainData asset.");
                CopyHeights(input, output);
                CopyHoles(input, output);
                CopyPaint(input, output);
                CopyTrees(input, output);
                clippedCells = 0;
                CopyDetails(input, output);
                EditorUtility.SetDirty(output);
                AssetDatabase.SaveAssets();
                Progress("Applying the finished terrain", 0.99f);

                // Keep the same component, rendering settings, material, scripts and collider.
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Expand terrain toward negative X");
                Undo.RecordObject(terrain, "Expand terrain data");
                Undo.RecordObject(terrain.transform, "Shift terrain origin");
                TerrainCollider collider = terrain.GetComponent<TerrainCollider>();
                changedTerrain = terrain;
                previousData = input;
                previousPosition = oldPosition;
                previousColliderData = collider ? collider.terrainData : null;
                appliedData = output;
                terrain.terrainData = output;
                if (collider)
                {
                    Undo.RecordObject(collider, "Expand terrain collider");
                    collider.terrainData = output;
                    Dirty(collider);
                }
                MoveOriginKeepingChildren(terrain.transform, oldPosition - Vector3.right * addMeters);
                terrain.allowAutoConnect = false;
                terrain.SetNeighbors(null, null, null, null);
                terrain.Flush();
                Dirty(terrain);
                Dirty(terrain.transform);
                EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
                Undo.CollapseUndoOperations(undoGroup);
                committed = true;
                SceneView.RepaintAll();
                EditorUtility.ClearProgressBar();
                string message = "Expanded one terrain to " + output.size.x + " x " + output.size.z +
                    " m. Added only on negative X. Existing world footprint and tree positions retained.\n\n" +
                    "Raster maps were resampled. Save your scene.\nNew data: " + path;
                if (clippedCells > 0) message += "\n\nGrass density capped in " + clippedCells + " cells; some detail instances were lost.";
                Debug.Log(message, terrain);
                EditorUtility.DisplayDialog(Title, message, "OK");
            }
            catch (OperationCanceledException) { Debug.Log("Terrain expansion cancelled; previous terrain retained."); }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (!committed && undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog(Title, e.Message + "\n" + (committed ? "Use Restore if needed." : "Previous terrain retained."), "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!committed)
                {
                    if (created) AssetDatabase.DeleteAsset(path);
                    else if (output && !AssetDatabase.Contains(output)) DestroyImmediate(output);
                }
            }
        }

        private void CopyHeights(TerrainData input, TerrainData output)
        {
            float[,] data = input.GetHeights(0, 0, input.heightmapResolution, input.heightmapResolution);
            int r = output.heightmapResolution;
            // Strip writes avoid a second 4097-square managed height array.
            for (int z0 = 0; z0 < r; z0 += 32)
            {
                Progress("Expanding heights", 0.02f + 0.2f * z0 / r);
                int rows = Math.Min(32, r - z0);
                float[,] strip = new float[rows, r];
                for (int dz = 0; dz < rows; dz++)
                for (int x = 0; x < r; x++)
                {
                    float oldX = x / (float)(r - 1) * output.size.x - addMeters;
                    strip[dz, x] = SampleHeight(data, oldX / input.size.x, (z0 + dz) / (float)(r - 1));
                }
                output.SetHeightsDelayLOD(0, z0, strip);
            }
            output.SyncHeightmap();
        }

        private void CopyHoles(TerrainData input, TerrainData output)
        {
            bool[,] holes = input.GetHoles(0, 0, input.holesResolution, input.holesResolution);
            int r = output.holesResolution;
            for (int z0 = 0; z0 < r; z0 += 32)
            {
                Progress("Expanding terrain holes", 0.23f + 0.08f * z0 / r);
                int rows = Math.Min(32, r - z0);
                bool[,] band = new bool[rows, r];
                for (int dz = 0; dz < rows; dz++)
                for (int x = 0; x < r; x++)
                {
                    float oldX = (x + 0.5f) / r * output.size.x - addMeters;
                    int sx = Mathf.Clamp(Mathf.FloorToInt(oldX / input.size.x * input.holesResolution), 0, input.holesResolution - 1);
                    int sz = Mathf.Clamp(Mathf.FloorToInt((z0 + dz + 0.5f) / r * input.holesResolution), 0, input.holesResolution - 1);
                    // Unity true = solid. New extension is always solid, never a stretched hole.
                    band[dz, x] = oldX < 0f || holes[sz, sx];
                }
                output.SetHoles(0, z0, band);
            }
        }

        private void CopyPaint(TerrainData input, TerrainData output)
        {
            TerrainLayer[] layers = input.terrainLayers;
            if (layers.Length == 0) return;
            TerrainLayer[] clones = new TerrainLayer[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                clones[i] = Instantiate(layers[i]);
                clones[i].name = layers[i].name;
                // Local X increases by addMeters at every unchanged world point.
                clones[i].tileOffset = layers[i].tileOffset - new Vector2(addMeters, 0f);
                AssetDatabase.AddObjectToAsset(clones[i], output);
            }
            output.terrainLayers = clones;
            int desired = Mathf.CeilToInt(input.alphamapResolution * output.size.x / input.size.x);
            output.alphamapResolution = Mathf.Clamp(Mathf.NextPowerOfTwo(desired), input.alphamapResolution, 4096);
            int r = output.alphamapResolution;
            for (int z0 = 0; z0 < r; z0 += 8)
            {
                Progress("Expanding painted textures", 0.32f + 0.27f * z0 / r);
                int rows = Math.Min(8, r - z0);
                int first = Mathf.Clamp(Mathf.FloorToInt((z0 + 0.5f) / r * input.alphamapHeight - 0.5f), 0, input.alphamapHeight - 1);
                int last = Mathf.Clamp(Mathf.FloorToInt((z0 + rows - 0.5f) / r * input.alphamapHeight - 0.5f) + 1, 0, input.alphamapHeight - 1);
                float[,,] source = input.GetAlphamaps(0, first, input.alphamapWidth, last - first + 1);
                float[,,] band = new float[rows, r, layers.Length];
                for (int dz = 0; dz < rows; dz++)
                {
                    float pz = Mathf.Clamp((z0 + dz + 0.5f) / r * input.alphamapHeight - 0.5f, 0, input.alphamapHeight - 1);
                    int az = Mathf.FloorToInt(pz), bz = Mathf.Min(az + 1, input.alphamapHeight - 1);
                    for (int x = 0; x < r; x++)
                    {
                        float oldX = (x + 0.5f) / r * output.size.x - addMeters;
                        float px = Mathf.Clamp(oldX / input.size.x * input.alphamapWidth - 0.5f, 0, input.alphamapWidth - 1);
                        int ax = Mathf.FloorToInt(px), bx = Mathf.Min(ax + 1, input.alphamapWidth - 1);
                        float sum = 0f;
                        for (int l = 0; l < layers.Length; l++)
                        {
                            float value = Mathf.Lerp(Mathf.Lerp(source[az - first, ax, l], source[az - first, bx, l], px - ax),
                                Mathf.Lerp(source[bz - first, ax, l], source[bz - first, bx, l], px - ax), pz - az);
                            band[dz, x, l] = value;
                            sum += value;
                        }
                        if (sum > 0) for (int l = 0; l < layers.Length; l++) band[dz, x, l] /= sum;
                        else band[dz, x, 0] = 1;
                    }
                }
                output.SetAlphamaps(0, z0, band);
            }
        }

        private void CopyTrees(TerrainData input, TerrainData output)
        {
            output.treePrototypes = input.treePrototypes;
            TreeInstance[] trees = input.treeInstances;
            for (int i = 0; i < trees.Length; i++)
            {
                if ((i & 2047) == 0) Progress("Remapping tree coordinates", 0.6f);
                TreeInstance t = trees[i];
                t.position = new Vector3((t.position.x * input.size.x + addMeters) / output.size.x, t.position.y, t.position.z);
                trees[i] = t;
            }
            output.SetTreeInstances(trees, false);
        }

        private void CopyDetails(TerrainData input, TerrainData output)
        {
            output.SetDetailScatterMode(input.detailScatterMode);
            output.wavingGrassAmount = input.wavingGrassAmount;
            output.wavingGrassSpeed = input.wavingGrassSpeed;
            output.wavingGrassStrength = input.wavingGrassStrength;
            output.wavingGrassTint = input.wavingGrassTint;
            if (input.detailResolution <= 0 || input.detailPrototypes.Length == 0) return;
            int desired = Mathf.CeilToInt(input.detailResolution * output.size.x / input.size.x / 16f) * 16;
            output.SetDetailResolution(Mathf.Clamp(desired, 16, 4048), 16);
            output.detailPrototypes = input.detailPrototypes;
            int r = output.detailResolution;
            double outputCellX = output.size.x / (double)r;
            double outputCellZ = output.size.z / (double)r;
            double sourceCellX = input.size.x / (double)input.detailWidth;
            double sourceCellZ = input.size.z / (double)input.detailHeight;
            int x0 = Math.Max(0, (int)Math.Floor(addMeters / outputCellX));
            bool coverage = input.detailScatterMode == DetailScatterMode.CoverageMode;
            for (int layer = 0; layer < input.detailPrototypes.Length; layer++)
            {
                int[,] map = input.GetDetailLayer(0, 0, input.detailWidth, input.detailHeight, layer);
                for (int row = 0; row < r; row += 16)
                {
                    Progress("Resampling grass / flowers " + (layer + 1) + "/" + input.detailPrototypes.Length,
                        0.61f + 0.36f * (layer + row / (float)r) / input.detailPrototypes.Length);
                    int rows = Math.Min(16, r - row);
                    int[,] band = new int[rows, r - x0];
                    for (int z = row; z < row + rows; z++)
                    for (int x = x0; x < r; x++)
                    {
                        double ax = (x * outputCellX - addMeters) / sourceCellX;
                        double bx = ((x + 1) * outputCellX - addMeters) / sourceCellX;
                        double az = z * outputCellZ / sourceCellZ;
                        double bz = (z + 1) * outputCellZ / sourceCellZ;
                        double value = Integrate(map, ax, bx, az, bz);
                        if (coverage) value /= (bx - ax) * (bz - az);
                        int count = StableRound(value, x, z, layer);
                        if (count > output.maxDetailScatterPerRes) clippedCells++;
                        band[z - row, x - x0] = Math.Min(count, output.maxDetailScatterPerRes);
                    }
                    output.SetDetailLayer(x0, row, layer, band);
                }
            }
        }

        private static float SampleHeight(float[,] values, float u, float v)
        {
            float px = Mathf.Clamp01(u) * (values.GetLength(1) - 1), pz = Mathf.Clamp01(v) * (values.GetLength(0) - 1);
            int x = Mathf.FloorToInt(px), z = Mathf.FloorToInt(pz);
            int bx = Mathf.Min(x + 1, values.GetLength(1) - 1), bz = Mathf.Min(z + 1, values.GetLength(0) - 1);
            return Mathf.Lerp(Mathf.Lerp(values[z, x], values[z, bx], px - x), Mathf.Lerp(values[bz, x], values[bz, bx], px - x), pz - z);
        }

        private static double Integrate(int[,] map, double ax, double bx, double az, double bz)
        {
            int left = Math.Max(0, (int)Math.Floor(ax)), right = Math.Min(map.GetLength(1), (int)Math.Ceiling(bx));
            int bottom = Math.Max(0, (int)Math.Floor(az)), top = Math.Min(map.GetLength(0), (int)Math.Ceiling(bz));
            double sum = 0;
            for (int z = bottom; z < top; z++)
            for (int x = left; x < right; x++)
                sum += map[z, x] * Math.Max(0, Math.Min(bx, x + 1) - Math.Max(ax, x)) * Math.Max(0, Math.Min(bz, z + 1) - Math.Max(az, z));
            return sum;
        }

        private static int StableRound(double value, int x, int z, int layer)
        {
            unchecked
            {
                uint h = (uint)x * 73856093u ^ (uint)z * 19349663u ^ (uint)(layer + 1) * 83492791u;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return (int)Math.Floor(value) + ((h & 0x00ffffffu) / 16777216.0 < value - Math.Floor(value) ? 1 : 0);
            }
        }

        private static void MoveOriginKeepingChildren(Transform root, Vector3 position)
        {
            Transform[] children = new Transform[root.childCount];
            Vector3[] points = new Vector3[children.Length];
            for (int i = 0; i < children.Length; i++)
            {
                children[i] = root.GetChild(i);
                points[i] = children[i].position;
                Undo.RecordObject(children[i], "Keep terrain child world position");
            }
            root.position = position;
            for (int i = 0; i < children.Length; i++) { children[i].position = points[i]; Dirty(children[i]); }
        }

        private void Restore()
        {
            if (!changedTerrain || !previousData || changedTerrain.terrainData != appliedData) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Restore terrain before expansion");
            Undo.RecordObject(changedTerrain, "Restore terrain data");
            Undo.RecordObject(changedTerrain.transform, "Restore terrain origin");
            changedTerrain.terrainData = previousData;
            TerrainCollider collider = changedTerrain.GetComponent<TerrainCollider>();
            if (collider)
            {
                Undo.RecordObject(collider, "Restore collider data");
                collider.terrainData = previousColliderData;
                Dirty(collider);
            }
            MoveOriginKeepingChildren(changedTerrain.transform, previousPosition);
            changedTerrain.Flush();
            Dirty(changedTerrain);
            Dirty(changedTerrain.transform);
            EditorSceneManager.MarkSceneDirty(changedTerrain.gameObject.scene);
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
        }

        private static void Dirty(UnityEngine.Object obj)
        {
            EditorUtility.SetDirty(obj);
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        }

        private static void Progress(string message, float progress)
        {
            if (EditorUtility.DisplayCancelableProgressBar(Title, message, progress)) throw new OperationCanceledException();
        }
    }
}
#endif
