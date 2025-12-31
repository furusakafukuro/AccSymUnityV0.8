using UnityEngine;

public sealed class ParticleVisualBuilder : MonoBehaviour
{
    public SimulationController controller;
    public GameObject prefab;  // Sphere等
    public Transform parent;

    [ContextMenu("Build Visuals")]
    public void BuildVisuals()
    {
        if (controller == null) { Debug.LogError("controller is null"); return; }
        if (prefab == null) { Debug.LogError("prefab is null"); return; }
        if (parent == null) parent = this.transform;

        int n = Mathf.Max(1, controller.particleCount);
        controller.particleVisuals = new Transform[n];

        // 既存子を消したいならここでDestroy（編集時のみ推奨）
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            DestroyImmediate(parent.GetChild(i).gameObject);
#else
            Destroy(parent.GetChild(i).gameObject);
#endif
        }

        for (int i = 0; i < n; i++)
        {
            GameObject go = Instantiate(prefab, parent);
            go.name = $"Particle_{i:000}";
            controller.particleVisuals[i] = go.transform;
        }

        Debug.Log($"Built {n} particle visuals and assigned to controller.");
    }
}