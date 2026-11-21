using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace ModChecker
{
    internal static class Ease
    {
        public static float OutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        public static float OutCubic(float t) { t = Mathf.Clamp01(t); float u = 1f - t; return 1f - u * u * u; }
        public static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    }

    // ===================================================================== input

    /// <summary>Direct XR input so it works regardless of the game's input wrapper.</summary>
    internal static class Inp
    {
        private static InputDevice left, right;
        private static object poller;
        private static bool pY, pX, pT, pB;
        public static bool YDown, XDown, TrigDown, BDown;
        public static float Trig, GripL, GripR;

        public static float Grip(bool leftHand) { return leftHand ? GripL : GripR; }

        /// <param name="pointerRight">true = the right hand points / selects (menu on the left palm), false = the left hand does.</param>
        public static void Poll(bool pointerRight)
        {
            if (!left.isValid) left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!right.isValid) right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            bool y = false, x = false, bb = false;
            float tl = 0f, tr = 0f, gl = 0f, gr = 0f;
            if (left.isValid)
            {
                left.TryGetFeatureValue(CommonUsages.secondaryButton, out y);   // Y
                left.TryGetFeatureValue(CommonUsages.primaryButton, out x);     // X
                left.TryGetFeatureValue(CommonUsages.trigger, out tl);
                left.TryGetFeatureValue(CommonUsages.grip, out gl);
            }
            if (right.isValid)
            {
                right.TryGetFeatureValue(CommonUsages.secondaryButton, out bb);   // B
                right.TryGetFeatureValue(CommonUsages.trigger, out tr);
                right.TryGetFeatureValue(CommonUsages.grip, out gr);
            }

            // The game's own poller is what Gorilla Tag itself uses, so OR it in as a second source.
            if (R.Dead(poller)) poller = R.Get(R.FindType("ControllerInputPoller"), "instance", "Instance");
            if (!R.Dead(poller))
            {
                object o;
                o = R.Get(poller, "leftControllerSecondaryButton"); if (o is bool && (bool)o) y = true;
                o = R.Get(poller, "leftControllerPrimaryButton"); if (o is bool && (bool)o) x = true;
                o = R.Get(poller, "rightControllerSecondaryButton"); if (o is bool && (bool)o) bb = true;
                o = R.Get(poller, "rightControllerIndexFloat"); if (o is float && (float)o > tr) tr = (float)o;
                o = R.Get(poller, "leftControllerIndexFloat"); if (o is float && (float)o > tl) tl = (float)o;
                o = R.Get(poller, "rightControllerGripFloat"); if (o is float && (float)o > gr) gr = (float)o;
                o = R.Get(poller, "leftControllerGripFloat"); if (o is float && (float)o > gl) gl = (float)o;
            }
            float t = pointerRight ? tr : tl;

            bool key = false;
            try { key = Input.GetKeyDown(KeyCode.F9); } catch { }   // desktop testing: F9 toggles the menu

            bool trig = pT ? t > 0.35f : t > 0.65f;
            YDown = (y && !pY) || key;
            XDown = x && !pX;
            BDown = bb && !pB;
            TrigDown = trig && !pT;
            Trig = t;
            GripL = gl; GripR = gr;
            pY = y; pX = x; pT = trig; pB = bb;
        }

        public static void Haptic(bool leftHand, float amp, float dur)
        {
            var d = leftHand ? left : right;
            if (d.isValid) d.SendHapticImpulse(0, amp, dur);
        }
    }

    // ===================================================================== sound


    internal static class Snd
    {
        private static AudioSource Source;
        private static AudioClip ClickClip;

        private const string ClickData =
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AQABAAEAAQABAAEAAAAAAAAAAAD/////////////AAAAAAAAAAAAAAAAAAAAAAAAAQAAAAEAAAABAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAD/////////////AAAAAAAAAQAAAAEAAAABAAAAAAABAAEAAAAAAAEAAAABAAAAAQAAAAEAAAAAAAAA////////AAAAAAEA" +
            "AAABAAAAAAD//wAAAAAAAAEAAQACAAAAAgAAAAEA//8AAP///v//////AAD//wEA//8AAAAAAQD//wIAAAAAAAAA//8BAP7/AgD/" +
            "/wEA/v8BAPz/AgABAPv/BgD7////AQD9/wEAAgD9/wMA/P8DAP///v8IAPj/AwD+/wAA9v8EAAIA9/8GAPv/BQD+/wUA/f8DAAMA" +
            "+f8GAP//+/8EAAQA+v8EAAEA/v/2/wUAAQD6/wQA/v8BAPz/BQD9/wQA/f/9/wgA+f8BAAMA//8CAAIAAQAGAAAABAADAAMA/v8A" +
            "AP//+f8DAPj/AgD+//z/AAD+/wAA+v8DAP3/AQAAAP3/AgAAAAEA+v8GAAEA+/8GAP//AgD7/wQAAwD5/wUAAAD9////AgD+/wIA" +
            "/v8BAP7/+P8DAAAACwARABoAFwAdABgACgD7/9n/zP+k/4X/e/+e/93/HwBwALMA9AAiAUABDgFpADz/lv3a+136dPlI+SD6+vum" +
            "/goCywWrCUYNRBCEEuETORQ3E94Q/AyDB5sAaPi070/nCOAu2kfWhtTI1FrX1ts14kzqufNk/vsJDBbvIeQs0TXtO4g+HD3DN7ku" +
            "oiKBFGEFbvbM6HjdOdWI0KnPYdJj2BrhkusK94wCQQ1SFjkdmyEqIw4ibh7IGHQRNAm0AEn43PCX6unlE+P34a7i5eRm6MXsu/HP" +
            "9qr7DQCmA2QGFgjXCLcIywdUBoIEegKaAOb+iv25/Fv8nPw4/Vb+wP9IAfUCgATuBQsH2wdaCG0IIwiZB8cGwAW1BJEDgAKRAcMA" +
            "MQDF/5b/kf+s//T/QgCTAOAALwFTAWgBaQEzAQUBrQBKAOL/cv8L/6/+Z/4o/gz++f39/RX+Pf5s/qP+1v4M/0D/XP9+/4//lP+L" +
            "/4b/cv9h/07/Nf8w/xv/Jf8g/yv/Ov9L/27/ef+j/7H/z//l/+v/BgD8/wgABQD///z/8v/u/+P/4f/h/9v/1//l/+n/5v/x/wQA" +
            "AQAKABoAEwAhABsAHwAfABkAFwATABcAAgAKAAgA//8DAPz/CQD+/wAAEAAHAAwAEAASABAAEgAdAA4ADwAaAAkADQASAP7/EAAK" +
            "AP7/DgD7/woABgD//wwAAQAPAAgACAAKABAADgABABQABgAFAA4ABQAFAAcABgADAAMABwAFAP//CgACAAAACwD+/wgACQD+/w0A" +
            "BgD//wkABQABAAMABwACAAIABAAFAAUAAwADAAIABgADAAQAAQAGAP3/AAALAPn/AgAIAPv/AgAKAPn/BAAEAP3/AgABAAMA/P8H" +
            "AAAAAgD//wEACAD+/wMAAAAIAPv/AQAJAPr/BAADAPz/AAAIAP3//f8DAAEABAD9/wEABQAAAP//BgAEAP7/AQADAP///v8FAPz/" +
            "//8CAP7/BgD+/wEAAwABAP//BwD+/wAACAD5/wcA+f8CAAcA9/8EAP3/AwAAAPf/CQD7//n/DAD1/wUA///7/wsA9P8HAAIA+f8H" +
            "AAAA/P8BAAEA/////wEA/f8CAAUA+f8HAP3//f8EAP7//v8AAAMA+v8DAP//AAABAAAAAAD//wMA///+/wMA///8/woA+P8CAAQA" +
            "+/8EAP3/BQD6////BAD9////AgD7/wMAAgD6/wcA+/8DAP7/AgACAPv/BgD8/wIA/f8EAP3/AQAAAP3/CAD1/wcA/v/8/wYA+/8E" +
            "APz/AQACAPz/BAD///3/AwD//wAAAAAAAAEA/f8GAPz/AQADAPr/BwD9//7/AQADAAAA+f8HAPv/BAABAPn/BwD9//7/AQABAP3/" +
            "AQAAAP3/BgD7/wEAAgD8/wQAAAD8/wMAAgD8/wMA/f/+/wcA+/8AAAAA//8BAP3/CAD3/wIABAD6/wYA+f8CAAAAAQAAAP7/AgD9" +
            "/wMA//8DAPz/AgACAPv/AwD//wAAAQD+/wAABAD6/wMA///9/wYA/P8CAPv/AwAEAPv/AgAAAAEA/v8BAAAA/f8GAPr/AwACAPn/" +
            "BwD7/wIA//8AAAEA//8DAPv/CAD5/wMAAgD7/wYA/P8CAP//AQD//wEAAgD9/wAABAD+/wEAAAAAAAQA+f8JAP3/+/8HAP7//v8A" +
            "AAEA//8BAP//AQD//wAAAAAAAAAA//8BAP//AQD//wEA//8BAAAAAAAAAAAAAQD//wEA//8BAP//AQAAAAAAAAAAAAAAAAAAAP//" +
            "AAD//wEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";

        public static void Init(GameObject Host)
        {
            Source = Host.GetComponent<AudioSource>();

            if (Source == null)
                Source = Host.AddComponent<AudioSource>();

            Source.playOnAwake = false;
            Source.loop = false;
            Source.spatialBlend = 0f;
            Source.volume = 1f;
            Source.pitch = 1f;

            byte[] Data = Convert.FromBase64String(ClickData);
            int SampleCount = Data.Length / 2;
            float[] Samples = new float[SampleCount];

            for (int i = 0; i < SampleCount; i++)
            {
                short Sample = BitConverter.ToInt16(Data, i * 2);
                Samples[i] = Sample / 32768f;
            }

            ClickClip = AudioClip.Create("ModCheckerClick", SampleCount, 1, 44100, false);
            ClickClip.SetData(Samples, 0);
        }

        private static void Play(float Volume)
        {
            if (Source == null || ClickClip == null)
                return;

            Source.PlayOneShot(ClickClip, Volume);
        }

        public static void Tick()
        {
            Play(1f);
        }

        public static void Soft()
        {
            Play(0.65f);
        }

        public static void Select()
        {
            Play(1f);
        }

        public static void Error()
        {
            Play(0.8f);
        }
    }

    // ===================================================================== rounded slab mesh

    /// <summary>
    /// A real 3D rounded-corner slab (bevelled edges). Lighting is baked into vertex colours so the
    /// tablet looks solid under any map lighting and renders with a plain unlit shader.
    /// Front face points toward -Z (the canvas sits on it and faces the same way).
    /// </summary>
    internal class Slab
    {
        public Mesh Mesh;
        private Vector3[] nrm;
        private Color32[] col;

        public static Slab Build(float w, float h, float t, float radius, float bevel)
        {
            const int arc = 6, bs = 3;
            int N = 4 * (arc + 1);
            int P = 2 * (bs + 1);

            var cx = new float[N]; var cy = new float[N]; var dx = new float[N]; var dy = new float[N];
            float hx = w / 2f - radius, hy = h / 2f - radius;
            float[] ccx = { hx, -hx, -hx, hx };
            float[] ccy = { hy, hy, -hy, -hy };
            int n = 0;
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= arc; i++)
                {
                    float a = (c * 90f + i * 90f / arc) * Mathf.Deg2Rad;
                    cx[n] = ccx[c]; cy[n] = ccy[c]; dx[n] = Mathf.Cos(a); dy[n] = Mathf.Sin(a); n++;
                }

            var ins = new float[P]; var zz = new float[P]; var no = new float[P]; var nz = new float[P];
            for (int k = 0; k <= bs; k++)
            {
                float f = 90f * k / bs * Mathf.Deg2Rad;
                ins[k] = bevel * (1f - Mathf.Sin(f));
                zz[k] = -t / 2f + bevel * (1f - Mathf.Cos(f));
                no[k] = Mathf.Sin(f); nz[k] = -Mathf.Cos(f);
                int j = bs + 1 + k;
                ins[j] = bevel * (1f - Mathf.Cos(f));
                zz[j] = t / 2f - bevel + bevel * Mathf.Sin(f);
                no[j] = Mathf.Cos(f); nz[j] = Mathf.Sin(f);
            }

            int total = P * N + 2 * (N + 1);
            var verts = new Vector3[total];
            var nrms = new Vector3[total];
            for (int r = 0; r < P; r++)
                for (int i = 0; i < N; i++)
                {
                    float rr = radius - ins[r];
                    verts[r * N + i] = new Vector3(cx[i] + dx[i] * rr, cy[i] + dy[i] * rr, zz[r]);
                    nrms[r * N + i] = new Vector3(dx[i] * no[r], dy[i] * no[r], nz[r]).normalized;
                }

            int cF = P * N;
            int cB = P * N + N + 1;
            verts[cF] = new Vector3(0, 0, -t / 2f); nrms[cF] = new Vector3(0, 0, -1);
            verts[cB] = new Vector3(0, 0, t / 2f); nrms[cB] = new Vector3(0, 0, 1);
            for (int i = 0; i < N; i++)
            {
                verts[cF + 1 + i] = verts[i]; nrms[cF + 1 + i] = new Vector3(0, 0, -1);
                verts[cB + 1 + i] = verts[(P - 1) * N + i]; nrms[cB + 1 + i] = new Vector3(0, 0, 1);
            }

            var tris = new List<int>();
            Action<int, int, int> tri = (a, b, c) =>
            {
                Vector3 g = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                Vector3 nn = nrms[a] + nrms[b] + nrms[c];
                if (Vector3.Dot(g, nn) < 0f) { int tmp = b; b = c; c = tmp; }
                tris.Add(a); tris.Add(b); tris.Add(c);
            };

            // draw order matters (unlit shader writes no depth): back cap, sides back-to-front, front cap last
            for (int i = 0; i < N; i++) tri(cB, cB + 1 + i, cB + 1 + (i + 1) % N);
            for (int r = P - 2; r >= 0; r--)
                for (int i = 0; i < N; i++)
                {
                    int i2 = (i + 1) % N;
                    int a = r * N + i, b = r * N + i2, c = (r + 1) * N + i2, d = (r + 1) * N + i;
                    tri(a, b, c);
                    tri(a, c, d);
                }
            for (int i = 0; i < N; i++) tri(cF, cF + 1 + i, cF + 1 + (i + 1) % N);

            var s = new Slab();
            s.Mesh = new Mesh { name = "ModCheckerSlab" };
            s.Mesh.vertices = verts;
            s.Mesh.normals = nrms;
            s.Mesh.triangles = tris.ToArray();
            s.Mesh.RecalculateBounds();
            s.nrm = nrms;
            s.col = new Color32[total];
            s.Shade(Color.gray);
            return s;
        }

        public void Shade(Color baseCol)
        {
            Vector3 L = new Vector3(-0.35f, 0.6f, -0.72f).normalized;
            Vector3 H = (L + new Vector3(0, 0, -1)).normalized;
            for (int i = 0; i < nrm.Length; i++)
            {
                float d = Mathf.Max(0f, Vector3.Dot(nrm[i], L));
                float k = 0.48f + 0.62f * d;
                float spec = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(nrm[i], H)), 22f) * 0.32f;
                Color c = new Color(
                    Mathf.Clamp01(baseCol.r * k + spec),
                    Mathf.Clamp01(baseCol.g * k + spec),
                    Mathf.Clamp01(baseCol.b * k + spec), 1f);
                col[i] = c;
            }
            Mesh.colors32 = col;
        }
    }

    // ===================================================================== sprites / materials

    internal static class Gfx
    {
        private static Sprite round;
        private static Shader unlit;
        private static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();

        public static Shader Unlit
        {
            get
            {
                if (unlit == null)
                {
                    string[] names = { "Sprites/Default", "UI/Default", "Unlit/Color", "Legacy Shaders/Particles/Alpha Blended", "GorillaTag/UberShader", "Hidden/Internal-Colored" };
                    foreach (var n in names) { unlit = Shader.Find(n); if (unlit != null) break; }
                    if (unlit == null)
                    {
                        foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
                            if (s != null && (s.name == "Sprites/Default" || s.name == "UI/Default")) { unlit = s; break; }
                    }
                    if (Plugin.Log != null)
                    {
                        if (unlit == null) Plugin.Log.LogError("No usable shader found for the tablet.");
                        else Plugin.Log.LogInfo("Tablet shader: " + unlit.name);
                    }
                }
                return unlit;
            }
        }

        /// <summary>Opaque-looking body material; rendered before the canvases that sit on top of it.</summary>
        public static Material BodyMat()
        {
            var m = new Material(Unlit);
            m.renderQueue = 2990;
            return m;
        }

        public static Material FlatMat(Color c)
        {
            var m = new Material(Unlit);
            m.color = c;
            return m;
        }

        /// <summary>128px rounded rectangle, 9-sliced (border 48 = corner radius).</summary>
        public static Sprite Round
        {
            get
            {
                if (round != null) return round;
                const int S = 128, B = 48;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float dx = Mathf.Max(B - x - 0.5f, x + 0.5f - (S - B), 0f);
                        float dy = Mathf.Max(B - y - 0.5f, y + 0.5f - (S - B), 0f);
                        float a = Mathf.Clamp01(B - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                        px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                tex.SetPixels32(px);
                tex.Apply(false);
                round = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(B, B, B, B));
                return round;
            }
        }

        private static Sprite soft;

        /// <summary>A soft white disc that fades to nothing at the edge (the moving bass shapes).</summary>
        public static Sprite Soft
        {
            get
            {
                if (soft != null) return soft;
                const int S = 64;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float dx = (x + 0.5f - S / 2f) / (S / 2f), dy = (y + 0.5f - S / 2f) / (S / 2f);
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        a = a * a * (3f - 2f * a);
                        px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                tex.SetPixels32(px);
                tex.Apply(false);
                soft = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
                return soft;
            }
        }

        // ---------------------------------------------------------------- icons

        private static Sprite MakeIcon(Func<float, float, bool> inside)
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                        {
                            float u = ((x + (sx + 0.5f) / 3f) / S) * 2f - 1f;
                            float v = ((y + (sy + 0.5f) / 3f) / S) * 2f - 1f;
                            if (inside(u, v)) hit++;
                        }
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 9));
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }

        public static Sprite Icon(string key)
        {
            Sprite s;
            if (icons.TryGetValue(key, out s)) return s;
            switch (key)
            {
                case "players":
                    s = MakeIcon((x, y) =>
                    {
                        float hy = y - 0.35f;
                        if (x * x + hy * hy < 0.09f) return true;
                        float bx = x / 0.62f, by = (y + 0.55f) / 0.5f;
                        return bx * bx + by * by < 1f && y < -0.08f;
                    });
                    break;
                case "info":
                    s = MakeIcon((x, y) =>
                    {
                        float r = Mathf.Sqrt(x * x + y * y);
                        if (r > 0.72f && r < 0.92f) return true;
                        if (x * x + (y - 0.4f) * (y - 0.4f) < 0.01f) return true;
                        return Mathf.Abs(x) < 0.09f && y > -0.4f && y < 0.2f;
                    });
                    break;
                case "friends":
                    s = MakeIcon((x, y) =>
                    {
                        float hx = x * 1.25f, hy = y * 1.25f + 0.1f;
                        float a = hx * hx + hy * hy - 1f;
                        return a * a * a - hx * hx * hy * hy * hy <= 0f;
                    });
                    break;
                case "camera":
                    s = MakeIcon((x, y) =>
                    {
                        bool body = Mathf.Abs(x) < 0.88f && y > -0.55f && y < 0.4f;
                        bool bump = Mathf.Abs(x) < 0.38f && y >= 0.4f && y < 0.68f;
                        if (!(body || bump)) return false;
                        float r = Mathf.Sqrt(x * x + (y + 0.08f) * (y + 0.08f));
                        return !(r > 0.2f && r < 0.4f);   // lens ring cut out of the body
                    });
                    break;
                case "play":
                    s = MakeIcon((x, y) => x > -0.55f && x < 0.75f && Mathf.Abs(y) < (0.75f - x) * 0.6f);
                    break;
                case "pause":
                    s = MakeIcon((x, y) => (Mathf.Abs(x - 0.35f) < 0.2f || Mathf.Abs(x + 0.35f) < 0.2f) && Mathf.Abs(y) < 0.7f);
                    break;
                case "next":
                    s = MakeIcon((x, y) =>
                        (x > -0.75f && x < 0.3f && Mathf.Abs(y) < (0.3f - x) * 0.66f) ||
                        (x > 0.42f && x < 0.7f && Mathf.Abs(y) < 0.7f));
                    break;
                case "prev":
                    s = MakeIcon((x, y) =>
                        (x < 0.75f && x > -0.3f && Mathf.Abs(y) < (x + 0.3f) * 0.66f) ||
                        (x < -0.42f && x > -0.7f && Mathf.Abs(y) < 0.7f));
                    break;
                case "tablet":
                    s = MakeIcon((x, y) =>
                    {
                        bool outer = Mathf.Abs(x) < 0.92f && Mathf.Abs(y) < 0.62f;
                        bool inner = Mathf.Abs(x) < 0.72f && Mathf.Abs(y) < 0.42f;
                        bool pic = x > 0.12f && x < 0.55f && Mathf.Abs(y) < 0.24f;   // the bigger album picture on the right
                        return (outer && !inner) || pic;
                    });
                    break;
                case "music":
                    s = MakeIcon((x, y) =>
                    {
                        float hx = x + 0.28f, hy = y + 0.5f;
                        if (hx * hx + hy * hy < 0.09f) return true;                          // note head
                        if (x > 0.0f && x < 0.18f && y > -0.5f && y < 0.78f) return true;   // stem
                        return x >= 0.18f && x < 0.62f && y < 0.78f - (x - 0.18f) * 0.55f && y > 0.42f - (x - 0.18f) * 0.55f;   // flag
                    });
                    break;
                case "back":
                    s = MakeIcon((x, y) =>
                    {
                        bool head = x > -0.85f && x < -0.05f && Mathf.Abs(y) < (x + 0.85f) * 0.85f;
                        bool shaft = x >= -0.1f && x < 0.85f && Mathf.Abs(y) < 0.18f;
                        return head || shaft;
                    });
                    break;
                case "config":
                    s = MakeIcon((x, y) =>
                    {
                        float r = Mathf.Sqrt(x * x + y * y);
                        float th = Mathf.Atan2(y, x);
                        float rr = 0.66f + 0.14f * Mathf.Clamp(Mathf.Sin(8f * th) * 3f, -1f, 1f);
                        return r < rr && r > 0.27f;
                    });
                    break;
                default: // gorilla
                    s = MakeIcon((x, y) =>
                    {
                        float hy = y - 0.05f;
                        bool head = x * x / 0.64f + hy * hy / 0.6084f < 1f;
                        bool earL = (x + 0.82f) * (x + 0.82f) + (y - 0.12f) * (y - 0.12f) < 0.0484f;
                        bool earR = (x - 0.82f) * (x - 0.82f) + (y - 0.12f) * (y - 0.12f) < 0.0484f;
                        if (!(head || earL || earR)) return false;
                        if ((x + 0.3f) * (x + 0.3f) + (y - 0.2f) * (y - 0.2f) < 0.0144f) return false;
                        if ((x - 0.3f) * (x - 0.3f) + (y - 0.2f) * (y - 0.2f) < 0.0144f) return false;
                        if (x * x / 0.1444f + (y + 0.35f) * (y + 0.35f) / 0.04f < 1f) return false;
                        return true;
                    });
                    break;
            }
            icons[key] = s;
            return s;
        }

        // ---------------------------------------------------------------- screen gradient

        /// <summary>Fills a rounded-corner screen texture with the current theme (gradient, glow, stars).</summary>
        public static void FillScreen(Texture2D tex, Color32[] buf, int w, int h, float radius)
        {
            Color bot = Skin.Bottom, top = Skin.Top, acc = Skin.Accent;
            bool stars = Skin.Stars;
            for (int y = 0; y < h; y++)
            {
                float t = h > 1 ? (float)y / (h - 1) : 0f;
                Color baseC = Color.Lerp(bot, top, t);
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (w - radius), 0f);
                    float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (h - radius), 0f);
                    float a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    Color c = baseC;
                    if (stars)
                    {
                        float d1 = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), new Vector2(w * 0.3f, h * 0.72f)) / (w * 0.9f));
                        float d2 = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), new Vector2(w * 0.8f, h * 0.25f)) / (w * 0.8f));
                        c = Color.Lerp(c, acc, (d1 * d1 + d2 * d2) * 0.2f);
                    }
                    buf[y * w + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(a * 255f));
                }
            }
            if (stars)
            {
                var rnd = new System.Random(11);
                int count = w * h / 240;
                for (int i = 0; i < count; i++)
                {
                    int px = rnd.Next(w), py = rnd.Next(h);
                    float br = 0.4f + 0.6f * (float)rnd.NextDouble();
                    Plot(buf, w, h, px, py, br);
                    if (br > 0.85f)
                    {
                        Plot(buf, w, h, px + 1, py, br * 0.4f); Plot(buf, w, h, px - 1, py, br * 0.4f);
                        Plot(buf, w, h, px, py + 1, br * 0.4f); Plot(buf, w, h, px, py - 1, br * 0.4f);
                    }
                }
            }
            tex.SetPixels32(buf);
            tex.Apply(false);
        }

        private static void Plot(Color32[] buf, int w, int h, int x, int y, float amt)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            Color32 c = buf[y * w + x];
            if (c.a < 200) return;
            int add = (int)(255f * amt);
            buf[y * w + x] = new Color32((byte)Mathf.Min(255, c.r + add), (byte)Mathf.Min(255, c.g + add), (byte)Mathf.Min(255, c.b + add), c.a);
        }
    }
}