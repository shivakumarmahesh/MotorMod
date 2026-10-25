using System.IO;
using HarmonyLib;
using UnityEngine;

namespace MotorMod.Harness
{
    /// <summary>A camera the mod owns. It copies the game camera's settings (layers, clip planes) and renders on demand into a PNG.</summary>
    internal static class HarnessCamera
    {
        private static Camera s_cam;

        public static void Shoot(string path, Vector3 pos, Vector3 lookAt, int width, int height, float fov)
        {
            var cam = Get();
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt, Vector3.up);
            cam.fieldOfView = fov;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.Destroy(tex);
            }
            finally
            {
                RenderTexture.active = prev;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static Camera Get()
        {
            if (s_cam) return s_cam;
            var go = new GameObject("MotorHarnessCamera");
            Object.DontDestroyOnLoad(go);
            s_cam = go.AddComponent<Camera>();
            var gc = GameCamera.instance;
            var src = gc ? AccessTools.Field(typeof(GameCamera), "m_camera").GetValue(gc) as Camera : null;
            if (src) s_cam.CopyFrom(src);
            s_cam.enabled = false;                 // renders only when asked
            s_cam.allowHDR = false;                // no tonemapping on this camera, so keep it in range
            s_cam.allowMSAA = true;
            s_cam.nearClipPlane = 0.1f;
            s_cam.farClipPlane = 500f;
            s_cam.targetTexture = null;
            s_cam.rect = new Rect(0f, 0f, 1f, 1f);
            return s_cam;
        }
    }
}
