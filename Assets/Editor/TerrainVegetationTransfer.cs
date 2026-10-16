// Unity 6. Put in Assets/Editor. Works with the previously aligned large terrain.
// Clones the target TerrainData asset, replaces vegetation in the CLONE, then assigns it.
// Heights, textures, transform, source data and original target data stay unchanged.
#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FantasyForestTools
{
    public sealed class TerrainVegetationTransfer : EditorWindow
    {
        [SerializeField] private Terrain source;
        [SerializeField] private Terrain target;
        [SerializeField] private Terrain restoreTerrain;
        [SerializeField] private TerrainData previousData;
        [SerializeField] private TerrainData previousColliderData;
        [SerializeField] private TerrainData appliedData;
        private Vector2 scroll;
        private const string Title = "Terrain Vegetation Transfer";
        private long clippedCells;

        [MenuItem("Tools/Fantasy Forest/Terrain Vegetation Transfer")]
        private static void Open()
        {
            GetWindow<TerrainVegetationTransfer>(Title).minSize = new Vector2(490, 440);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Copy terrain trees, grass and flowers", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Source = original 200 x 200 Terrain (it may be disabled).\n" +
                "Target = CenteredForest_TEST, already aligned with the original forest.\n" +
                "This uses their CURRENT WORLD POSITIONS. Do not move either terrain again.", MessageType.Info);
            source = (Terrain)EditorGUILayout.ObjectField("Source (original forest)", source, typeof(Terrain), true);
            target = (Terrain)EditorGUILayout.ObjectField("Target (aligned test)", target, typeof(Terrain), true);
            EditorGUILayout.HelpBox("A NEW TerrainData asset is saved and assigned to the SAME target object. " +
                "Existing vegetation on the target is REPLACED, including vegetation outside the forest. " +
                "Use this on the test terrain, before planting other areas. Heights, paint and position are retained. " +
                "The source and old target assets remain unchanged.", MessageType.Warning);
            EditorGUILayout.HelpBox("Trees retain their world positions, rotation, scale and colors. " +
                "Grass/flowers copy their density maps; individual procedural stalk positions can change. " +
                "Child GameObjects, manually placed props and third-party vegetation systems are not copied.", MessageType.Info);
            if (source && source.terrainData)
            {
                TerrainData s = source.terrainData;
                EditorGUILayout.LabelField("Source tree instances", s.treeInstanceCount.ToString());
                EditorGUILayout.LabelField("Source detail types", s.detailPrototypes.Length.ToString());
                EditorGUILayout.LabelField("Source detail resolution", s.detailResolution.ToString());
                if (target && target.terrainData && s.detailResolution > 0)
                {
                    int r = ChooseResolution(s, target.terrainData);
                    EditorGUILayout.LabelField("Output detail resolution", r.ToString() + " (automatic, max 4048)");
                    if (r < DesiredResolution(s, target.terrainData))
                        EditorGUILayout.HelpBox("The large terrain reaches Unity's detail resolution limit. " +
                            "Fine detail placement will be resampled; dense instance-count cells can lose instances. " +
                            "Any clipped cells are reported after transfer.", MessageType.Warning);
                }
            }
            using (new EditorGUI.DisabledScope(!source || !target || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Copy Vegetation To Aligned Target", GUILayout.Height(38))) Transfer();
            using (new EditorGUI.DisabledScope(!restoreTerrain || !previousData || restoreTerrain.terrainData != appliedData))
                if (GUILayout.Button("Restore Previous Terrain Data")) Restore();
            EditorGUILayout.HelpBox("After copying, keep the original Terrain and the unused large input hidden. " +
                "Leave CenteredForest_TEST active, then save your scene. Restore only swaps data; it keeps display settings.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private static float DesiredResolution(TerrainData s, TerrainData d)
        {
            return s.detailResolution * Mathf.Max(d.size.x / s.size.x, d.size.z / s.size.z);
        }

        private static int ChooseResolution(TerrainData s, TerrainData d)
        {
            return Mathf.Clamp(Mathf.CeilToInt(DesiredResolution(s, d) / 16f) * 16, 16, 4048);
        }

        private void ValidateInputs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (!source || !target || !source.terrainData || !target.terrainData)
                throw new InvalidOperationException("Assign both terrain objects from the Hierarchy.");
            if (source == target || source.terrainData == target.terrainData)
                throw new InvalidOperationException("Use different source and target TerrainData assets.");
            foreach (Terrain t in new[] { source, target })
            {
                if (EditorUtility.IsPersistent(t) || !t.gameObject.scene.IsValid() || EditorSceneManager.IsPreviewScene(t.gameObject.scene))
                    throw new InvalidOperationException("Use terrains from a normal loaded scene, not a prefab asset or prefab stage.");
                if (Quaternion.Angle(t.transform.rotation, Quaternion.identity) > 0.01f ||
                    (t.transform.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
                    throw new InvalidOperationException(t.name + ": world rotation must be zero and world scale must be one.");
            }
            string path = AssetDatabase.GetAssetPath(target.terrainData);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save the target TerrainData as an .asset first.");
            Vector3 a = source.transform.position - target.transform.position;
            Vector3 s = source.terrainData.size;
            Vector3 d = target.terrainData.size;
            if (a.x < -0.01f || a.z < -0.01f || a.x + s.x > d.x + 0.01f || a.z + s.z > d.z + 0.01f)
                throw new InvalidOperationException("The original forest must fit inside the target at its current world position. Align the terrains first.");
            if (source.terrainData.treeInstanceCount == 0 && source.terrainData.detailPrototypes.Length == 0)
                throw new InvalidOperationException("Source has no terrain trees or detail prototypes. Check Source, or whether plants are child GameObjects / third-party vegetation.");
            foreach (TreePrototype p in source.terrainData.treePrototypes)
                if (p == null || !p.prefab) throw new InvalidOperationException("A source tree prototype has a missing prefab.");
            foreach (DetailPrototype p in source.terrainData.detailPrototypes)
                if (p == null || (p.usePrototypeMesh ? !p.prototype : !p.prototypeTexture))
                    throw new InvalidOperationException("A source grass/flower prototype has a missing mesh or texture.");
        }

        private void Transfer()
        {
            string path = null;
            bool created = false;
            bool assigned = false;
            try
            {
                ValidateInputs();
                path = EditorUtility.SaveFilePanelInProject("Save terrain WITH vegetation", "CenteredForest_WithVegetation", "asset", "A separate asset will be created.");
                if (string.IsNullOrEmpty(path)) return;
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                TerrainData old = target.terrainData;
                Progress("Copying the target terrain asset", 0f);
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(old), path))
                    throw new InvalidOperationException("Could not copy the target TerrainData asset.");
                created = true;
                TerrainData output = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                if (!output) throw new InvalidOperationException("Could not load the new TerrainData.");
                clippedCells = 0;
                CopyTrees(output);
                CopyDetails(output);
                EditorUtility.SetDirty(output);
                AssetDatabase.SaveAssets();
                // All expensive work completes before the scene object is changed.
                TerrainCollider collider = target.GetComponent<TerrainCollider>();
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Assign terrain with vegetation");
                int group = Undo.GetCurrentGroup();
                Undo.RecordObject(target, "Assign terrain with vegetation");
                restoreTerrain = target;
                previousData = old;
                previousColliderData = collider ? collider.terrainData : null;
                appliedData = output;
                target.terrainData = output;
                assigned = true;
                if (collider)
                {
                    Undo.RecordObject(collider, "Assign terrain collider data");
                    collider.terrainData = output;
                    EditorUtility.SetDirty(collider);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                }
                target.drawTreesAndFoliage = true;
                target.detailObjectDensity = source.detailObjectDensity;
                target.detailObjectDistance = Mathf.Max(target.detailObjectDistance, source.detailObjectDistance);
                target.treeDistance = Mathf.Max(target.treeDistance, source.treeDistance);
                target.treeBillboardDistance = source.treeBillboardDistance;
                target.treeCrossFadeLength = source.treeCrossFadeLength;
                target.treeMaximumFullLODCount = Mathf.Max(target.treeMaximumFullLODCount, source.treeMaximumFullLODCount);
                EditorUtility.SetDirty(target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                Undo.CollapseUndoOperations(group);
                target.Flush();
                EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
                SceneView.RepaintAll();
                Selection.activeGameObject = target.gameObject;
                string report = "Copied " + output.treeInstanceCount + " terrain trees and " + output.detailPrototypes.Length +
                    " detail types to the SAME target object. Position, heights and paint retained.\n\n" +
                    "Keep the source Terrain hidden and save the scene.\n\nNew asset: " + path;
                if (clippedCells > 0) report += "\n\nDensity was capped in " + clippedCells + " cells by Unity's per-cell limit. Some grass instances could not be retained.";
                Debug.Log(report, target);
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog(Title, report, "OK");
            }
            catch (OperationCanceledException) { Debug.Log("Vegetation transfer cancelled before assigning data."); }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog(Title, e.Message + (assigned ? "\nUse Restore Previous Terrain Data if needed." : "\nScene terrain data was not changed."), "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (created && !assigned) AssetDatabase.DeleteAsset(path);
            }
        }

        private void CopyTrees(TerrainData output)
        {
            TerrainData s = source.terrainData;
            output.treeInstances = Array.Empty<TreeInstance>();
            output.treePrototypes = s.treePrototypes;
            TreeInstance[] instances = s.treeInstances;
            Vector3 delta = source.transform.position - target.transform.position;
            for (int i = 0; i < instances.Length; i++)
            {
                if ((i & 1023) == 0) Progress("Copying terrain trees", 0.05f);
                TreeInstance item = instances[i];
                if (item.prototypeIndex < 0 || item.prototypeIndex >= s.treePrototypes.Length)
                    throw new InvalidOperationException("A source tree has an invalid prototype index.");
                Vector3 p = delta + Vector3.Scale(item.position, s.size);
                p = new Vector3(p.x / output.size.x, p.y / output.size.y, p.z / output.size.z);
                if (p.y < -0.00001f || p.y > 1.00001f)
                    throw new InvalidOperationException("A tree is outside the target vertical range. Check terrain base Y positions.");
                item.position = new Vector3(Mathf.Clamp01(p.x), Mathf.Clamp01(p.y), Mathf.Clamp01(p.z));
                instances[i] = item;
            }
            output.SetTreeInstances(instances, false); // Preserve original world Y, including hand-adjusted offsets.
        }

        private void CopyDetails(TerrainData output)
        {
            TerrainData s = source.terrainData;
            // Reset only the CLONED asset's vegetation. Re-running never appends duplicates.
            output.detailPrototypes = Array.Empty<DetailPrototype>();
            output.SetDetailResolution(0, 16);
            output.SetDetailScatterMode(s.detailScatterMode);
            output.wavingGrassAmount = s.wavingGrassAmount;
            output.wavingGrassSpeed = s.wavingGrassSpeed;
            output.wavingGrassStrength = s.wavingGrassStrength;
            output.wavingGrassTint = s.wavingGrassTint;
            if (s.detailResolution <= 0 || s.detailPrototypes.Length == 0) return;
            output.SetDetailResolution(ChooseResolution(s, output), 16);
            output.detailPrototypes = s.detailPrototypes;
            int resolution = output.detailResolution;
            Vector3 offset = source.transform.position - target.transform.position;
            double cellX = output.size.x / (double)resolution;
            double cellZ = output.size.z / (double)resolution;
            double sourceCellX = s.size.x / (double)s.detailWidth;
            double sourceCellZ = s.size.z / (double)s.detailHeight;
            int x0 = Mathf.Clamp((int)Math.Floor(offset.x / cellX), 0, resolution - 1);
            int z0 = Mathf.Clamp((int)Math.Floor(offset.z / cellZ), 0, resolution - 1);
            int xEnd = Mathf.Clamp((int)Math.Ceiling((offset.x + s.size.x) / cellX), 0, resolution);
            int zEnd = Mathf.Clamp((int)Math.Ceiling((offset.z + s.size.z) / cellZ), 0, resolution);
            bool coverage = s.detailScatterMode == DetailScatterMode.CoverageMode;
            for (int layer = 0; layer < s.detailPrototypes.Length; layer++)
            {
                int[,] input = s.GetDetailLayer(0, 0, s.detailWidth, s.detailHeight, layer);
                // Write only the forest rectangle; avoid a full 4048 x 4048 managed array per type.
                for (int row = z0; row < zEnd; row += 32)
                {
                    Progress("Copying grass / flowers: type " + (layer + 1) + " of " + s.detailPrototypes.Length,
                        0.1f + 0.85f * (layer + (row - z0) / (float)Math.Max(1, zEnd - z0)) / s.detailPrototypes.Length);
                    int rows = Math.Min(32, zEnd - row);
                    int[,] band = new int[rows, xEnd - x0];
                    for (int z = row; z < row + rows; z++)
                    for (int x = x0; x < xEnd; x++)
                    {
                        // Area integration handles both upsampling and downsampling without copying density 25x.
                        double ax = (x * cellX - offset.x) / sourceCellX;
                        double bx = ((x + 1) * cellX - offset.x) / sourceCellX;
                        double az = (z * cellZ - offset.z) / sourceCellZ;
                        double bz = ((z + 1) * cellZ - offset.z) / sourceCellZ;
                        double sum = Integrate(input, ax, bx, az, bz);
                        double value = coverage ? sum / ((bx - ax) * (bz - az)) : sum;
                        int count = StableRound(value, x, z, layer);
                        if (count > output.maxDetailScatterPerRes) clippedCells++;
                        band[z - row, x - x0] = Math.Min(count, output.maxDetailScatterPerRes);
                    }
                    output.SetDetailLayer(x0, row, layer, band);
                }
            }
        }

        private static double Integrate(int[,] map, double ax, double bx, double az, double bz)
        {
            int left = Math.Max(0, (int)Math.Floor(ax));
            int right = Math.Min(map.GetLength(1), (int)Math.Ceiling(bx));
            int bottom = Math.Max(0, (int)Math.Floor(az));
            int top = Math.Min(map.GetLength(0), (int)Math.Ceiling(bz));
            double sum = 0;
            for (int z = bottom; z < top; z++)
            for (int x = left; x < right; x++)
                sum += map[z, x] * Math.Max(0, Math.Min(bx, x + 1) - Math.Max(ax, x)) *
                    Math.Max(0, Math.Min(bz, z + 1) - Math.Max(az, z));
            return sum;
        }

        private static int StableRound(double value, int x, int z, int layer)
        {
            // Deterministic stochastic rounding avoids erasing sparse flowers when cells are subdivided.
            unchecked
            {
                uint h = (uint)x * 73856093u ^ (uint)z * 19349663u ^ (uint)(layer + 1) * 83492791u;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return (int)Math.Floor(value) + ((h & 0x00ffffffu) / 16777216.0 < value - Math.Floor(value) ? 1 : 0);
            }
        }

        private void Restore()
        {
            if (!restoreTerrain || !previousData || restoreTerrain.terrainData != appliedData) return;
            Undo.RecordObject(restoreTerrain, "Restore terrain data");
            restoreTerrain.terrainData = previousData;
            PrefabUtility.RecordPrefabInstancePropertyModifications(restoreTerrain);
            TerrainCollider collider = restoreTerrain.GetComponent<TerrainCollider>();
            if (collider)
            {
                Undo.RecordObject(collider, "Restore terrain collider data");
                collider.terrainData = previousColliderData;
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            }
            restoreTerrain.Flush();
            EditorSceneManager.MarkSceneDirty(restoreTerrain.gameObject.scene);
            SceneView.RepaintAll();
        }

        private static void Progress(string message, float fraction)
        {
            if (EditorUtility.DisplayCancelableProgressBar(Title, message, fraction)) throw new OperationCanceledException();
        }
    }
}
#endif
