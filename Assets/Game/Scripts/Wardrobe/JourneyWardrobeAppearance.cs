using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class JourneyWardrobeAppearance : MonoBehaviour
{
    // Same order on preview and gameplay prefab variants. Hats are optional additions.
    public GameObject[] hatOptions = Array.Empty<GameObject>();
    public bool preserveAuthoredOutfitUntilChoice;
    public static event Action Changed;
    private const string Key = "JourneyOfLight.Outfit.v1";
    [Serializable] private class Outfit { public int[] values = { 1, 1, 1, 1, 0, 0, 0, 0 }; }
    private static Outfit outfit;
    private readonly Dictionary<string, GameObject> parts = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { outfit = null; Changed = null; }
    private static Outfit Current
    {
        get
        {
            if (outfit != null) return outfit;
            try { outfit = JsonUtility.FromJson<Outfit>(PlayerPrefs.GetString(Key, "")); } catch { }
            if (outfit == null || outfit.values == null || outfit.values.Length != 8) outfit = new Outfit();
            return outfit;
        }
    }
    private void Awake()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.GetComponent<Renderer>() != null) parts[t.name.Trim()] = t.gameObject;
    }
    private void OnEnable() { Changed += Apply; Apply(); }
    private void OnDisable() { Changed -= Apply; }
    public int GetSelection(int slot) => slot >= 0 && slot < 8 ? Current.values[slot] : 0;
    public int OptionCount(int slot)
    {
        switch (slot) { case 0: return 6; case 1: return 5; case 2: return 6; case 3: return 5; case 4: return 3; case 5: case 6: return 2; case 7: return 1 + hatOptions.Length; default: return 0; }
    }
    public void Step(int slot, int delta)
    {
        int count = OptionCount(slot); if (count < 2) return;
        Current.values[slot] = ((GetSelection(slot) + delta) % count + count) % count;
        // A head covering and hair/hat occupy the same space.
        if (slot == 5 && Current.values[5] != 0) Current.values[7] = 0;
        if (slot == 7 && Current.values[7] != 0) Current.values[5] = 0;
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(Current)); PlayerPrefs.Save(); Changed?.Invoke();
    }
    private void Set(string name, bool value) { if (parts.TryGetValue(name, out var go) && go != null) go.SetActive(value); }
    private void Apply()
    {
        if (preserveAuthoredOutfitUntilChoice && !PlayerPrefs.HasKey(Key)) return;
        int top = Mathf.Clamp(GetSelection(0), 0, 5), pants = Mathf.Clamp(GetSelection(1), 0, 4);
        bool dress = top == 5, scarf = GetSelection(5) != 0;
        int hat = Mathf.Clamp(GetSelection(7), 0, hatOptions.Length);
        for (int i = 1; i <= 4; i++) { Set("top" + i, top == i); Set("pants" + i, !dress && pants == i); Set("hair" + i, !scarf && hat == 0 && GetSelection(3) == i); }
        for (int i = 1; i <= 5; i++) Set("shoes" + i, GetSelection(2) == i);
        Set("dress", dress); Set("under wear", !dress && pants == 0);
        Set("glasses1", GetSelection(4) == 1); Set("glasses 2", GetSelection(4) == 2);
        Set("scarf", scarf); Set("backpack", GetSelection(6) != 0);
        Set("body", false); // Full-body mesh overlaps the segmented body in this package.
        Set("body _ Torso", top == 0); Set("body _ Pelvis", !dress); Set("body _ Leg", !dress);
        for (int i = 0; i < hatOptions.Length; i++) if (hatOptions[i] != null) hatOptions[i].SetActive(hat == i + 1);
    }
}
