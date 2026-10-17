#nullable enable
using System.IO;
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

        static void SetupScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera));
            cam.tag = "MainCamera";
            var c = cam.GetComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color32(0x14, 0x10, 0x0c, 0xff);
            cam.transform.position = new Vector3(0, 0, -10);
            new GameObject("Boot", typeof(BootView), typeof(DevShot));
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
    }
}
