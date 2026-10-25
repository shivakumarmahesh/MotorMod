// Editor-only build for every bike listed in Assets/Bikes/bikes.txt and every prop in Assets/Props/props.txt (both written
// by assets/models/make_unity_textures.py):
//   Unity -batchmode -nographics -projectPath unity/MotorModUnity -executeMethod MotorModBuild.BuildAll -quit -logFile -
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class MotorModBuild
{
    const string BikesRoot = "Assets/Bikes", PropsRoot = "Assets/Props";

    static IEnumerable<(string key, string prefab)> List(string path) =>
        !File.Exists(path) ? Enumerable.Empty<(string, string)>()
        : File.ReadAllLines(path).Where(l => l.Contains(":")).Select(l => (l.Split(':')[0].Trim(), l.Split(':')[1].Trim()));
    static IEnumerable<(string key, string prefab)> Bikes() => List(BikesRoot + "/bikes.txt");
    static IEnumerable<(string key, string prefab)> Props() => List(PropsRoot + "/props.txt");

    public static void BuildAll()
    {
        foreach (var (key, prefab) in Bikes())
        {
            var fbx = $"{BikesRoot}/{key}/{key}.fbx";
            if (!File.Exists(fbx)) { Debug.LogWarning($"[MotorModBuild] {key}: no {fbx}, skipped"); continue; }
            ImportSettings($"{BikesRoot}/{key}", key);
            BuildPrefab(key, prefab);
        }
        foreach (var (key, prefab) in Props())
        {
            var folder = $"{PropsRoot}/{key}";
            if (!File.Exists($"{folder}/{key}.fbx")) { Debug.LogWarning($"[MotorModBuild] prop {key}: no fbx, skipped"); continue; }
            ImportSettings(folder, key);
            BuildPropPrefab(key, prefab);
        }
        BuildBundles();
    }

    static void ImportSettings(string folder, string key)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder + "/Audio" }))
        {
            var imp = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            imp.forceToMono = true;
            var s = imp.defaultSampleSettings; s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.8f;
            imp.defaultSampleSettings = s; imp.SaveAndReimport();
        }
        var model = (ModelImporter)AssetImporter.GetAtPath($"{folder}/{key}.fbx");
        model.materialImportMode = ModelImporterMaterialImportMode.None; model.importAnimation = false; model.isReadable = true; model.SaveAndReimport();
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder + "/Icons" }))
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single; imp.mipmapEnabled = false; imp.SaveAndReimport();
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder + "/Textures" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid); var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            bool normal = path.EndsWith("_normal.png"); bool albedo = path.EndsWith("_albedo.png");
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = albedo; imp.maxTextureSize = 1024; imp.SaveAndReimport();
        }
    }

    static Material MakeMaterial(string path, string albedo, string normal, string ms)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
        var a = AssetDatabase.LoadAssetAtPath<Texture2D>(albedo); var n = AssetDatabase.LoadAssetAtPath<Texture2D>(normal); var m = AssetDatabase.LoadAssetAtPath<Texture2D>(ms);
        mat.SetTexture("_MainTex", a);
        if (n) { mat.SetTexture("_BumpMap", n); mat.EnableKeyword("_NORMALMAP"); } else { mat.SetTexture("_BumpMap", null); mat.DisableKeyword("_NORMALMAP"); }
        if (m) { mat.SetTexture("_MetallicGlossMap", m); mat.EnableKeyword("_METALLICGLOSSMAP"); }
        EditorUtility.SetDirty(mat); return mat;
    }

    static Vector3 Vec(Match m, int i) => new Vector3(float.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[i + 1].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[i + 2].Value, CultureInfo.InvariantCulture));

    /// <summary>Root with the model as its "Visual" child, materials from slots.txt.</summary>
    static (GameObject root, GameObject visual) Assemble(string folder, string key, string prefabName)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{key}.fbx");
        var root = new GameObject(prefabName);
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        visual.name = "Visual"; visual.transform.SetParent(root.transform, false);

        var mats = new Dictionary<string, Material>();
        var slots = File.ReadAllLines(folder + "/slots.txt").Where(l => l.Contains(":")).ToDictionary(l => l.Split(':')[0], l => l.Split(':')[1].Split(','));
        foreach (var r in visual.GetComponentsInChildren<MeshRenderer>())
        {
            var k = r.gameObject == visual ? "Body" : r.gameObject.name;      // the FBX root is the Body mesh, renamed Visual
            if (!slots.TryGetValue(k, out var names)) { Debug.LogWarning($"[MotorModBuild] {key}: no slot list for {k}"); continue; }
            var arr = new Material[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var mn = names[i];
                if (!mats.TryGetValue(mn, out var mat))
                    mats[mn] = mat = MakeMaterial($"{folder}/{key}_{mn}.mat", $"{folder}/Textures/{mn}_albedo.png", $"{folder}/Textures/{mn}_normal.png", $"{folder}/Textures/{mn}_ms.png");
                arr[i] = mat;
            }
            r.sharedMaterials = arr;
        }
        return (root, visual);
    }

    /// <summary>A prop (the saddlebag): just the model with its materials; the mod adds colliders and behaviour.</summary>
    static void BuildPropPrefab(string key, string prefabName)
    {
        var folder = $"{PropsRoot}/{key}";
        var (root, _) = Assemble(folder, key, prefabName);
        PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/{prefabName}.prefab");
        Object.DestroyImmediate(root); AssetDatabase.SaveAssets();
        Debug.Log($"[MotorModBuild] prop {key}: prefab {prefabName} saved");
    }

    static void BuildPrefab(string key, string prefabName)
    {
        var folder = $"{BikesRoot}/{key}";
        var (root, visual) = Assemble(folder, key, prefabName);

        var bounds = visual.GetComponentsInChildren<MeshRenderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        float tyre = 0.36f; var seat = new Vector3(0, 0.8f, -0.6f); var gl = new Vector3(-0.35f, 1.1f, 0.3f); var gr = new Vector3(0.35f, 1.1f, 0.3f); var head = new Vector3(0, 0.9f, bounds.size.z * 0.45f); Vector3? steerBottom = null, steerTop = null;
        var layoutPath = Path.GetFullPath($"../../assets/models/{key}/unity/{key}_layout.json");
        if (File.Exists(layoutPath))
        {
            var txt = File.ReadAllText(layoutPath);
            var m = Regex.Match(txt, @"""WheelFront""[^}]*""radius_m"":\s*([0-9.]+)"); if (m.Success) tyre = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var s = Regex.Match(txt, @"""seat_unity"":\s*\[\s*([-0-9.]+),\s*([-0-9.]+),\s*([-0-9.]+)"); if (s.Success) seat = Vec(s, 1);
            var g = Regex.Match(txt, @"""grips_unity"":\s*\[\s*\[\s*([-0-9.]+),\s*([-0-9.]+),\s*([-0-9.]+)\s*\],\s*\[\s*([-0-9.]+),\s*([-0-9.]+),\s*([-0-9.]+)"); if (g.Success) { gl = Vec(g, 1); gr = Vec(g, 4); }
            var h = Regex.Match(txt, @"""headlight_unity"":\s*\[\s*([-0-9.]+),\s*([-0-9.]+),\s*([-0-9.]+)"); if (h.Success) head = Vec(h, 1);
            var sa = Regex.Match(txt, @"""steer_axis"":\s*\[\s*\[\s*([-0-9.]+),\s*([-0-9.]+),\s*([-0-9.]+)\s*\],\s*\[\s*([-0-9.]+),\s*([-0-9.]+),\s*([-0-9.]+)"); if (sa.Success) { steerBottom = Vec(sa, 1); steerTop = Vec(sa, 4); }
        }
        Debug.Log($"[MotorModBuild] {key}: size {bounds.size}; tyre {tyre}; seat {seat}; grips {gl} {gr}; headlight {head}");

        var body = root.AddComponent<CapsuleCollider>();
        body.direction = 2; body.radius = tyre; body.center = new Vector3(0, tyre, 0); body.height = bounds.size.z * 0.9f;
        // markers live under Visual so they lean with the bike; what sits on the bars (grips, lamp) under the steering part, so it
        // turns with them
        var steer = FindDeep(visual.transform, "Steer"); var bars = steer != null ? steer.gameObject : visual;
        Mark(root, visual, "SeatAttach", seat); Mark(root, bars, "GripLeft", gl); Mark(root, bars, "GripRight", gr);
        var lamp = Mark(root, bars, "Headlight", head);
        if (steer != null && steerBottom.HasValue) { Mark(root, visual, "SteerAxisBottom", steerBottom.Value); Mark(root, visual, "SteerAxisTop", steerTop.Value); }
        var light = lamp.AddComponent<Light>();
        light.type = LightType.Spot; light.spotAngle = 55f; light.range = 30f; light.intensity = 2.5f; light.color = new Color(1f, 0.93f, 0.8f); light.shadows = LightShadows.None; light.enabled = false;
        lamp.transform.localRotation = Quaternion.identity;
        // soft additive glow at the lamp (a camera-facing quad; the mod turns it toward whichever camera renders and switches it with the engine)
        var glow = GameObject.CreatePrimitive(PrimitiveType.Quad); glow.name = "Glow"; Object.DestroyImmediate(glow.GetComponent<Collider>());
        glow.transform.SetParent(lamp.transform, false); glow.transform.localScale = Vector3.one * 0.3f; glow.transform.localPosition = new Vector3(0f, 0f, 0.05f);
        var glowR = glow.GetComponent<MeshRenderer>(); glowR.sharedMaterial = GlowMaterial(); glowR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; glowR.receiveShadows = false;
        Debug.Log($"[MotorModBuild] {key}: wheels front={FindDeep(visual.transform, "WheelFront") != null} rear={FindDeep(visual.transform, "WheelRear") != null} steering={(steer != null ? "yes, axis " + steerBottom + " to " + steerTop : "none")}");

        PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/{prefabName}.prefab");
        Object.DestroyImmediate(root); AssetDatabase.SaveAssets();
        Debug.Log($"[MotorModBuild] {key}: prefab {prefabName} saved");
    }

    /// <summary>Additive material with a radial gradient: bright warm centre fading to nothing at the edge.</summary>
    static Material GlowMaterial()
    {
        var path = $"{BikesRoot}/GlowMat.mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        var texPath = $"{BikesRoot}/glow.png";
        if (!File.Exists(texPath))
        {
            const int N = 128; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                float a = Mathf.Clamp01(1f - r); a = a * a * (3f - 2f * a); a = Mathf.Pow(a, 1.7f);
                tex.SetPixel(x, y, new Color(1f, 0.93f, 0.78f, a));
            }
            tex.Apply(); File.WriteAllBytes(texPath, tex.EncodeToPNG()); AssetDatabase.ImportAsset(texPath);
            var imp = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (imp != null) { imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = false; imp.SaveAndReimport(); }
        }
        var shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Standard");
        mat = new Material(shader) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) };
        if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", new Color(1f, 0.93f, 0.78f, 0.55f));
        AssetDatabase.CreateAsset(mat, path); return mat;
    }

    static Transform FindDeep(Transform t, string name)
    {
        foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
        return null;
    }

    /// <summary>Marker placed at a root-space position, then parented where it should move with.</summary>
    static GameObject Mark(GameObject root, GameObject parent, string name, Vector3 rootLocalPos)
    {
        var go = new GameObject(name); go.transform.SetParent(root.transform, false); go.transform.localPosition = rootLocalPos; go.transform.localRotation = Quaternion.identity;
        go.transform.SetParent(parent.transform, true); return go;
    }

    static void BuildBundles()
    {
        var builds = new List<AssetBundleBuild>();
        foreach (var (key, prefab) in Props())
        {
            var p = $"{PropsRoot}/{key}/{prefab}.prefab";
            if (File.Exists(p)) builds.Add(new AssetBundleBuild { assetBundleName = key, assetNames = new[] { p } });
        }
        foreach (var (key, prefab) in Bikes())
        {
            var folder = $"{BikesRoot}/{key}"; var p = $"{folder}/{prefab}.prefab";
            if (!File.Exists(p)) continue;
            var assets = new[] { p }.Concat(AssetDatabase.FindAssets("t:AudioClip", new[] { folder + "/Audio" }).Select(AssetDatabase.GUIDToAssetPath))
                .Concat(AssetDatabase.FindAssets("t:Texture2D", new[] { folder + "/Icons" }).Select(AssetDatabase.GUIDToAssetPath)).ToArray();
            builds.Add(new AssetBundleBuild { assetBundleName = key, assetNames = assets });
        }
        var outRoot = Path.GetFullPath("../../src/MotorMod/Bundles");
        foreach (var (target, suffix) in new[] { (BuildTarget.StandaloneOSX, "osx"), (BuildTarget.StandaloneWindows64, "win") })
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target)) { Debug.LogWarning($"[MotorModBuild] no module for {target}, skipped"); continue; }
            var dir = Path.Combine(outRoot, suffix); Directory.CreateDirectory(dir);
            var manifest = BuildPipeline.BuildAssetBundles(dir, builds.ToArray(), BuildAssetBundleOptions.ChunkBasedCompression, target);
            Debug.Log($"[MotorModBuild] built {suffix}: {(manifest == null ? "FAILED" : string.Join(",", manifest.GetAllAssetBundles()))} -> {dir}");
        }
    }
}
