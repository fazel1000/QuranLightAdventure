using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent, RequireComponent(typeof(JourneyVehicle))]
public sealed class JourneyVehicleEffects : MonoBehaviour
{
    [Header("Brake sound - separate from engine")]
    public AudioClip brakeLoop;
    public AudioSource brakeSource;
    [Range(0f, 1f)] public float brakeVolume = 0.5f;
    [Header("Skid marks")]
    public Material skidMaterial;
    public LayerMask skidSurfaces = ~0;
    [Min(0.01f)] public float skidWidth = 0.18f;
    [Min(0f)] public float minimumSkidSpeedKph = 5f;
    [Min(0f)] public float minimumSlip = 0.25f;
    [Tooltip("Arcade option: hard braking makes marks even before tires lock.")]
    public bool marksOnHardBrake = true;
    [Min(1f)] public float markLifetime = 20f;
    [Range(64, 4096)] public int maximumSegments = 1024;
    [Header("Exhaust - each point's blue Z arrow faces out of the pipe")]
    public Transform[] exhaustPoints;
    public Material smokeMaterial;
    [Min(0f)] public float idleSmokeRate = 3f;
    [Min(0f)] public float fullThrottleSmokeRate = 35f;
    [Min(0f)] public float nitroSmokeRate = 55f;

    private JourneyVehicle vehicle;
    private ParticleSystem[] smoke;
    private GameObject marksObject;
    private Mesh marksMesh;
    private Material ownedSkidMaterial, ownedSmokeMaterial;
    private Texture2D smokeTexture;
    private Vector3[] vertices, lastPoints, lastSides;
    private Color[] colors;
    private float[] births;
    private bool[] linked;
    private Collider[] lastSurfaces;
    private int cursor;
    private float smokeLoad;
    private bool ownsBrakeSource;

    private void Awake()
    {
        vehicle = GetComponent<JourneyVehicle>();
        if (brakeSource == null || brakeSource == vehicle.engineSource)
        {
            var audioObject = new GameObject("BrakeAudio");
            audioObject.transform.SetParent(transform, false);
            brakeSource = audioObject.AddComponent<AudioSource>();
            ownsBrakeSource = true;
        }
        brakeSource.playOnAwake = false;
        brakeSource.loop = true;
        brakeSource.spatialBlend = 1f;
        brakeSource.minDistance = 3f;
        brakeSource.maxDistance = 35f;
        brakeSource.volume = 0f;
        brakeSource.Stop();
        int wheelCount = vehicle.wheels != null ? vehicle.wheels.Length : 0;
        lastPoints = new Vector3[wheelCount]; lastSides = new Vector3[wheelCount];
        linked = new bool[wheelCount]; lastSurfaces = new Collider[wheelCount];
        BuildMarks();
        BuildSmoke();
    }
    private Material MakeMaterial(string title)
    {
        Shader shader = Shader.Find("Journey/Vehicle Effects");
        if (shader == null) { Debug.LogWarning("Assign transparent vertex-color materials for vehicle effects.", this); return null; }
        return new Material(shader) { name = title };
    }
    private void BuildMarks()
    {
        if (skidMaterial == null) { ownedSkidMaterial = MakeMaterial("Runtime Skid Material"); skidMaterial = ownedSkidMaterial; }
        if (skidMaterial == null) return;
        maximumSegments = Mathf.Clamp(maximumSegments, 64, 4096);
        vertices = new Vector3[maximumSegments * 4];
        colors = new Color[vertices.Length]; births = new float[maximumSegments];
        int[] triangles = new int[maximumSegments * 6];
        for (int i = 0; i < maximumSegments; i++)
        {
            births[i] = float.NegativeInfinity;
            int v = i * 4, t = i * 6;
            triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 2; triangles[t + 4] = v + 3; triangles[t + 5] = v + 1;
        }
        marksObject = new GameObject(name + " World Skid Marks");
        // Keep world-space marks in the owner's scene, but never parent them to the moving car.
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(marksObject, gameObject.scene);
        marksMesh = new Mesh { name = "Bounded Skid Marks" };
        marksMesh.MarkDynamic(); marksMesh.vertices = vertices; marksMesh.colors = colors;
        marksMesh.triangles = triangles;
        marksObject.AddComponent<MeshFilter>().sharedMesh = marksMesh;
        var renderer = marksObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = skidMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
    }
    private void BuildSmoke()
    {
        smoke = new ParticleSystem[exhaustPoints != null ? exhaustPoints.Length : 0];
        if (smoke.Length == 0) return;
        if (smokeMaterial == null)
        {
            ownedSmokeMaterial = MakeMaterial("Runtime Exhaust Material");
            smokeMaterial = ownedSmokeMaterial;
            if (smokeMaterial == null) return;
            smokeTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            smokeTexture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float radius = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                pixels[y * 32 + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - radius), 2f));
            }
            smokeTexture.SetPixels(pixels); smokeTexture.Apply(); smokeMaterial.mainTexture = smokeTexture;
        }
        for (int i = 0; i < smoke.Length; i++)
        {
            if (exhaustPoints[i] == null) continue;
            var go = new GameObject("Runtime Exhaust Smoke");
            go.transform.SetParent(exhaustPoints[i], false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            main.startColor = new Color(0.45f, 0.45f, 0.45f, 0.55f); main.maxParticles = 150;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 15f; shape.radius = 0.025f;
            var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f); velocity.y = new ParticleSystem.MinMaxCurve(0.3f); velocity.z = new ParticleSystem.MinMaxCurve(0f);
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 2f));
            var color = ps.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = smokeMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
            smoke[i] = ps;
        }
    }
    private void Update()
    {
        bool active = vehicle.EffectsActive;
        bool skidSound = false;
        for (int i = 0; i < linked.Length; i++)
        {
            var wheel = vehicle.wheels[i];
            WheelHit hit = default;
            if (wheel == null || wheel.collider == null || !wheel.collider.GetGroundHit(out hit))
            { linked[i] = false; continue; }
            bool slipping = Mathf.Abs(hit.forwardSlip) >= minimumSlip || Mathf.Abs(hit.sidewaysSlip) >= minimumSlip;
            bool skid = active && vehicle.SpeedKph >= minimumSkidSpeedKph &&
                (slipping || (marksOnHardBrake && vehicle.Brake01 >= 0.5f));
            if (!skid) { linked[i] = false; continue; }
            skidSound = true;
            if (marksMesh == null || hit.collider == null || (skidSurfaces.value & (1 << hit.collider.gameObject.layer)) == 0)
            { linked[i] = false; continue; }
            Vector3 point = hit.point + hit.normal * 0.015f;
            Vector3 side = hit.sidewaysDir.normalized * skidWidth * 0.5f;
            float distance = Vector3.Distance(point, lastPoints[i]);
            if (linked[i] && lastSurfaces[i] == hit.collider && distance < 2f && Vector3.Dot(side, lastSides[i]) > 0f)
            {
                if (distance < 0.07f) continue;
                int v = cursor * 4;
                vertices[v] = lastPoints[i] - lastSides[i]; vertices[v + 1] = lastPoints[i] + lastSides[i];
                vertices[v + 2] = point - side; vertices[v + 3] = point + side;
                births[cursor] = Time.time; cursor = (cursor + 1) % maximumSegments;
            }
            lastPoints[i] = point; lastSides[i] = side; lastSurfaces[i] = hit.collider; linked[i] = true;
        }
        if (marksMesh != null)
        {
            for (int i = 0; i < births.Length; i++)
            {
                float age = (Time.time - births[i]) / Mathf.Max(1f, markLifetime);
                var color = new Color(0.06f, 0.06f, 0.06f, 0.65f * Mathf.Clamp01((1f - age) * 4f));
                for (int j = 0; j < 4; j++) colors[i * 4 + j] = color;
            }
            marksMesh.vertices = vertices; marksMesh.colors = colors; marksMesh.RecalculateBounds();
        }
        UpdateAudio(active && skidSound);
        smokeLoad = Mathf.MoveTowards(smokeLoad, active ? vehicle.Throttle01 : 0f, Time.deltaTime * 3f);
        foreach (var ps in smoke)
        {
            if (ps == null) continue;
            var emission = ps.emission;
            emission.rateOverTime = active ? Mathf.Lerp(idleSmokeRate,
                vehicle.IsBoosting ? nitroSmokeRate : fullThrottleSmokeRate, smokeLoad) : 0f;
            if (active) { if (!ps.isPlaying) ps.Play(); }
            else if (ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
    private void UpdateAudio(bool skid)
    {
        if (brakeSource == null) return;
        if (!vehicle.EffectsActive) { brakeSource.Stop(); brakeSource.volume = 0f; return; }
        brakeSource.volume = Mathf.MoveTowards(brakeSource.volume, skid ? brakeVolume : 0f, Time.deltaTime * 4f);
        if (skid && brakeLoop != null && !brakeSource.isPlaying)
        { brakeSource.clip = brakeLoop; brakeSource.Play(); }
        if (!skid && brakeSource.volume <= 0.001f) brakeSource.Stop();
    }
    private void OnDisable()
    {
        if (brakeSource != null) brakeSource.Stop();
        if (linked != null) System.Array.Clear(linked, 0, linked.Length);
        if (smoke != null) foreach (var ps in smoke)
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    private void OnDestroy()
    {
        if (ownsBrakeSource && brakeSource != null) Destroy(brakeSource.gameObject);
        if (marksObject != null) Destroy(marksObject);
        if (marksMesh != null) Destroy(marksMesh);
        if (ownedSkidMaterial != null) Destroy(ownedSkidMaterial);
        if (ownedSmokeMaterial != null) Destroy(ownedSmokeMaterial);
        if (smokeTexture != null) Destroy(smokeTexture);
        if (smoke != null) foreach (var ps in smoke) if (ps != null) Destroy(ps.gameObject);
    }
}
