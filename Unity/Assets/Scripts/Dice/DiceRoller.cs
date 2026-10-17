#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Tavern.Unity.Dice
{
    /// <summary>
    /// A 3D twenty-sided die drawn into a RenderTexture (shown by a RawImage over the scene). The game decides the
    /// number first; the die tumbles and settles on that face so every player sees the same roll.
    /// </summary>
    public sealed class DiceRoller : MonoBehaviour
    {
        const int Layer = 30;
        public RenderTexture Texture { get; private set; } = null!;
        Transform _die = null!;
        readonly List<(Vector3 normal, Vector3 up)> _faces = new List<(Vector3, Vector3)>();
        Coroutine? _rolling;

        public static DiceRoller Create(Transform parent)
        {
            var go = new GameObject("Dice");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0, -500, 0);   // far away from anything else
            var d = go.AddComponent<DiceRoller>();
            d.Build();
            return d;
        }

        void Build()
        {
            Texture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "DiceRT" };

            var camGo = new GameObject("DiceCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0, 0, -6);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << Layer;
            cam.fieldOfView = 30;
            cam.targetTexture = Texture;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;

            var lightGo = new GameObject("DiceLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localRotation = Quaternion.Euler(35, -30, 0);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.6f; light.color = new Color(1f, 0.9f, 0.75f);
            light.cullingMask = 1 << Layer;
            var fillGo = new GameObject("DiceFill");
            fillGo.transform.SetParent(transform, false);
            fillGo.transform.localRotation = Quaternion.Euler(-20, 150, 0);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 0.5f; fill.color = new Color(0.55f, 0.6f, 0.8f);
            fill.cullingMask = 1 << Layer;

            _die = BuildDie().transform;
            _die.SetParent(transform, false);
            gameObject.SetActive(false);
        }

        GameObject BuildDie()
        {
            float p = (1 + Mathf.Sqrt(5)) / 2;
            var v = new[]
            {
                new Vector3(-1, p, 0), new Vector3(1, p, 0), new Vector3(-1, -p, 0), new Vector3(1, -p, 0),
                new Vector3(0, -1, p), new Vector3(0, 1, p), new Vector3(0, -1, -p), new Vector3(0, 1, -p),
                new Vector3(p, 0, -1), new Vector3(p, 0, 1), new Vector3(-p, 0, -1), new Vector3(-p, 0, 1),
            };
            for (int i = 0; i < v.Length; i++) v[i] = v[i].normalized;
            int[] f =
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
            };
            var verts = new List<Vector3>(); var tris = new List<int>();
            var go = new GameObject("D20") { layer = Layer };
            for (int i = 0; i < 20; i++)
            {
                var a = v[f[i * 3]]; var b = v[f[i * 3 + 1]]; var c = v[f[i * 3 + 2]];
                var n = Vector3.Cross(b - a, c - a).normalized;
                var centre = (a + b + c) / 3f;
                if (Vector3.Dot(n, centre) < 0) { (b, c) = (c, b); n = -n; }
                int k = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
                var up = (a - centre).normalized;
                _faces.Add((n, up));

                // The number, slightly above the face, reading outward.
                var label = new GameObject("n" + (i + 1)) { layer = Layer };
                label.transform.SetParent(go.transform, false);
                label.transform.localPosition = centre + n * 0.012f;
                label.transform.localRotation = Quaternion.LookRotation(-n, up);
                var tm = label.AddComponent<TextMesh>();
                tm.text = (i + 1).ToString();
                tm.font = Theme.Serif;
                tm.fontSize = 96; tm.characterSize = 0.034f; tm.fontStyle = FontStyle.Bold;
                tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
                tm.color = i == 19 || i == 0 ? new Color(0.5f, 0.06f, 0.03f) : new Color(0.16f, 0.09f, 0.03f);
                label.GetComponent<MeshRenderer>().sharedMaterial = Theme.Serif.material;
            }
            var mesh = new Mesh { name = "d20" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            // The material is an asset made by ProjectSetup, so the URP Lit shader is included in builds.
            mr.sharedMaterial = Resources.Load<Material>("Materials/DiceBody");
            go.transform.localScale = Vector3.one * 1.25f;
            return go;
        }

        /// <summary>Tumbles the die and settles on face (1..20). duration in seconds.</summary>
        public void Roll(int face, float duration, Action? done = null)
        {
            gameObject.SetActive(true);
            if (_rolling != null) StopCoroutine(_rolling);
            _rolling = StartCoroutine(RollRoutine(Mathf.Clamp(face, 1, 20), duration, done));
        }

        public void Hide() { if (_rolling != null) StopCoroutine(_rolling); gameObject.SetActive(false); }

        IEnumerator RollRoutine(int face, float duration, Action? done)
        {
            var (n, up) = _faces[face - 1];
            var final = Quaternion.LookRotation(Vector3.back, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(n, up));
            var axis = UnityEngine.Random.onUnitSphere;
            var spin = UnityEngine.Random.Range(900f, 1300f);
            var start = UnityEngine.Random.rotation;
            float t = 0;
            float tumble = duration * 0.75f;
            while (t < duration)
            {
                t += Time.deltaTime;
                if (t < tumble)
                {
                    var k = t / tumble;
                    var speed = spin * (1 - k) * (1 - k);
                    _die.localRotation = Quaternion.AngleAxis(speed * Time.deltaTime, axis) * _die.localRotation;
                    if (t == Time.deltaTime) _die.localRotation = start;
                    _die.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(k * Mathf.PI * 3)) * 0.35f * (1 - k), 0);
                }
                else
                {
                    var k = Mathf.SmoothStep(0, 1, (t - tumble) / (duration - tumble));
                    _die.localRotation = Quaternion.Slerp(_die.localRotation, final, k);
                    _die.localPosition = Vector3.Lerp(_die.localPosition, Vector3.zero, k);
                }
                yield return null;
            }
            _die.localRotation = final;
            _die.localPosition = Vector3.zero;
            _rolling = null;
            done?.Invoke();
        }
    }
}
