#nullable enable
using System.Linq;
using System.IO;
using Tavern.Unity.App;
using Tavern.Unity.Boot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Tavern.Unity.Editor
{
    /// <summary>
    /// Idempotent project setup, run from batch mode (never by hand in the editor):
    ///   Unity.exe -batchmode -nographics -quit -projectPath Unity -executeMethod Tavern.Unity.Editor.ProjectSetup.Apply
    /// Player settings, the URP asset and the main scene are all made here so diffs stay readable.
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        const string UrpPath = "Assets/Settings/URP.asset";
        const string RendererPath = "Assets/Settings/URP_Renderer.asset";

        public static void Apply()
        {
            PlayerSettings.companyName = "tavern-tales";
            PlayerSettings.productName = "酒場の語り部";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;   // the host must keep the game going while alt-tabbed to Discord
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            SetupUrp();
            SetupMaterials();
            SetupArtImport();
            SetupAudioImport();
            SetupScene();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] done");
        }

        static void SetupUrp()
        {
            Directory.CreateDirectory("Assets/Settings");
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpPath);
            if (asset == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, UrpPath);
            }
            GraphicsSettings.defaultRenderPipeline = asset;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
        }

        /// <summary>Materials made from code at runtime would lose their shader in a build; make them assets here.</summary>
        static void SetupMaterials()
        {
            Directory.CreateDirectory("Assets/Resources/Materials");
            const string path = "Assets/Resources/Materials/DiceBody.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", new Color(0.93f, 0.85f, 0.66f));
            mat.SetFloat("_Smoothness", 0.55f);
            EditorUtility.SetDirty(mat);

            // the soft shadow under the rolling die: URP Unlit, transparent (kept as an asset so the shader is in builds)
            const string shadowPath = "Assets/Resources/Materials/DiceShadow.mat";
            var shadow = AssetDatabase.LoadAssetAtPath<Material>(shadowPath);
            if (shadow == null)
            {
                shadow = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(shadow, shadowPath);
            }
            shadow.SetFloat("_Surface", 1); shadow.SetFloat("_Blend", 0);
            shadow.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            shadow.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            shadow.SetFloat("_ZWrite", 0); shadow.SetFloat("_Cull", 0);
            shadow.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            shadow.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            shadow.SetColor("_BaseColor", new Color(0, 0, 0, 0.55f));
            EditorUtility.SetDirty(shadow);
        }

        /// <summary>Pictures in Resources/Art: no mipmaps blur, sensible sizes, compressed.</summary>
        static void SetupArtImport()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Art")) return;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Art" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue;
                var max = Path.GetFileName(p).StartsWith("bg_") ? 2048 : 1024;
                if (ti.maxTextureSize == max && !ti.mipmapEnabled && ti.textureCompression == TextureImporterCompression.CompressedHQ) continue;
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = max;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
        }

        /// <summary>
        /// Audio: the synthesized clips (Resources/Audio) and the free tracks and sounds (Resources/Music, Resources/Sfx).
        /// Music and long ambience stream, effects stay in memory; all Vorbis.
        /// </summary>
        static void SetupAudioImport()
        {
            var folders = new[] { "Assets/Resources/Audio", "Assets/Resources/Music", "Assets/Resources/Sfx" }.Where(AssetDatabase.IsValidFolder).ToArray();
            if (folders.Length == 0) return;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", folders))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(p) is AudioImporter ai)) continue;
                var music = p.Contains("/bgm/") || p.Contains("/Music/") || p.Contains("ambience");
                var s = ai.defaultSampleSettings;
                var load = music ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
                if (s.loadType == load && s.compressionFormat == AudioCompressionFormat.Vorbis) continue;
                s.loadType = load; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = music ? 0.5f : 0.7f;
                ai.defaultSampleSettings = s;
                ai.SaveAndReimport();
            }
        }

        static void SetupScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera));
            cam.tag = "MainCamera";
            var c = cam.GetComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color32(0x14, 0x10, 0x0c, 0xff);
            c.cullingMask = ~(1 << 30);   // layer 30 is the dice, drawn by its own camera
            cam.transform.position = new Vector3(0, 0, -10);
            new GameObject("App", typeof(AppMain), typeof(DevShot));
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
    }
}
