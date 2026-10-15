using System.Collections.Generic;
using UnityEngine;

// Per-renderer overrides; never modifies a shared material asset.
public sealed class JourneyBoardTint
{
    private sealed class Entry { public Renderer renderer; public MaterialPropertyBlock original, current; }
    private readonly List<Entry> entries = new List<Entry>();
    private Color? last;
    private bool dirty;
    public JourneyBoardTint(Renderer[] renderers)
    {
        if (renderers == null) return;
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            var entry = new Entry { renderer = renderer, original = new MaterialPropertyBlock(), current = new MaterialPropertyBlock() };
            renderer.GetPropertyBlock(entry.original); entries.Add(entry);
        }
    }
    public void Set(Color? color)
    {
        if (dirty && last == color) return;
        dirty = true; last = color;
        foreach (var entry in entries)
        {
            if (entry.renderer == null) continue;
            if (!color.HasValue) { entry.renderer.SetPropertyBlock(entry.original); continue; }
            entry.renderer.GetPropertyBlock(entry.current);
            entry.current.SetColor("_BaseColor", color.Value);
            entry.current.SetColor("_Color", color.Value);
            entry.renderer.SetPropertyBlock(entry.current);
        }
    }
    public void Restore() { dirty = false; Set(null); }
}
