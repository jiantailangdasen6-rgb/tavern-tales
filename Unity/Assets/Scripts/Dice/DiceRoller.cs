#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Tavern.Unity.Dice
{
    /// <summary>
    /// A 3D twenty-sided die thrown across a tray, drawn into a 16:9 RenderTexture over the scene. The game decides the
    /// number first. Each throw is played out in a hidden physics scene (random start, real bounces and tumbling), the face
    /// it comes to rest on is read, and the die's look is turned by one of its own symmetries so that the decided number is
    /// on that face; the recorded throw is then replayed. The die really rolls, and every player sees the decided number.
    /// </summary>
    public sealed class DiceRoller : MonoBehaviour
    {
        const int Layer = 30;
        public RenderTexture Texture { get; private set; } = null!;
        /// <summary>Width / height of the picture.</summary>
        public const float Aspect = 16f / 9f;

        /// <summary>The die hit the tray (strength 0..1), for a knock sound.</summary>
        public event Action<float>? Bounced;

        Transform _body = null!, _visual = null!, _shadow = null!, _cam = null!;
        Mesh _mesh = null!;
        readonly List<(Vector3 normal, Vector3 up)> _faces = new List<(Vector3, Vector3)>();
        Coroutine? _rolling;
        Vector3 _camHome;
        Camera _camera = null!;
        /// <summary>Where the die came to rest in its picture (0..1, y up), for a light behind it.</summary>
        public Vector2 Spot { get; private set; }
        float _shake;
        int _index;

        // the hidden tray where each throw is played out first
        PhysicsScene _phys;
        Rigidbody _simDie = null!;
        const float TrayW = 12f, TrayD = 5.4f, DieScale = 1.6f, Step = 1f / 60f;

        /// <summary>index: several dice side by side (each far from the others, with its own picture and tray).</summary>
        public static DiceRoller Create(Transform parent, int index = 0)
        {
            var go = new GameObject("Dice");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(index * 300, -500, 0);   // far away from anything else (and from the other dice)
            var d = go.AddComponent<DiceRoller>();
            d._index = index;
            d.Build();
            return d;
        }

        void Build()
        {
            Texture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "DiceRT" };

            var camGo = new GameObject("DiceCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0, 10.5f, -6.8f);
            _cam = camGo.transform;
            _camHome = _cam.localPosition;
            camGo.transform.localRotation = Quaternion.LookRotation(new Vector3(0, 0, 0.5f) - camGo.transform.localPosition);
            var cam = camGo.AddComponent<Camera>();
            _camera = cam;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << Layer;
            cam.fieldOfView = 44;   // room for a die that rolls to the walls
            cam.targetTexture = Texture;
            camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;

            // directional lights reach every die on the layer: only the first die brings them
            if (_index == 0)
            {
                var lightGo = new GameObject("DiceLight");
                lightGo.transform.SetParent(transform.parent, false);   // stays on while this die is hidden
                lightGo.transform.localRotation = Quaternion.Euler(55, -30, 0);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.6f; light.color = new Color(1f, 0.9f, 0.75f);
                light.cullingMask = 1 << Layer;
                var fillGo = new GameObject("DiceFill");
                fillGo.transform.SetParent(transform.parent, false);
                fillGo.transform.localRotation = Quaternion.Euler(20, 150, 0);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = 0.5f; fill.color = new Color(0.55f, 0.6f, 0.8f);
                fill.cullingMask = 1 << Layer;
            }

            _mesh = BuildMesh();
            _body = new GameObject("D20") { layer = Layer }.transform;
            _body.SetParent(transform, false);
            var visual = new GameObject("Look") { layer = Layer };
            visual.transform.SetParent(_body, false);
            visual.transform.localScale = Vector3.one * DieScale;
            visual.AddComponent<MeshFilter>().sharedMesh = _mesh;
            // The material is an asset made by ProjectSetup (so the URP Lit shader is in builds); the numbers are baked into its texture.
            var mat = new Material(Resources.Load<Material>("Materials/DiceBody"));
            mat.SetTexture("_BaseMap", BakeNumbers());
            mat.SetColor("_BaseColor", Color.white);
            visual.AddComponent<MeshRenderer>().sharedMaterial = mat;
            _visual = visual.transform;

            _shadow = BuildShadow();
            BuildTray();
            gameObject.SetActive(false);
        }

        /// <summary>A soft round shadow on the tray floor under the die (the picture has no floor of its own).</summary>
        Transform BuildShadow()
        {
            var go = new GameObject("Shadow") { layer = Layer };
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var mesh = new Mesh { name = "shadow" };
            mesh.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var r = new Vector2(x - size / 2f + 0.5f, y - size / 2f + 0.5f).magnitude / (size / 2f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1 - r) * Mathf.Clamp01(1 - r)));
                }
            tex.Apply();
            var mat = new Material(Resources.Load<Material>("Materials/DiceShadow"));
            mat.SetTexture("_BaseMap", tex);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        /// <summary>The hidden physics scene: a floor, four low walls and a die of the same shape.</summary>
        void BuildTray()
        {
            var scene = SceneManager.CreateScene("DiceTray" + (_index == 0 ? "" : _index.ToString()), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _phys = scene.GetPhysicsScene();
            var felt = new PhysicsMaterial("tray") { bounciness = 0.42f, dynamicFriction = 0.2f, staticFriction = 0.32f, bounceCombine = PhysicsMaterialCombine.Average };
            void Box(Vector3 centre, Vector3 size)
            {
                var b = new GameObject("TrayPart");
                var c = b.AddComponent<BoxCollider>();
                c.center = centre; c.size = size; c.sharedMaterial = felt;
                SceneManager.MoveGameObjectToScene(b, scene);
            }
            Box(new Vector3(0, -0.5f, 0), new Vector3(TrayW + 4, 1, TrayD + 4));
            Box(new Vector3(-(TrayW / 2 + 0.5f), 3, 0), new Vector3(1, 8, TrayD + 4));
            Box(new Vector3(TrayW / 2 + 0.5f, 3, 0), new Vector3(1, 8, TrayD + 4));
            Box(new Vector3(0, 3, -(TrayD / 2 + 0.5f)), new Vector3(TrayW + 4, 8, 1));
            Box(new Vector3(0, 3, TrayD / 2 + 0.5f), new Vector3(TrayW + 4, 8, 1));

            var die = new GameObject("SimDie");
            die.transform.localScale = Vector3.one * DieScale;
            var mc = die.AddComponent<MeshCollider>();
            mc.sharedMesh = _mesh; mc.convex = true; mc.sharedMaterial = felt;
            _simDie = die.AddComponent<Rigidbody>();
            _simDie.mass = 1; _simDie.linearDamping = 0.03f; _simDie.angularDamping = 0.08f;   // a slick tray: the die keeps rolling (owner 2026-10-25)
            _simDie.maxAngularVelocity = 60;   // the default (7 rad/s) cut every spin short
            SceneManager.MoveGameObjectToScene(die, scene);
        }

        const int AtlasCols = 5, AtlasRows = 4, Cell = 256;
        const int BakeLayer = 29;

        /// <summary>
        /// Draws the numbers 1..20 (ivory cells, dark numbers, 1 and 20 in red) with a throwaway camera and reads them
        /// into a texture, so the numbers are part of the die's surface.
        /// </summary>
        Texture2D BakeNumbers()
        {
            var w = AtlasCols * Cell; var h = AtlasRows * Cell;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var root = new GameObject("Bake");
            root.transform.position = new Vector3(0, 1000, 0);
            var camGo = new GameObject("BakeCam");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(w / 2f / Cell, -h / 2f / Cell, -10);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = h / 2f / Cell;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.93f, 0.86f, 0.68f);
            cam.cullingMask = 1 << BakeLayer; cam.targetTexture = rt; cam.enabled = false;
            camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            for (int i = 0; i < 20; i++)
            {
                var label = new GameObject("n" + (i + 1)) { layer = BakeLayer };
                label.transform.SetParent(root.transform, false);
                label.transform.localPosition = new Vector3(i % AtlasCols + 0.5f, -(i / AtlasCols + 0.5f) + 0.04f, 0);
                var tm = label.AddComponent<TextMesh>();
                tm.text = (i + 1) == 6 || (i + 1) == 9 ? (i + 1) + "." : (i + 1).ToString();   // a dot tells 6 from 9
                tm.font = Theme.Serif;
                tm.fontSize = 128; tm.characterSize = 0.036f; tm.fontStyle = FontStyle.Bold;
                tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
                tm.color = i == 19 || i == 0 ? new Color(0.55f, 0.07f, 0.03f) : new Color(0.17f, 0.1f, 0.04f);
                label.GetComponent<MeshRenderer>().sharedMaterial = Theme.Serif.material;
            }
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "D20Numbers", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(true, true);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(root);
            return tex;
        }

        /// <summary>The icosahedron with each face's number cell mapped onto it; fills _faces (normal, "up" towards vertex a).</summary>
        Mesh BuildMesh()
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
            var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
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

                // Map the face onto its number's cell of the atlas: "up" points to vertex a, "right" is as seen from outside.
                var right = Vector3.Cross(up, -n).normalized;
                var radius = (a - centre).magnitude;
                var cell = new Vector2((i % AtlasCols + 0.5f) / AtlasCols, 1 - (i / AtlasCols + 0.5f) / AtlasRows);
                var half = new Vector2(0.5f / AtlasCols, 0.5f / AtlasRows) * 0.98f;
                foreach (var corner in new[] { a, b, c })
                {
                    var d = corner - centre;
                    uvs.Add(cell + new Vector2(Vector3.Dot(d, right) / radius * half.x, Vector3.Dot(d, up) / radius * half.y));
                }
            }
            var mesh = new Mesh { name = "d20" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.SetUVs(0, uvs); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        struct Throw
        {
            public List<(Vector3 pos, Quaternion rot)> Frames;
            public List<(float time, float strength)> Bounces;
            public Quaternion Look;
            public int Face;
        }

        /// <summary>Plays a random throw out in the hidden tray and turns the die's look so that face (1..20) ends on top.</summary>
        Throw PlayOut(int face)
        {
            var rb = _simDie;
            var start = new Vector3(-TrayW / 2 + 1.4f, UnityEngine.Random.Range(2.0f, 2.8f), UnityEngine.Random.Range(-1.4f, 1.4f));
            var rot = UnityEngine.Random.rotation;
            rb.transform.SetPositionAndRotation(start, rot);
            rb.position = start; rb.rotation = rot;
            rb.linearVelocity = new Vector3(UnityEngine.Random.Range(11f, 14f), UnityEngine.Random.Range(0.5f, 2f), UnityEngine.Random.Range(-2.6f, 2.6f));   // tossed a little, not slid
            rb.angularVelocity = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(26f, 38f);
            rb.WakeUp();

            var t = new Throw { Frames = new List<(Vector3, Quaternion)>(), Bounces = new List<(float, float)>() };
            float prevVy = 0;
            int still = 0;
            for (int i = 0; i < 60 * 8; i++)
            {
                _phys.Simulate(Step);
                t.Frames.Add((rb.position, rb.rotation));
                var vy = rb.linearVelocity.y;
                if (prevVy < -1.2f && vy > prevVy + 1.2f) t.Bounces.Add((i * Step, Mathf.Clamp01(-prevVy / 9f)));
                prevVy = vy;
                still = rb.linearVelocity.sqrMagnitude < 0.0025f && rb.angularVelocity.sqrMagnitude < 0.0025f ? still + 1 : 0;
                if (still > 12) break;
            }

            // the face that came to rest on top, and the readable way round for the camera (the number's top away from it)
            var final = t.Frames[t.Frames.Count - 1].rot;
            int top = 0;
            for (int i = 1; i < _faces.Count; i++) if ((final * _faces[i].normal).y > (final * _faces[top].normal).y) top = i;
            var nTop = _faces[top].normal;
            var best = _faces[top].up;
            foreach (var angle in new[] { 120f, 240f })
            {
                var w = Quaternion.AngleAxis(angle, nTop) * _faces[top].up;
                if ((final * w).z > (final * best).z) best = w;
            }
            var (nFace, upFace) = _faces[face - 1];
            // a symmetry of the icosahedron: carries the decided face (and its number's top) onto the face that lands on top
            t.Look = Quaternion.LookRotation(nTop, best) * Quaternion.Inverse(Quaternion.LookRotation(nFace, upFace));
            t.Face = face;
            if (Debug.isDebugBuild || Tavern.Unity.App.AppMain.Flag("-dicelog"))
            {
                var bodyEnd = t.Frames[t.Frames.Count - 1].rot;
                int shownTop = 0;
                for (int i = 1; i < _faces.Count; i++) if ((bodyEnd * (t.Look * _faces[i].normal)).y > (bodyEnd * (t.Look * _faces[shownTop].normal)).y) shownTop = i;
                Debug.Log($"[Dice] wanted {face}, landed on {top + 1} (normal y {(final * nTop).y:0.00}), after the turn {shownTop + 1}, frames {t.Frames.Count}");
            }
            return t;
        }

        /// <summary>Throws the die across the tray and lets it settle on face (1..20); duration is the wanted playback length.</summary>
        public void Roll(int face, float duration, Action? done = null)
        {
            gameObject.SetActive(true);
            if (_rolling != null) StopCoroutine(_rolling);
            _rolling = StartCoroutine(RollRoutine(PlayOut(Mathf.Clamp(face, 1, 20)), duration, done));
        }

        public void Hide() { if (_rolling != null) StopCoroutine(_rolling); _cam.localPosition = _camHome; gameObject.SetActive(false); }

        IEnumerator RollRoutine(Throw t, float duration, Action? done)
        {
            _visual.localRotation = t.Look;
            var length = t.Frames.Count * Step;
            var speed = Mathf.Max(1f, length / Mathf.Max(0.3f, duration));   // real time, only very long throws a little faster
            float time = 0;
            int bounce = 0;
            while (true)
            {
                time += Time.deltaTime * speed;
                var at = Mathf.Min(time / Step, t.Frames.Count - 1);
                var i = Mathf.FloorToInt(at);
                var (p0, r0) = t.Frames[i];
                var (p1, r1) = t.Frames[Mathf.Min(i + 1, t.Frames.Count - 1)];
                var k = at - i;
                Place(Vector3.Lerp(p0, p1, k), Quaternion.Slerp(r0, r1, k));
                while (bounce < t.Bounces.Count && t.Bounces[bounce].time <= time) { var s = t.Bounces[bounce++].strength; Bounced?.Invoke(s); _shake = Mathf.Max(_shake, s); }
                // a hard landing jolts the view a little
                _shake = Mathf.Max(0, _shake - Time.deltaTime * 5);
                _cam.localPosition = _camHome + (_shake > 0.3f ? UnityEngine.Random.insideUnitSphere * 0.09f * _shake : Vector3.zero);
                if (time >= length) break;
                yield return null;
            }
            _cam.localPosition = _camHome;
            yield return Present(t);
            _rolling = null;
            done?.Invoke();
        }

        /// <summary>
        /// After it settles the die rises a little and turns the rolled face to the camera, number upright: seen from above at an
        /// angle, a side face looked bigger than the top one and could be misread.
        /// </summary>
        IEnumerator Present(Throw t)
        {
            var from = _body.localRotation;
            var pos = _body.localPosition;
            var (n, up) = _faces[t.Face - 1];
            var normal = from * (t.Look * n);
            var numberUp = from * (t.Look * up);
            var toCam = (_cam.localPosition - pos).normalized;
            var turn = Quaternion.FromToRotation(normal, toCam);
            var screenUp = Vector3.ProjectOnPlane(_cam.localRotation * Vector3.up, toCam);
            var twist = Vector3.SignedAngle(Vector3.ProjectOnPlane(turn * numberUp, toCam), screenUp, toCam);
            var to = Quaternion.AngleAxis(twist, toCam) * turn * from;
            for (float time = 0; time < 1; time += Time.deltaTime / 0.45f)
            {
                var k = Mathf.SmoothStep(0, 1, time);
                _body.localRotation = Quaternion.Slerp(from, to, k);
                _body.localPosition = pos + Vector3.up * (0.5f * k);
                yield return null;
            }
            _body.localRotation = to;
            _body.localPosition = pos + Vector3.up * 0.5f;
            Spot = _camera.WorldToViewportPoint(_body.position);
        }

        void Place(Vector3 pos, Quaternion rot)
        {
            _body.localPosition = pos;
            _body.localRotation = rot;
            // the shadow stays on the floor, smaller and fainter the higher the die is
            var lift = Mathf.Clamp01((pos.y - DieScale * 0.8f) / 3f);
            _shadow.localPosition = new Vector3(pos.x + 0.25f, 0.01f, pos.z + 0.2f);
            _shadow.localScale = Vector3.one * DieScale * (2.4f - lift);
        }
    }
}
