using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

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
    private UniversalRenderPipelineAsset pipelineAsset;
    private float highShadowDistance;
    private int highPixelLightCount;
    private int highShadowCascades;
    private int highAntiAliasing;
    private float highLodBias;
    private float highRenderScale = 1f;
    private bool initialized;
    private bool mediumMode;

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
    }

    private void ResolveButtons()
    {
        if (highQualityButton == null)
            highQualityButton = FindButton("Near", "High", "High Quality", "با کیفیت");
        if (mediumQualityButton == null)
            mediumQualityButton = FindButton("Away", "Medium", "Medium Quality", "کیفیت متوسط");
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
}
