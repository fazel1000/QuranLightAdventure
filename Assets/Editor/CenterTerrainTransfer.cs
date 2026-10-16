// Unity 6 Editor tool. Put this file in Assets/Editor/CenterTerrainTransfer.cs.
// Menu: Tools > Fantasy Forest > Center Terrain Transfer
// Creates a NEW TerrainData asset and a NEW terrain object. Inputs are read-only.
// Copies heights and painted terrain layers, NOT trees, grass, holes or props.
// Save the scene after testing. Height/paint resampling is not lossless.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FantasyForestTools
{
    public sealed class CenterTerrainTransfer : EditorWindow
    {
        [SerializeField] private Terrain source;
        [SerializeField] private Terrain destination;
        [SerializeField] private int heightResolution = 4097;
        [SerializeField] private int paintResolution = 4096;
        [SerializeField] private float transitionWidth = 15f;
        [SerializeField] private bool copyPaint = true;
        [SerializeField] private Terrain lastResult;
        private Vector2 scroll;
        private const string Title = "Center Terrain Transfer";
        private static readonly int[] HeightChoices = { 513, 1025, 2049, 4097 };
        private static readonly string[] HeightLabels = { "513", "1025", "2049", "4097 (default)" };
        private static readonly int[] PaintChoices = { 1024, 2048, 4096 };
        private static readonly string[] PaintLabels = { "1024", "2048 (lighter)", "4096 (default)" };

        [MenuItem("Tools/Fantasy Forest/Center Terrain Transfer")]
        private static void Open()
        {
            GetWindow<CenterTerrainTransfer>(Title).minSize = new Vector2(460, 570);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Copy a small terrain into the center of a larger one", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("SOURCE = original 200 x 200 forest. DESTINATION = new 1000 x 1000 terrain. " +
                "Both inputs remain unchanged. This creates a separate test terrain and asset.", MessageType.Info);
            source = (Terrain)EditorGUILayout.ObjectField("Source (small forest)", source, typeof(Terrain), true);
            destination = (Terrain)EditorGUILayout.ObjectField("Destination (large)", destination, typeof(Terrain), true);
            heightResolution = EditorGUILayout.IntPopup("Output height resolution", heightResolution, HeightLabels, HeightChoices);
            copyPaint = EditorGUILayout.Toggle("Copy painted textures", copyPaint);
            using (new EditorGUI.DisabledScope(!copyPaint))
                paintResolution = EditorGUILayout.IntPopup("Output paint resolution", paintResolution, PaintLabels, PaintChoices);
            transitionWidth = Mathf.Max(0f, EditorGUILayout.FloatField("Outer transition (meters)", transitionWidth));
            EditorGUILayout.HelpBox("The entire source footprint is copied at its original size. The transition is OUTSIDE " +
                "that footprint. Source world-space heights are preserved; output base Y/range may change to avoid clipping. " +
                "Terrain rotation must be zero and world scale must be one.", MessageType.Info);
            EditorGUILayout.HelpBox("Test output contains terrain heights + surface paint only. It does NOT copy trees, " +
                "grass/details, terrain holes, props, buildings, water or scripts. Models in Map 1 stay where they are. " +
                "4096 paint with many layers uses substantial memory; use 2048 on a low-memory PC. " +
                "This is not a mobile performance preset.", MessageType.Warning);
            if (source && destination && source.terrainData && destination.terrainData)
            {
                Vector3 s = source.terrainData.size;
                Vector3 d = destination.terrainData.size;
                Vector2 offset = new Vector2((d.x - s.x) * 0.5f, (d.z - s.z) * 0.5f);
                EditorGUILayout.LabelField("Source footprint", s.x.ToString("0.##") + " x " + s.z.ToString("0.##") + " m");
                EditorGUILayout.LabelField("Centered X range", offset.x.ToString("0.##") + " to " + (offset.x + s.x).ToString("0.##") + " m");
                EditorGUILayout.LabelField("Centered Z range", offset.y.ToString("0.##") + " to " + (offset.y + s.z).ToString("0.##") + " m");
                EditorGUILayout.LabelField("Output height sample spacing", (d.x / (heightResolution - 1)).ToString("0.###") + " m (X)");
                if (copyPaint)
                    EditorGUILayout.LabelField("Output paint sample spacing", (d.x / (paintResolution - 1)).ToString("0.###") + " m (X)");
            }
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!source || !destination || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Create Centered Test Copy", GUILayout.Height(36))) CreateTestCopy();
            }
            EditorGUILayout.Space();
            lastResult = (Terrain)EditorGUILayout.ObjectField("Last test result", lastResult, typeof(Terrain), true);
            EditorGUILayout.HelpBox("The result initially overlaps the large input terrain. After a successful transfer, " +
                "use the buttons below to switch between them (no automatic hiding). These buttons also switch the collider. " +
                "They do not move or hide Map 1.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!lastResult || !destination))
            {
                if (GUILayout.Button("Show Result / Hide Large Input")) SwitchVisibility(true);
                if (GUILayout.Button("Restore Large Input / Hide Result")) SwitchVisibility(false);
            }
            EditorGUILayout.EndScrollView();
        }

        private void ValidateInputs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode first.");
            if (!source || !destination || !source.terrainData || !destination.terrainData)
                throw new InvalidOperationException("Assign both terrain objects from the Hierarchy.");
            if (source == destination || source.terrainData == destination.terrainData)
                throw new InvalidOperationException("Source and destination must use different TerrainData assets.");
            foreach (Terrain t in new[] { source, destination })
            {
                if (EditorUtility.IsPersistent(t) || !t.gameObject.scene.IsValid() ||
                    EditorSceneManager.IsPreviewScene(t.gameObject.scene))
                    throw new InvalidOperationException("Use terrain instances in a normal, loaded scene, not a prefab asset/stage.");
                if (Quaternion.Angle(t.transform.rotation, Quaternion.identity) > 0.01f ||
                    (t.transform.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
                    throw new InvalidOperationException(t.name + ": world rotation must be zero and world scale must be (1,1,1). Check parents too.");
            }
            Vector3 s = source.terrainData.size;
            Vector3 d = destination.terrainData.size;
            if (s.x <= 0 || s.z <= 0 || s.y <= 0 || d.y <= 0 || s.x > d.x || s.z > d.z)
                throw new InvalidOperationException("The source footprint must fit inside the destination.");
            if (copyPaint && source.terrainData.terrainLayers.Length == 0)
                throw new InvalidOperationException("The source has no Terrain Layers to copy.");
            CheckLayers(source.terrainData);
            CheckLayers(destination.terrainData);
            if (float.IsNaN(transitionWidth) || float.IsInfinity(transitionWidth))
                throw new InvalidOperationException("Transition width must be a finite number.");
        }

        private static void CheckLayers(TerrainData data)
        {
            foreach (TerrainLayer layer in data.terrainLayers)
                if (!layer || layer.tileSize.x <= 0f || layer.tileSize.y <= 0f)
                    throw new InvalidOperationException(data.name + ": remove missing Terrain Layers and use positive tiling sizes first.");
        }

        private void CreateTestCopy()
        {
            string outputPath = null;
            TerrainData output = null;
            GameObject resultObject = null;
            bool createdAsset = false;
            bool complete = false;
            try
            {
                ValidateInputs();
                outputPath = EditorUtility.SaveFilePanelInProject("Save NEW test TerrainData", "CenteredForest_Test", "asset",
                    "Choose a location for the new test asset. Existing assets will not be overwritten.");
                if (string.IsNullOrEmpty(outputPath)) return;
                outputPath = AssetDatabase.GenerateUniqueAssetPath(outputPath);
                TerrainData src = source.terrainData;
                TerrainData dst = destination.terrainData;
                Vector3 sourcePosition = source.transform.position;
                Vector3 destinationPosition = destination.transform.position;
                Vector2 offset = new Vector2((dst.size.x - src.size.x) * 0.5f, (dst.size.z - src.size.z) * 0.5f);
                float feather = Mathf.Min(transitionWidth, Mathf.Min(offset.x, offset.y));
                // Cover both possible world height ranges, preserving their absolute heights.
                float baseY = Mathf.Min(sourcePosition.y, destinationPosition.y);
                float topY = Mathf.Max(sourcePosition.y + src.size.y, destinationPosition.y + dst.size.y);
                float heightRange = topY - baseY;
                if (heightRange > 10000f)
                    throw new InvalidOperationException("Combined vertical range exceeds 10000 m. Bring the terrain base heights closer together first.");
                output = new TerrainData();
                output.name = System.IO.Path.GetFileNameWithoutExtension(outputPath);
                output.heightmapResolution = heightResolution;
                output.size = new Vector3(dst.size.x, heightRange, dst.size.z);
                output.baseMapResolution = dst.baseMapResolution;
                AssetDatabase.CreateAsset(output, outputPath);
                createdAsset = AssetDatabase.GetAssetPath(output) == outputPath;
                if (!createdAsset) throw new InvalidOperationException("Could not create the new TerrainData asset.");

                CopyHeights(src, dst, output, sourcePosition.y, destinationPosition.y, baseY, offset, feather);
                CopyPaint(src, dst, output, offset, feather);
                EditorUtility.SetDirty(output);
                AssetDatabase.SaveAssets();

                resultObject = Terrain.CreateTerrainGameObject(output);
                resultObject.name = "CenteredForest_TEST";
                if (resultObject.scene != destination.gameObject.scene)
                    SceneManager.MoveGameObjectToScene(resultObject, destination.gameObject.scene);
                resultObject.transform.position = new Vector3(destinationPosition.x, baseY, destinationPosition.z);
                resultObject.layer = destination.gameObject.layer;
                Terrain result = resultObject.GetComponent<Terrain>();
                result.materialTemplate = source.materialTemplate ? source.materialTemplate : destination.materialTemplate;
                result.drawInstanced = destination.drawInstanced;
                result.heightmapPixelError = destination.heightmapPixelError;
                result.basemapDistance = destination.basemapDistance;
                result.shadowCastingMode = destination.shadowCastingMode;
                result.allowAutoConnect = false;
                result.SetNeighbors(null, null, null, null);
                result.Flush();
                Undo.RegisterCreatedObjectUndo(resultObject, "Create centered terrain test");
                EditorSceneManager.MarkSceneDirty(resultObject.scene);
                lastResult = result;
                Selection.activeGameObject = resultObject;
                if (SceneView.lastActiveSceneView)
                    SceneView.lastActiveSceneView.Frame(new Bounds(new Vector3(destinationPosition.x + dst.size.x * 0.5f,
                        sourcePosition.y + src.size.y * 0.05f, destinationPosition.z + dst.size.z * 0.5f),
                        new Vector3(src.size.x * 1.2f, 60f, src.size.z * 1.2f)), false);
                complete = true;
                Debug.Log("Centered terrain test created: " + outputPath +
                    ". Original terrains unchanged. No trees, details, holes or props were copied.", resultObject);
                EditorUtility.DisplayDialog(Title, "Created a separate test terrain. Originals are unchanged.\n\n" +
                    "Click 'Show Result / Hide Large Input' in this window to avoid overlapping surfaces. " +
                    "Only terrain heights and surface paint were copied; no trees or props were moved.\n\n" +
                    "Save the scene to keep the result object. Terrain asset:\n" + outputPath, "OK");
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Terrain transfer cancelled. Original terrains were not changed.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(Title, ex.Message + "\n\nOriginal terrains were not changed. See Console for details.", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!complete)
                {
                    // Only remove objects/assets created by this attempt. Never touch an input asset.
                    if (resultObject) DestroyImmediate(resultObject);
                    if (createdAsset && !string.IsNullOrEmpty(outputPath)) AssetDatabase.DeleteAsset(outputPath);
                    else if (output && !AssetDatabase.Contains(output)) DestroyImmediate(output);
                }
            }
        }

        private static void CopyHeights(TerrainData src, TerrainData dst, TerrainData output,
            float sourceY, float destinationY, float outputY, Vector2 offset, float feather)
        {
            float[,] sh = src.GetHeights(0, 0, src.heightmapResolution, src.heightmapResolution);
            float[,] dh = dst.GetHeights(0, 0, dst.heightmapResolution, dst.heightmapResolution);
            int res = output.heightmapResolution;
            float[,] heights = new float[res, res];
            for (int z = 0; z < res; z++)
            {
                if ((z & 31) == 0) Progress("Resampling terrain heights", 0.05f + 0.35f * z / res);
                float v = z / (float)(res - 1);
                float localZ = v * dst.size.z;
                for (int x = 0; x < res; x++)
                {
                    float u = x / (float)(res - 1);
                    float localX = u * dst.size.x;
                    float worldHeight = destinationY + SampleHeight(dh, u, v) * dst.size.y;
                    float weight = PatchWeight(localX, localZ, offset, src.size, feather);
                    if (weight > 0f)
                    {
                        float su = Mathf.Clamp01((localX - offset.x) / src.size.x);
                        float sv = Mathf.Clamp01((localZ - offset.y) / src.size.z);
                        float forestHeight = sourceY + SampleHeight(sh, su, sv) * src.size.y;
                        worldHeight = Mathf.Lerp(worldHeight, forestHeight, weight);
                    }
                    heights[z, x] = Mathf.Clamp01((worldHeight - outputY) / output.size.y);
                }
            }
            Progress("Writing heightmap (this may take a moment)", 0.41f);
            output.SetHeights(0, 0, heights);
        }

        private void CopyPaint(TerrainData src, TerrainData dst, TerrainData output, Vector2 offset, float feather)
        {
            var layers = new List<TerrainLayer>();
            foreach (TerrainLayer layer in dst.terrainLayers)
                layers.Add(CloneLayer(layer, output, Vector2.zero, "Outside_"));
            int sourceStart = layers.Count;
            if (copyPaint)
                foreach (TerrainLayer layer in src.terrainLayers)
                    layers.Add(CloneLayer(layer, output, offset, "Forest_"));
            if (layers.Count == 0) return;
            output.terrainLayers = layers.ToArray();
            output.alphamapResolution = copyPaint ? paintResolution : dst.alphamapResolution;
            int res = output.alphamapResolution;
            int dstCount = dst.terrainLayers.Length;
            int srcCount = copyPaint ? src.terrainLayers.Length : 0;
            const int rowsPerBatch = 32;
            for (int z0 = 0; z0 < res; z0 += rowsPerBatch)
            {
                Progress("Copying painted texture masks (Cancel is safe)", 0.45f + 0.5f * z0 / res);
                int rows = Mathf.Min(rowsPerBatch, res - z0);
                // URP Terrain/Lit maps UV endpoints to control-map texel centers:
                // shader UV = (terrainUV * (resolution - 1) + 0.5) / resolution.
                float v0 = z0 / (float)(res - 1);
                float v1 = (z0 + rows - 1) / (float)(res - 1);
                AlphaBand dstBand = dstCount > 0 ? new AlphaBand(dst, v0, v1) : null;
                bool touchesSource = copyPaint && v1 * dst.size.z >= offset.y - feather &&
                    v0 * dst.size.z <= offset.y + src.size.z + feather;
                AlphaBand srcBand = touchesSource ? new AlphaBand(src,
                    (v0 * dst.size.z - offset.y) / src.size.z,
                    (v1 * dst.size.z - offset.y) / src.size.z) : null;
                float[,,] values = new float[rows, res, layers.Count];
                for (int z = 0; z < rows; z++)
                {
                    float v = (z0 + z) / (float)(res - 1);
                    float localZ = v * dst.size.z;
                    for (int x = 0; x < res; x++)
                    {
                        float u = x / (float)(res - 1);
                        float localX = u * dst.size.x;
                        float weight = srcBand != null ? PatchWeight(localX, localZ, offset, src.size, feather) : 0f;
                        float sum = 0f;
                        if (dstBand != null && weight < 1f)
                        {
                            for (int l = 0; l < dstCount; l++)
                            {
                                float value = dstBand.Sample(u, v, l) * (1f - weight);
                                values[z, x, l] = value;
                                sum += value;
                            }
                        }
                        else if (dstCount == 0 && weight < 1f)
                        {
                            // With no destination paint, use the first forest layer as the exterior base.
                            values[z, x, sourceStart] = 1f - weight;
                            sum += 1f - weight;
                        }
                        if (weight > 0f)
                        {
                            float su = Mathf.Clamp01((localX - offset.x) / src.size.x);
                            float sv = Mathf.Clamp01((localZ - offset.y) / src.size.z);
                            for (int l = 0; l < srcCount; l++)
                            {
                                float value = srcBand.Sample(su, sv, l) * weight;
                                values[z, x, sourceStart + l] += value;
                                sum += value;
                            }
                        }
                        if (sum <= 0.000001f) values[z, x, 0] = 1f;
                        else for (int l = 0; l < layers.Count; l++) values[z, x, l] /= sum;
                    }
                }
                output.SetAlphamaps(0, z0, values);
            }
        }

        private static TerrainLayer CloneLayer(TerrainLayer input, TerrainData owner, Vector2 patchOffset, string prefix)
        {
            TerrainLayer clone = Instantiate(input);
            clone.name = prefix + input.name;
            // Preserve the local phase of tiled textures after translating the forest footprint.
            clone.tileOffset = input.tileOffset - patchOffset;
            AssetDatabase.AddObjectToAsset(clone, owner);
            return clone;
        }

        // Only loads the source rows needed by one destination strip; avoids a huge full alphamap allocation.
        private sealed class AlphaBand
        {
            private readonly float[,,] data;
            private readonly int width;
            private readonly int fullHeight;
            private readonly int firstRow;

            public AlphaBand(TerrainData terrain, float v0, float v1)
            {
                width = terrain.alphamapWidth;
                fullHeight = terrain.alphamapHeight;
                float p0 = Mathf.Clamp01(v0) * (fullHeight - 1);
                float p1 = Mathf.Clamp01(v1) * (fullHeight - 1);
                firstRow = Mathf.FloorToInt(Mathf.Min(p0, p1));
                int lastRow = Mathf.Min(fullHeight - 1, Mathf.FloorToInt(Mathf.Max(p0, p1)) + 1);
                data = terrain.GetAlphamaps(0, firstRow, width, lastRow - firstRow + 1);
            }

            public float Sample(float u, float v, int layer)
            {
                float px = Mathf.Clamp01(u) * (width - 1);
                float py = Mathf.Clamp01(v) * (fullHeight - 1);
                int x0 = Mathf.FloorToInt(px);
                int z0 = Mathf.FloorToInt(py);
                int x1 = Mathf.Min(x0 + 1, width - 1);
                int z1 = Mathf.Min(z0 + 1, fullHeight - 1);
                float a = Mathf.Lerp(data[z0 - firstRow, x0, layer], data[z0 - firstRow, x1, layer], px - x0);
                float b = Mathf.Lerp(data[z1 - firstRow, x0, layer], data[z1 - firstRow, x1, layer], px - x0);
                return Mathf.Lerp(a, b, py - z0);
            }
        }

        private static float SampleHeight(float[,] values, float u, float v)
        {
            int height = values.GetLength(0);
            int width = values.GetLength(1);
            float x = Mathf.Clamp01(u) * (width - 1);
            float z = Mathf.Clamp01(v) * (height - 1);
            int x0 = Mathf.FloorToInt(x);
            int z0 = Mathf.FloorToInt(z);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int z1 = Mathf.Min(z0 + 1, height - 1);
            return Mathf.Lerp(Mathf.Lerp(values[z0, x0], values[z0, x1], x - x0),
                Mathf.Lerp(values[z1, x0], values[z1, x1], x - x0), z - z0);
        }

        private static float PatchWeight(float x, float z, Vector2 offset, Vector3 sourceSize, float feather)
        {
            float dx = Mathf.Max(Mathf.Max(offset.x - x, 0f), x - offset.x - sourceSize.x);
            float dz = Mathf.Max(Mathf.Max(offset.y - z, 0f), z - offset.y - sourceSize.z);
            if (dx == 0f && dz == 0f) return 1f;
            if (feather <= 0f) return 0f;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            if (distance >= feather) return 0f;
            float t = distance / feather;
            return 1f - t * t * (3f - 2f * t);
        }

        private static void Progress(string message, float progress)
        {
            if (EditorUtility.DisplayCancelableProgressBar(Title, message, progress))
                throw new OperationCanceledException();
        }

        private void SwitchVisibility(bool showResult)
        {
            if (!lastResult || !destination || lastResult == destination) return;
            // Change Terrain + collider components, never entire map hierarchies.
            Undo.RecordObjects(new UnityEngine.Object[] { destination, lastResult }, "Switch terrain test visibility");
            destination.enabled = !showResult;
            lastResult.enabled = showResult;
            TerrainCollider dc = destination.GetComponent<TerrainCollider>();
            TerrainCollider rc = lastResult.GetComponent<TerrainCollider>();
            if (dc) { Undo.RecordObject(dc, "Switch terrain collider"); dc.enabled = !showResult; }
            if (rc) { Undo.RecordObject(rc, "Switch terrain collider"); rc.enabled = showResult; }
            EditorSceneManager.MarkSceneDirty(destination.gameObject.scene);
            EditorSceneManager.MarkSceneDirty(lastResult.gameObject.scene);
            SceneView.RepaintAll();
        }
    }
}
#endif
