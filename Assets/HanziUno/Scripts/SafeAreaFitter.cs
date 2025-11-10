using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class SafeAreaFitter : MonoBehaviour
{
    RectTransform rt;
    Rect lastApplied;
    bool warned;

    [Header("Editor Simulation")]
    public bool simulateInEditor = false;
    public Vector4 editorInsets = new Vector4(0, 60, 0, 40); // L,T,R,B px

    void OnEnable()  { rt = GetComponent<RectTransform>(); Apply(true); }
    void Update()    { Apply(false); }
    void OnRectTransformDimensionsChange() { Apply(false); }

    void Apply(bool force)
    {
        if (!rt) return;

        var sa = GetSafeAreaPixels();
        // Fallback if invalid
        if (sa.width < 2f || sa.height < 2f)
        {
            sa = new Rect(0, 0, Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
            if (!warned)
            {
                Debug.LogWarning("[SafeAreaFitter] Invalid Screen.safeArea; using full screen.");
                warned = true;
            }
        }

        if (!force && sa == lastApplied) return;
        lastApplied = sa;

        float w = Mathf.Max(1f, Screen.width);
        float h = Mathf.Max(1f, Screen.height);

        var min = new Vector2(sa.xMin / w, sa.yMin / h);
        var max = new Vector2(sa.xMax / w, sa.yMax / h);

        // Clamp strictly to [0,1]
        min.x = Mathf.Clamp01(min.x);  min.y = Mathf.Clamp01(min.y);
        max.x = Mathf.Clamp01(max.x);  max.y = Mathf.Clamp01(max.y);

        // Ensure max > min but still within [0,1]
        const float epsilon = 0.001f;
        if (max.x <= min.x) max.x = Mathf.Min(1f, min.x + epsilon);
        if (max.y <= min.y) max.y = Mathf.Min(1f, min.y + epsilon);

        // Apply anchors & zero offsets. No NaNs.
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.anchoredPosition = Vector2.zero; // belts & suspenders
    }

    Rect GetSafeAreaPixels()
    {
#if UNITY_EDITOR
        if (simulateInEditor)
        {
            float w = Mathf.Max(1f, Screen.width);
            float h = Mathf.Max(1f, Screen.height);
            float l = Mathf.Clamp(editorInsets.x, 0, w * 0.45f);
            float t = Mathf.Clamp(editorInsets.y, 0, h * 0.45f);
            float r = Mathf.Clamp(editorInsets.z, 0, w * 0.45f);
            float b = Mathf.Clamp(editorInsets.w, 0, h * 0.45f);
            return new Rect(l, b, w - l - r, h - t - b);
        }
#endif
        return Screen.safeArea;
    }
}
