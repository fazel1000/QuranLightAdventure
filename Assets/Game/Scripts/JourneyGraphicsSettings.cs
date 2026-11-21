using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using TriForge;

[DisallowMultipleComponent]
[DefaultExecutionOrder(1100)]
public sealed class JourneyGraphicsSettings : MonoBehaviour
{
    private const string PreferenceKey = "JourneyOfLight.Graphics.Mode";

    [Header("Graphic Panel")]
    [SerializeField] private Button highQualityButton;
    [SerializeField] private Button mediumQualityButton;

    [Header("Terrain targets")]
    [Tooltip("Assign the scene terrains here. Leave empty to use every active Terrain.")]
    [SerializeField] private Terrain[] targetTerrains = Array.Empty<Terrain>();

    [Header("Wind")]
    [Tooltip("Optional. If empty, all wind controllers in the scene are found automatically.")]
    [SerializeField] private TF_FFE_WindController[] windControllers = Array.Empty<TF_FFE_WindController>();

    [Header("Animation distance")]
    [Tooltip("Animations farther than this distance from the player are paused in both quality modes.")]
    [SerializeField, Min(1f)] private float animationPauseDistance = 40f;
    [Tooltip("Optional. Leave empty to find the scene object named Player automatically.")]
    [SerializeField] private Transform playerTransform;

    [Header("Optional objects shown only in high quality")]
    [Tooltip("Assign manually placed foliage or effects that should be hidden in Medium mode.")]
    [SerializeField] private GameObject[] highQualityOnlyObjects = Array.Empty<GameObject>();

    [Header("Medium terrain profile")]
    [SerializeField, Min(20f)] private float mediumTreeDistance = 120f;
    [SerializeField, Min(5f)] private float mediumTreeBillboardDistance = 30f;
    [SerializeField, Min(0)] private int mediumTreeMaximumFullLodCount = 15;
    [SerializeField, Min(10f)] private float mediumDetailDistance = 25f;
    [SerializeField, Range(0f, 1f)] private float mediumDetailDensity = 0.2f;
    [SerializeField, Min(0f)] private float mediumShadowDistance = 25f;
    [SerializeField, Range(0.5f, 1f)] private float mediumRenderScale = 0.8f;
    [SerializeField] private bool mediumDisablesTerrainShadows = true;

    private readonly List<TerrainSnapshot> terrainSnapshots = new List<TerrainSnapshot>();
    private readonly List<WindSnapshot> windSnapshots = new List<WindSnapshot>();
    private readonly Dictionary<Animator, float> animatorSpeeds = new Dictionary<Animator, float>();
    private readonly Dictionary<Animation, float> legacyAnimationSpeeds = new Dictionary<Animation, float>();
    private UniversalRenderPipelineAsset pipelineAsset;
    private float highShadowDistance;
    private int highPixelLightCount;
    private int highShadowCascades;
    private int highAntiAliasing;
    private float highLodBias;
    private float highRenderScale = 1f;
    private bool initialized;
    private bool mediumMode;
    private float nextAnimationScanTime;

    public bool IsMediumMode => mediumMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // PlayerPrefs remains persistent by design. This only prevents stale static state.
    }

    private void Awake()
    {
        CacheOriginalSettings();
        ResolveButtons();
        initialized = true;

        bool savedMedium = PlayerPrefs.GetInt(PreferenceKey, 1) == 1;
        ApplyMode(savedMedium, false);
    }

    private void OnEnable()
    {
        if (!initialized) return;
        SubscribeButtons();
    }

    private void Start()
    {
        if (!initialized) return;
        SubscribeButtons();
        RefreshButtonState();
    }

    private void Update()
    {
        UpdateAnimationDistance();
    }

    private void LateUpdate()
    {
        // Wind controllers write these globals in Update. Writing zero here guarantees
        // that medium mode wins even when several controllers are present.
        if (mediumMode)
        {
            Shader.SetGlobalFloat("FFE_Wind_Strength", 0f);
            Shader.SetGlobalFloat("FFE_Leaf_Flutter", 0f);
            Shader.SetGlobalFloat("FFE_Wind_Speed", 0f);
            Shader.SetGlobalFloat("_WindStrength", 0f);
            Shader.SetGlobalFloat("_WindSpeed", 0f);
            Shader.SetGlobalFloat("_LeavesWindStrength", 0f);
            Shader.SetGlobalFloat("_LeavesWindSpeed", 0f);
        }
    }

    private void OnDisable()
    {
        if (highQualityButton != null) highQualityButton.onClick.RemoveListener(SelectHighQuality);
        if (mediumQualityButton != null) mediumQualityButton.onClick.RemoveListener(SelectMediumQuality);
    }

    public void SelectHighQuality()
    {
        ApplyMode(false, true);
    }

    public void SelectMediumQuality()
    {
        ApplyMode(true, true);
    }

    public void ToggleQuality()
    {
        ApplyMode(!mediumMode, true);
    }

    private void ApplyMode(bool useMedium, bool savePreference)
    {
        mediumMode = useMedium;

        if (useMedium)
        {
            ApplyMediumQuality();
        }
        else
        {
            ApplyHighQuality();
        }

        SetHighQualityOnlyObjectsActive(!useMedium);
        RefreshButtonState();

        if (savePreference)
        {
            PlayerPrefs.SetInt(PreferenceKey, useMedium ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    private void CacheOriginalSettings()
    {
        highShadowDistance = QualitySettings.shadowDistance;
        highPixelLightCount = QualitySettings.pixelLightCount;
        highShadowCascades = QualitySettings.shadowCascades;
        highAntiAliasing = QualitySettings.antiAliasing;
        highLodBias = QualitySettings.lodBias;
        highRenderScale = 1f;

        pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipelineAsset != null) highRenderScale = pipelineAsset.renderScale;

        terrainSnapshots.Clear();
        foreach (Terrain terrain in GetTargetTerrains())
        {
            if (terrain == null) continue;
            terrainSnapshots.Add(new TerrainSnapshot(terrain));
        }

        windSnapshots.Clear();
        foreach (TF_FFE_WindController controller in GetWindControllers())
        {
            if (controller == null) continue;
            windSnapshots.Add(new WindSnapshot(controller));
        }
    }

    private void ResolveButtons()
    {
        if (highQualityButton == null)
            highQualityButton = FindButton("Near", "High", "High Quality", "با کیفیت");
        if (mediumQualityButton == null)
            mediumQualityButton = FindButton("Away", "Medium", "Medium Quality", "کیفیت متوسط");
    }

    private void UpdateAnimationDistance()
    {
        if (Time.unscaledTime < nextAnimationScanTime) return;
        nextAnimationScanTime = Time.unscaledTime + 0.25f;

        if (playerTransform == null)
        {
            GameObject playerObject = GameObject.Find("Player");
            if (playerObject != null) playerTransform = playerObject.transform;
        }

        if (playerTransform == null) return;
        Vector3 playerPosition = playerTransform.position;
        float maxDistanceSqr = animationPauseDistance * animationPauseDistance;

        Animator[] animators = FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Animator animator in animators)
        {
            if (animator == null || animator.transform.IsChildOf(playerTransform)) continue;
            if (!animatorSpeeds.ContainsKey(animator)) animatorSpeeds.Add(animator, animator.speed);
            animator.speed = (animator.transform.position - playerPosition).sqrMagnitude > maxDistanceSqr
                ? 0f
                : animatorSpeeds[animator];
        }

        Animation[] legacyAnimations = FindObjectsByType<Animation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Animation animation in legacyAnimations)
        {
            if (animation == null || animation.transform.IsChildOf(playerTransform)) continue;
            if (!legacyAnimationSpeeds.ContainsKey(animation)) legacyAnimationSpeeds.Add(animation, animation[animation.clip != null ? animation.clip.name : string.Empty]?.speed ?? 1f);
            AnimationState state = animation.clip != null ? animation[animation.clip.name] : null;
            if (state != null) state.speed = (animation.transform.position - playerPosition).sqrMagnitude > maxDistanceSqr
                ? 0f
                : legacyAnimationSpeeds[animation];
        }
    }

    private Button FindButton(params string[] names)
    {
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        foreach (Transform item in transforms)
        {
            foreach (string name in names)
            {
                if (!string.Equals(item.name, name, StringComparison.OrdinalIgnoreCase)) continue;
                Button button = item.GetComponent<Button>();
                if (button != null) return button;
            }
        }

        return null;
    }

    private void SubscribeButtons()
    {
        if (highQualityButton != null)
        {
            highQualityButton.onClick.RemoveListener(SelectHighQuality);
            highQualityButton.onClick.AddListener(SelectHighQuality);
        }

        if (mediumQualityButton != null)
        {
            mediumQualityButton.onClick.RemoveListener(SelectMediumQuality);
            mediumQualityButton.onClick.AddListener(SelectMediumQuality);
        }
    }

    private void ApplyMediumQuality()
    {
        QualitySettings.shadowDistance = mediumShadowDistance;
        QualitySettings.pixelLightCount = 1;
        QualitySettings.shadowCascades = 0;
        QualitySettings.antiAliasing = 0;
        QualitySettings.lodBias = Mathf.Min(highLodBias, 0.75f);

        if (pipelineAsset != null) pipelineAsset.renderScale = mediumRenderScale;

        foreach (Terrain terrain in GetTargetTerrains())
        {
            if (terrain == null) continue;
            terrain.treeDistance = mediumTreeDistance;
            terrain.treeBillboardDistance = mediumTreeBillboardDistance;
            terrain.treeMaximumFullLODCount = mediumTreeMaximumFullLodCount;
            terrain.detailObjectDistance = mediumDetailDistance;
            terrain.detailObjectDensity = mediumDetailDensity;
            terrain.drawInstanced = SystemInfo.supportsInstancing;
            if (mediumDisablesTerrainShadows) terrain.shadowCastingMode = ShadowCastingMode.Off;
        }

        foreach (WindSnapshot snapshot in windSnapshots)
        {
            snapshot.Stop();
        }
    }

    private void ApplyHighQuality()
    {
        QualitySettings.shadowDistance = highShadowDistance;
        QualitySettings.pixelLightCount = highPixelLightCount;
        QualitySettings.shadowCascades = highShadowCascades;
        QualitySettings.antiAliasing = highAntiAliasing;
        QualitySettings.lodBias = highLodBias;

        if (pipelineAsset != null) pipelineAsset.renderScale = highRenderScale;

        foreach (TerrainSnapshot snapshot in terrainSnapshots)
        {
            if (snapshot.Terrain == null) continue;
            snapshot.Restore();
        }

        foreach (WindSnapshot snapshot in windSnapshots)
        {
            snapshot.Restore();
        }
    }

    private IEnumerable<Terrain> GetTargetTerrains()
    {
        if (targetTerrains != null && targetTerrains.Length > 0)
        {
            for (int i = 0; i < targetTerrains.Length; i++)
            {
                if (targetTerrains[i] != null) yield return targetTerrains[i];
            }

            yield break;
        }

        Terrain[] activeTerrains = Terrain.activeTerrains;
        for (int i = 0; i < activeTerrains.Length; i++)
        {
            if (activeTerrains[i] != null) yield return activeTerrains[i];
        }
    }

    private IEnumerable<TF_FFE_WindController> GetWindControllers()
    {
        if (windControllers != null && windControllers.Length > 0)
        {
            for (int i = 0; i < windControllers.Length; i++)
            {
                if (windControllers[i] != null) yield return windControllers[i];
            }

            yield break;
        }

        TF_FFE_WindController[] activeControllers = FindObjectsByType<TF_FFE_WindController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < activeControllers.Length; i++)
        {
            if (activeControllers[i] != null) yield return activeControllers[i];
        }
    }

    private void SetHighQualityOnlyObjectsActive(bool active)
    {
        if (highQualityOnlyObjects == null) return;
        foreach (GameObject item in highQualityOnlyObjects)
        {
            if (item != null) item.SetActive(active);
        }
    }

    private void RefreshButtonState()
    {
        if (highQualityButton != null) highQualityButton.interactable = mediumMode;
        if (mediumQualityButton != null) mediumQualityButton.interactable = !mediumMode;
    }

    private sealed class TerrainSnapshot
    {
        public readonly Terrain Terrain;
        private readonly float treeDistance;
        private readonly float treeBillboardDistance;
        private readonly int treeMaximumFullLodCount;
        private readonly float detailObjectDistance;
        private readonly float detailObjectDensity;
        private readonly bool drawInstanced;
        private readonly ShadowCastingMode shadowCastingMode;

        public TerrainSnapshot(Terrain terrain)
        {
            Terrain = terrain;
            treeDistance = terrain.treeDistance;
            treeBillboardDistance = terrain.treeBillboardDistance;
            treeMaximumFullLodCount = terrain.treeMaximumFullLODCount;
            detailObjectDistance = terrain.detailObjectDistance;
            detailObjectDensity = terrain.detailObjectDensity;
            drawInstanced = terrain.drawInstanced;
            shadowCastingMode = terrain.shadowCastingMode;
        }

        public void Restore()
        {
            Terrain.treeDistance = treeDistance;
            Terrain.treeBillboardDistance = treeBillboardDistance;
            Terrain.treeMaximumFullLODCount = treeMaximumFullLodCount;
            Terrain.detailObjectDistance = detailObjectDistance;
            Terrain.detailObjectDensity = detailObjectDensity;
            Terrain.drawInstanced = drawInstanced;
            Terrain.shadowCastingMode = shadowCastingMode;
        }
    }

    private sealed class WindSnapshot
    {
        private readonly TF_FFE_WindController controller;
        private readonly float windStrength;
        private readonly float leafFlutterStrength;
        private readonly float windSpeed;

        public WindSnapshot(TF_FFE_WindController controller)
        {
            this.controller = controller;
            windStrength = controller.WindStrength;
            leafFlutterStrength = controller.LeafFlutterStrength;
            windSpeed = controller.WindSpeed;
        }

        public void Stop()
        {
            if (controller == null) return;
            controller.WindStrength = 0f;
            controller.LeafFlutterStrength = 0f;
            controller.WindSpeed = 0f;
        }

        public void Restore()
        {
            if (controller == null) return;
            controller.WindStrength = windStrength;
            controller.LeafFlutterStrength = leafFlutterStrength;
            controller.WindSpeed = windSpeed;
        }
    }
}
