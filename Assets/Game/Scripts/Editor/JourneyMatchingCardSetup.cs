#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class JourneyMatchingCardSetup
{
    private const string BoardName = "32 Matching Card Puzzle";
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private sealed class CardModel
    {
        public MeshFilter filter;
        public int pairId;
    }
    private sealed class BoardScan
    {
        public readonly List<CardModel> cards = new List<CardModel>();
        public readonly List<string> unpaired = new List<string>();
        public readonly List<string> readErrors = new List<string>();
        public int meshCount, repeatedPictures, unreadableMeshes;
        public bool Complete => cards.Count == 32 && repeatedPictures == 0;
        public string Summary => meshCount + " mesh objects, " + cards.Count + " cards in complete pairs, " +
            unpaired.Count + " unmatched picture(s), " + repeatedPictures + " picture(s) repeated more than twice, " + unreadableMeshes + " meshes without readable picture UVs.";
    }
    private sealed class BoardScanner
    {
        private readonly Dictionary<Transform, BoardScan> boards = new Dictionary<Transform, BoardScan>();
        private readonly Dictionary<Mesh, string> meshPictures = new Dictionary<Mesh, string>();
        private readonly Dictionary<Mesh, string> meshReadErrors = new Dictionary<Mesh, string>();
        private string PictureUVs(Mesh mesh)
        {
            if (meshPictures.TryGetValue(mesh, out string cached)) return cached;
            string result = null;
            try
            {
                using (var data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    var snapshot = data[0];
                    if (snapshot.vertexCount > 0 && snapshot.HasVertexAttribute(VertexAttribute.TexCoord0))
                    {
                        using (var uvs = new NativeArray<Vector2>(snapshot.vertexCount, Allocator.Temp))
                        {
                            snapshot.GetUVs(0, uvs);
                            var coordinates = new HashSet<Vector2Int>();
                            for (int i = 0; i < uvs.Length; i++)
                                coordinates.Add(new Vector2Int(Mathf.RoundToInt(uvs[i].x * 10000f), Mathf.RoundToInt(uvs[i].y * 10000f)));
                            var key = new StringBuilder();
                            foreach (var coordinate in coordinates.OrderBy(value => value.x).ThenBy(value => value.y))
                                key.Append(coordinate.x).Append(',').Append(coordinate.y).Append(';');
                            result = key.ToString();
                        }
                    }
                }
            }
            catch (Exception error) { result = null; meshReadErrors[mesh] = error.Message; }
            if (result == null && !meshReadErrors.ContainsKey(mesh)) meshReadErrors[mesh] = "No usable UV0 texture coordinates.";
            meshPictures[mesh] = result; return result;
        }
        private static string TextureKey(MeshFilter filter)
        {
            var key = new StringBuilder();
            foreach (var material in filter.GetComponent<Renderer>().sharedMaterials)
            {
                if (material == null) { key.Append("none;"); continue; }
                string property = material.HasProperty("_BaseMap") ? "_BaseMap" : material.HasProperty("_MainTex") ? "_MainTex" : null;
                Texture texture = property != null ? material.GetTexture(property) : null;
                key.Append(texture != null ? texture.GetInstanceID() : material.GetInstanceID()).Append(':');
                if (property != null)
                {
                    Vector2 scale = material.GetTextureScale(property), offset = material.GetTextureOffset(property);
                    key.Append(Mathf.RoundToInt(scale.x * 10000f)).Append(',').Append(Mathf.RoundToInt(scale.y * 10000f)).Append(',')
                        .Append(Mathf.RoundToInt(offset.x * 10000f)).Append(',').Append(Mathf.RoundToInt(offset.y * 10000f));
                }
                key.Append(';');
            }
            return key.ToString();
        }
        public BoardScan Scan(Transform root)
        {
            if (boards.TryGetValue(root, out BoardScan cached)) return cached;
            var result = new BoardScan(); boards[root] = result;
            var filters = root.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.sharedMesh != null && filter.GetComponent<Renderer>() != null).ToArray();
            result.meshCount = filters.Length;
            // Avoid reading the entire map when a broad environment parent is selected.
            if (filters.Length > 128) return result;
            var groups = new SortedDictionary<string, List<MeshFilter>>(StringComparer.Ordinal);
            foreach (var filter in filters)
            {
                string uv = PictureUVs(filter.sharedMesh);
                if (string.IsNullOrEmpty(uv))
                {
                    result.unreadableMeshes++;
                    if (meshReadErrors.TryGetValue(filter.sharedMesh, out string problem)) result.readErrors.Add(filter.name + ": " + problem);
                    continue;
                }
                string key = TextureKey(filter) + "|" + uv;
                if (!groups.TryGetValue(key, out List<MeshFilter> group)) { group = new List<MeshFilter>(); groups.Add(key, group); }
                group.Add(filter);
            }
            int pairId = 0;
            foreach (var group in groups.Values)
            {
                if (group.Count == 1) { result.unpaired.Add(group[0].name); continue; }
                if (group.Count != 2) { result.repeatedPictures++; continue; }
                pairId++;
                foreach (var filter in group) result.cards.Add(new CardModel { filter = filter, pairId = pairId });
            }
            return result;
        }
    }
    private static Transform FindBoardRoot(Scene scene, BoardScanner scanner)
    {
        Transform selected = Selection.activeTransform;
        if (selected == null || EditorUtility.IsPersistent(selected) || selected.gameObject.scene != scene)
        {
            Debug.LogError("Select the puzzle instance in the active scene's Hierarchy, then run setup. The password cube is optional.");
            return null;
        }
        // Keep an existing manager on its original root, including a root with
        // an intermediate group containing the card meshes.
        for (Transform ancestor = selected; ancestor != null; ancestor = ancestor.parent)
            if (ancestor.GetComponent<CardGameManager>() != null && scanner.Scan(ancestor).Complete) return ancestor;
        Transform diagnosticRoot = selected; int closestCount = int.MaxValue;
        for (Transform ancestor = selected; ancestor != null; ancestor = ancestor.parent)
        {
            BoardScan scan = scanner.Scan(ancestor);
            if (scan.Complete)
            {
                var managers = ancestor.GetComponentsInChildren<CardGameManager>(true).Where(manager => scanner.Scan(manager.transform).Complete).ToArray();
                return managers.Length == 1 ? managers[0].transform : ancestor;
            }
            int gap = Mathf.Abs(scan.meshCount - 32);
            if (gap < closestCount) { closestCount = gap; diagnosticRoot = ancestor; }
            if (ancestor.name.StartsWith(BoardName, StringComparison.OrdinalIgnoreCase)) break;
        }
        var contained = selected.GetComponentsInChildren<Transform>(true).Where(candidate => scanner.Scan(candidate).Complete).ToArray();
        var roots = contained.Where(candidate => !contained.Any(child => child != candidate && child.IsChildOf(candidate))).ToArray();
        if (roots.Length == 1) return roots[0];
        if (roots.Length > 1) Debug.LogError("This selection contains multiple matching puzzles. Select one puzzle root and run setup.", selected);
        else
        {
            BoardScan scan = scanner.Scan(diagnosticRoot);
            string unmatched = scan.unpaired.Count > 0 ? " Unmatched objects: " + string.Join(", ", scan.unpaired.Take(6)) + "." : "";
            string readProblem = scan.readErrors.Count > 0 ? " Mesh details: " + string.Join(" | ", scan.readErrors.Take(2)) + "." : "";
            Debug.LogError("Matching Card: '" + diagnosticRoot.name + "' has " + scan.Summary + unmatched + readProblem +
                " Setup needs 32 playing cards forming 16 picture pairs. The password/reward cube is NOT required; do not restore it. Restore a playing card only if one of the 32 cards was removed.", diagnosticRoot);
        }
        return null;
    }
    [MenuItem("Quran Kids/Setup Matching Card Puzzle")]
    private static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogError("Exit Play Mode before matching-card setup."); return; }
        Scene scene = SceneManager.GetActiveScene();
        var scanner = new BoardScanner();
        Transform root = FindBoardRoot(scene, scanner);
        if (root == null) return;
        var models = scanner.Scan(root).cards;
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Set up 32 matching cards");
        var manager = root.GetComponent<CardGameManager>();
        if (manager == null) manager = Undo.AddComponent<CardGameManager>(root.gameObject);
        Undo.RecordObject(manager, "Connect matching puzzle");
        manager.cardsRoot = root; manager.requiredCardCount = 32; manager.completionPoints = 500;
        manager.menu = All<JourneyMenuController>(scene).FirstOrDefault();
        manager.scoreReceiver = All<LightBrushPuzzle>(scene).FirstOrDefault();
        manager.interactionCamera = All<Camera>(scene).FirstOrDefault(camera => camera.CompareTag("MainCamera"));
        manager.player = All<GameCreator.Runtime.Characters.Character>(scene).FirstOrDefault(character => character.IsPlayer);
        var cards = new List<CardBehaviour>();
        foreach (var entry in models)
        {
            Transform model = entry.filter.transform;
            var card = model.GetComponent<CardBehaviour>();
            if (card == null) card = Undo.AddComponent<CardBehaviour>(model.gameObject);
            Undo.RecordObject(card, "Assign the picture pair"); card.pairId = entry.pairId; card.isFlipped = false;
            var box = model.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = Undo.AddComponent<BoxCollider>(model.gameObject);
                Bounds bounds = entry.filter.sharedMesh.bounds;
                box.center = bounds.center; Vector3 size = bounds.size;
                box.size = new Vector3(Mathf.Max(0.005f, size.x), Mathf.Max(0.005f, size.y), Mathf.Max(0.005f, size.z));
            }
            Undo.RecordObject(box, "Enable card taps"); box.enabled = true; box.isTrigger = false;
            foreach (var part in model.GetComponentsInChildren<Transform>(true))
            { Undo.RecordObject(part.gameObject, "Make flipping cards movable"); GameObjectUtility.SetStaticEditorFlags(part.gameObject, 0); JourneyWorldSetupUtility.Dirty(part.gameObject); }
            foreach (var body in model.GetComponentsInChildren<Rigidbody>(true))
            { Undo.RecordObject(body, "Keep card motion controlled by the puzzle"); body.isKinematic = true; body.useGravity = false; JourneyWorldSetupUtility.Dirty(body); }
            JourneyWorldSetupUtility.Dirty(card); JourneyWorldSetupUtility.Dirty(box); cards.Add(card);
        }
        manager.cards = cards;
        var audio = manager.AudioMusic;
        if (audio == null || audio.gameObject != root.gameObject)
        {
            audio = root.GetComponent<AudioSource>();
            if (audio == null) audio = Undo.AddComponent<AudioSource>(root.gameObject);
        }
        Undo.RecordObject(audio, "Configure puzzle sound source");
        audio.playOnAwake = false; audio.loop = false; audio.spatialBlend = 0f; manager.AudioMusic = audio;
        var click = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Matching Card/Click.mp3");
        if (manager.MatchAudioClip == null) manager.MatchAudioClip = click;
        if (manager.SuccessAudioClip == null) manager.SuccessAudioClip = click;
        JourneyWorldSetupUtility.Dirty(audio); JourneyWorldSetupUtility.Dirty(manager);
        Undo.CollapseUndoOperations(group); EditorSceneManager.MarkSceneDirty(scene); Selection.activeGameObject = root.gameObject;
        Debug.Log("Matching cards setup COMPLETE for '" + root.name + "': 32 cards, 16 verified picture pairs, colliders, shuffled layout and 500 coins once on completion. Set Success Audio Clip on this instance's root manager, then save the scene. Cube.032 is unused.", manager);
    }
    [MenuItem("Quran Kids/Validate Matching Card Puzzle")]
    private static void Validate()
    {
        var managers = All<CardGameManager>(SceneManager.GetActiveScene());
        if (managers.Length == 0) { Debug.LogError("Run Quran Kids > Setup Matching Card Puzzle first."); return; }
        foreach (var manager in managers)
        {
            if (!manager.ValidateConfiguration(out string error)) { Debug.LogError("Matching Card: " + error, manager); continue; }
            bool missingCollider = manager.cards.Any(card => !card.GetComponentsInChildren<Collider>(true).Any(collider => collider.enabled && !collider.isTrigger));
            if (missingCollider || manager.AudioMusic == null || manager.SuccessAudioClip == null)
            { Debug.LogError("Each card needs an enabled non-trigger collider, and the manager needs Audio Music and Success Audio Clip.", manager); continue; }
            Debug.Log("Matching card references OK: 32 cards / 16 pairs. Check flipping, mismatches, all-pairs reward and touch input in Play and on device.", manager);
        }
    }
}
#endif
