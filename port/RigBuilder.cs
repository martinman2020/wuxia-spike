using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Spike 005：把 spike 004 嘅 2D 骨架拼貼演出搬去 Unity。
// 唯一輸入 = Resources/rig-data.unity.json（部件框 + 樞軸）＋ 部件 Sprite。
//
// ⚠️ 呢個檔案**未經 Unity 編譯／執行驗證**（呢部機冇 Unity：要帳號同 license，體積亦唔可行）。
//    Godot 版已經 headless 跑過並逐項驗證（見 ../README.md）；Unity 版係同一套數學，
//    照住官方 2D 做法寫，但未實跑過，第一次開就預期要微調一兩處。
//
// Unity 做法（唔一定要用 2D Animation package）：
//   GameObject 父層（樞軸）→ 子 SpriteRenderer（offset = 部件左上角）
//   旋轉父層 = 繞樞軸轉，同 CSS transform-origin / Godot Node2D pivot 一模一樣。
//
// 座標注意：rig-data.json 用「螢幕座標」（Y 向下），Unity 2D 係 Y 向上。
//   → y_unity = stageH - y_screen，而旋轉方向亦要反號（z = -angle）。
//      呢個係最容易出錯嘅一點，所以特地寫喺度。

[Serializable] public class Part {
    public string name;
    public int[] box;      // [x, y] 左上角（螢幕座標）
    public int[] size;     // [w, h]
    public int z;
    public string file;
}

[Serializable] public class Pivot { public float x; public float y; }

[Serializable] public class RigData {
    public int[] stage;                 // [W, H]
    public Part[] parts;                // 已按 z 排序
    public float[] headPivot;           // 用唔到字典，逐個寫
    public float[] torsoPivot;
    public float[] armPivot;
}

public class RigBuilder : MonoBehaviour {
    [Header("由 Resources/rig-data.unity.json 讀入，數值唔喺代碼寫死")]
    public float duration = 0.95f;      // 對應 CSS --dur: 950ms

    // CSS keyframes：時間比例 + 角度（度）
    static readonly float[] T   = { 0.00f, 0.17f, 0.34f, 0.45f, 0.62f, 1.00f };
    static readonly float[] ARM = { 0f, -27f, 15f, 15f, 6f, 0f };
    static readonly float[] TOR = { 0f, -5f, 7f, 7f, 3f, 0f };
    static readonly float[] HED = { 0f, -4f, 3f, 3f, 1f, 0f };
    static readonly float[] ROT = { 0f, -10f, 30f, 30f, 10f, 0f };   // 根節點位移（螢幕 px）

    RigData data;
    Transform root, pivotHead, pivotTorso, pivotArm;
    Vector3 rootBase;
    float t = 0f;
    bool playing = false;

    void Start() {
        var ta = Resources.Load<TextAsset>("rig-data.unity");
        if (ta == null) { Debug.LogError("找不到 Resources/rig-data.unity.json"); return; }
        data = ParseJson(ta.text);
        Build();
        Play();
    }

    void Build() {
        float W = data.stage[0], H = data.stage[1];
        root = new GameObject("root").transform;
        root.SetParent(transform, false);
        rootBase = root.localPosition;

        foreach (var p in data.parts) {                 // parts 已按 z 排序 → 次序 = 疊放次序
            float[] piv = PivotOf(p.name, W, H);        // 樞軸（螢幕座標）
            float bx = p.box[0], by = p.box[1];

            var pivot = new GameObject("pivot_" + p.name).transform;   // 樞軸 GameObject
            pivot.SetParent(root, false);
            pivot.localPosition = ToUnity(piv[0], piv[1], H);

            var go = new GameObject("sprite_" + p.name);              // 部件
            go.transform.SetParent(pivot, false);
            // 部件中心相對於樞軸（螢幕 px → Unity px，Y 反號）
            float cx = (bx + p.size[0] * 0.5f) - piv[0];
            float cy = (by + p.size[1] * 0.5f) - piv[1];
            go.transform.localPosition = new Vector3(cx / 100f, -cy / 100f, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            var tex = Resources.Load<Texture2D>("Parts/" + System.IO.Path.GetFileNameWithoutExtension(p.file));
            if (tex == null) { Debug.LogError("找不到 Resources/Parts/" + p.file); continue; }
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                      new Vector2(0.5f, 0.5f), 100f);   // 100 PPU → 同 px 1:1
            sr.sortingOrder = p.z;

            switch (p.name) {
                case "head":      pivotHead  = pivot; break;
                case "torso":     pivotTorso = pivot; break;
                case "arm_front": pivotArm   = pivot; break;
            }
        }
    }

    static Vector3 ToUnity(float x, float yScreen, float H) {
        // px → 世界單位（100 PPU），Y 由螢幕座標翻成 Unity 座標
        return new Vector3(x / 100f - 2.32f, (H - yScreen) / 100f - 4.41f, 0f);
    }

    float[] PivotOf(string name, float W, float H) {
        switch (name) {
            case "head":      return new[] { data.headPivot[0] * W, data.headPivot[1] * H };
            case "torso":     return new[] { data.torsoPivot[0] * W, data.torsoPivot[1] * H };
            case "arm_front": return new[] { data.armPivot[0] * W,  data.armPivot[1] * H };
            default:          return new[] { 0f, 0f };            // legs = 根，唔轉
        }
    }

    public void Play() { t = 0f; playing = true; }

    void Update() {
        if (!playing || data == null) return;
        t += Time.deltaTime;
        float p = Mathf.Clamp01(t / duration);
        Apply(p);
        if (p >= 1f) playing = false;
    }

    void Apply(float p) {
        // 用同一個 keyframes 表插值（CSS cubic-bezier 換成 SmoothStep，曲線略有差異）
        float arm = Sample(ARM, p), tor = Sample(TOR, p), hed = Sample(HED, p), r = Sample(ROT, p);
        SetZ(pivotArm, arm); SetZ(pivotTorso, tor); SetZ(pivotHead, hed);
        if (root != null) root.localPosition = rootBase + new Vector3(r / 100f, 0f, 0f);
    }

    void SetZ(Transform tr, float angleDeg) {
        if (tr == null) return;
        var e = tr.localEulerAngles;
        e.z = -angleDeg;      // 螢幕座標 Y 向下 + Unity Z 逆時針 → 要反號
        tr.localEulerAngles = e;
    }

    static float Sample(float[] keys, float p) {
        for (int i = 1; i < T.Length; i++) {
            if (p <= T[i]) {
                float u = (p - T[i - 1]) / (T[i] - T[i - 1]);
                u = u * u * (3f - 2f * u);                    // SmoothStep
                return Mathf.Lerp(keys[i - 1], keys[i], u);
            }
        }
        return keys[keys.Length - 1];
    }

    // rig-data.unity.json 係扁平結構，用 Unity 內建 JsonUtility 拆（唔需要 Newtonsoft）
    static RigData ParseJson(string json) {
        var d = JsonUtility.FromJson<RigData>(json);
        var pivots = ParsePivots(json);
        d.headPivot  = pivots["head"];
        d.torsoPivot = pivots["torso"];
        d.armPivot   = pivots["arm_front"];
        return d;
    }

    static Dictionary<string, float[]> ParsePivots(string json) {
        // "pivot": { "head": [0.29, 0.155], ... } —— JsonUtility 唔支援字典，手動抽
        var res = new Dictionary<string, float[]>();
        int i = json.IndexOf("\"pivot\"");
        if (i < 0) return res;
        int open = json.IndexOf('{', i), close = json.IndexOf('}', open);
        foreach (var chunk in json.Substring(open + 1, close - open - 1).Split('}')) {
            int c = chunk.IndexOf(':');
            if (c < 0) continue;
            string key = chunk.Substring(0, c).Trim().Trim('"', ',', ' ', '\n', '\r');
            int b = chunk.IndexOf('['), e = chunk.IndexOf(']');
            if (b < 0 || e < 0) continue;
            var parts = chunk.Substring(b + 1, e - b - 1).Split(',');
            res[key] = new[] {
                float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)
            };
        }
        return res;
    }
}