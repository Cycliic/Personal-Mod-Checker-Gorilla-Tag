using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ModChecker
{
    // ===================================================================== theme

    internal static class Skin
    {
        internal class Theme { public string Name; public Color Body, Top, Bottom, Accent, Moving; public bool Stars, Reactive; public float MovingAlpha; }

        public const string PlayerTheme = "Player Color";
        public static readonly List<Theme> Themes = new List<Theme>();
        public static Color Body, Top, Bottom, Panel, Button, Accent, Text, Sub, Moving;
        public static bool Stars, Reactive;
        public static float MovingAlpha;
        public static Theme Override;      // set while music plays: the streaming service's colours
        public static readonly Color Red = new Color(1f, 0.3f, 0.3f, 1f);
        public static readonly Color Green = new Color(0.35f, 1f, 0.5f, 1f);
        private static readonly List<Func<bool>> binds = new List<Func<bool>>();

        private static Color Hex(string h)
        {
            Color c;
            if (!ColorUtility.TryParseHtmlString("#" + h, out c)) c = Color.gray;
            return c;
        }

        private static void T(string n, string body, string top, string bottom, string acc, bool stars)
        {
            Themes.Add(new Theme { Name = n, Body = Hex(body), Top = Hex(top), Bottom = Hex(bottom), Accent = Hex(acc), Stars = stars });
        }

        static Skin()
        {
            T("Galaxy", "2a1b5e", "1a0f3d", "070518", "b57bff", true);
            T("Ocean", "0e5a78", "0b3f5e", "04182b", "3fd0ff", false);
            T("Navy", "1b2a52", "15234a", "070d22", "5b8cff", false);
            T("Sunset", "7a2c4a", "5c1f4a", "1d0a24", "ff8a4c", false);
            T("Forest", "1e5a35", "123d28", "05140c", "55e08a", false);
            T("Rose", "8a2f55", "5e1d40", "220a17", "ff7ab8", false);
            T("Lava", "6b1a10", "3d0d0a", "120403", "ff5a2a", false);
            T("Mint", "1f6b5e", "124a42", "051a17", "6dffd2", false);
            T("Midnight", "20203a", "12121f", "050509", "8f8fff", true);
            T("Candy", "8a3a8c", "5a2a6e", "1a0f2a", "ff9ee6", false);
            T("Gold", "5c4a14", "2a2008", "0d0a02", "ffcf40", false);
            T("Arctic", "6f8fa8", "2c4a63", "0c1a26", "c9f2ff", false);
            T("Cyber", "1a1a2e", "10102a", "05050f", "00ffa6", false);
        }

        /// <summary>Runs f now and again every time the theme changes, until it returns false.</summary>
        public static void Bind(Func<bool> f)
        {
            if (f()) binds.Add(f);
        }

        public static Theme Find(string name)
        {
            return Themes.Find(x => x.Name == name);
        }

        public static void Apply(Color player)
        {
            string name = Cfg.Theme != null ? Cfg.Theme.Value : PlayerTheme;
            Theme t = Override ?? Find(name);
            Reactive = false;
            if (t == null)
            {
                float h, s, v;
                Color.RGBToHSV(player, out h, out s, out v);
                s = Mathf.Clamp(s, 0.35f, 0.95f);
                Body = Color.HSVToRGB(h, s * 0.85f, 0.62f);
                Top = Color.HSVToRGB(h, s, 0.32f);
                Bottom = Color.HSVToRGB(h, s, 0.12f);
                Accent = Color.HSVToRGB(h, s * 0.5f, 1f);
                Stars = false;
            }
            else
            {
                Body = t.Body; Top = t.Top; Bottom = t.Bottom; Accent = t.Accent; Stars = t.Stars;
                Moving = t.Moving; MovingAlpha = t.MovingAlpha; Reactive = t.Reactive;
            }
            Panel = Color.Lerp(Bottom, Accent, 0.14f); Panel.a = 0.9f;
            Button = Color.Lerp(Top, Accent, 0.32f); Button.a = 1f;
            Text = Color.white;
            Sub = Color.Lerp(Color.white, Accent, 0.45f);
            for (int i = binds.Count - 1; i >= 0; i--)
                if (!binds[i]()) binds.RemoveAt(i);
        }

        public static Color ThemeButton(Theme t, bool selected)
        {
            return selected ? t.Accent : Color.Lerp(t.Top, t.Accent, 0.5f);
        }
    }

    // ===================================================================== factory

    internal static class UI
    {
        private static Font font;

        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 16);
                return font;
            }
        }

        /// <summary>Top-left anchored rect; x/y measured from the parent's top-left corner (y grows downward).</summary>
        public static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static Image AddImg(RectTransform rt, Func<Color> col, float radius)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Gfx.Round;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 48f / Mathf.Max(1f, radius);
            img.raycastTarget = false;
            if (col != null)
                Skin.Bind(() => { if (img == null) return false; img.color = col(); return true; });
            return img;
        }

        /// <summary>Themed gradient screen with rounded corners (regenerated when the theme changes).</summary>
        public static Image Screen(Transform parent, float x, float y, float w, float h, float radius)
        {
            var rt = Rect("screen", parent, x, y, w, h);
            int tw = Mathf.RoundToInt(w), th = Mathf.RoundToInt(h);
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var buf = new Color32[tw * th];
            var sprite = Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), 100f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            Skin.Bind(() => { if (img == null) return false; Gfx.FillScreen(tex, buf, tw, th, radius); return true; });
            BassFx.Attach(parent, x, y, w, h);      // moving shapes on top of the screen while a music theme is active
            return img;
        }

        private static Text MakeText(RectTransform rt, string s, int size, TextAnchor a, bool bold)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.alignment = a;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            t.raycastTarget = false;
            t.text = s;
            return t;
        }

        /// <summary>Text that follows the theme (col null = theme text colour).</summary>
        public static Text Label(Transform parent, float x, float y, float w, float h, string s, int size, Func<Color> col, TextAnchor a, bool bold)
        {
            var rt = Rect("txt", parent, x, y, w, h);
            var t = MakeText(rt, s, size, a, bold);
            Skin.Bind(() => { if (t == null) return false; t.color = col != null ? col() : Skin.Text; return true; });
            return t;
        }

        /// <summary>Text with a fixed colour (no theme binding).</summary>
        public static Text LabelC(Transform parent, float x, float y, float w, float h, string s, int size, Color col, TextAnchor a, bool bold)
        {
            var rt = Rect("txt", parent, x, y, w, h);
            var t = MakeText(rt, s, size, a, bold);
            t.color = col;
            return t;
        }

        public static Text LabelFill(Transform parent, string s, int size, Func<Color> col, TextAnchor a, bool bold)
        {
            var rt = Rect("txt", parent, 0, 0, 10, 10);
            Stretch(rt);
            var t = MakeText(rt, s, size, a, bold);
            Skin.Bind(() => { if (t == null) return false; t.color = col != null ? col() : Skin.Text; return true; });
            return t;
        }

        public static Btn Button(Space sp, Transform parent, float x, float y, float w, float h, string text, int size,
                                 Action tap, float radius, Func<Color> col, Func<Color> textCol)
        {
            var rt = Rect("btn", parent, x, y, w, h);
            var b = new Btn { Rt = rt, OnTap = tap };
            b.Col = col ?? (() => Skin.Button);
            b.Bg = AddImg(rt, null, radius);
            try { b.Bg.color = b.Col(); } catch { b.Bg.color = Skin.Button; }
            if (text != null) b.Label = LabelFill(rt, text, size, textCol, TextAnchor.MiddleCenter, true);
            sp.Btns.Add(b);
            return b;
        }

        public static Image Icon(Transform parent, string key, float x, float y, float size)
        {
            var rt = Rect("icon", parent, x, y, size, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Gfx.Icon(key);
            img.raycastTarget = false;
            return img;
        }
    }

    // ===================================================================== touch

    /// <summary>A group of buttons that live on one tablet (hit-testing is done by hand, no physics).</summary>
    internal class Space
    {
        public Transform Root;
        public float Radius = 0.3f;
        public bool Active = true;
        public readonly List<Btn> Btns = new List<Btn>();
    }

    internal class Host
    {
        public Transform Parent;
        public float W;
        public Space Sp;
        public Btn Proxy;   // scroll surface that rows forward drags to
    }

    internal class Btn
    {
        public RectTransform Rt;
        public Image Bg;
        public Text Label;
        public Action OnTap;
        public Action<float> OnDrag;
        public Action OnRelease;
        public Btn Proxy;
        public bool Enabled = true;
        public int Priority = 1;
        public Func<Color> Col;
        public bool Hover;
        public float Press;
        internal RectMask2D[] Masks;

        public void Refresh(float dt)
        {
            if (Rt == null || Bg == null) return;
            Color c;
            try { c = Col != null ? Col() : Skin.Button; } catch { c = Skin.Button; }
            Color target = !Enabled ? Color.Lerp(c, new Color(0.1f, 0.1f, 0.1f, c.a), 0.65f)
                         : Hover ? Color.Lerp(c, Skin.Accent, 0.45f) : c;
            Bg.color = Color.Lerp(Bg.color, target, 1f - Mathf.Exp(-16f * dt));
            Press = Mathf.MoveTowards(Press, 0f, dt * 5f);   // no scale animation on press
            if (Label != null)
            {
                Color lc = Label.color;
                lc.a = Enabled ? 1f : 0.5f;
                Label.color = lc;
            }
        }
    }

    /// <summary>
    /// Fingertip "sphere trigger": the tip point is tested against every button rectangle in that
    /// button's own plane. Hovering is within 5 cm of the surface, touching is within ~1.2 cm.
    /// Plain buttons fire when the tip touches them; rows inside a scroll view fire on release unless the finger dragged.
    /// </summary>
    internal class Touch
    {
        public const float TipRadius = 0.008f;
        public Action<Btn> Pressed;

        private Btn hover, down, drag;
        private bool touching, dragged;
        private float lastY, accum, lastFire;

        public void Update(Vector3 tip, List<Space> spaces, bool enabled)
        {
            Btn best = null;
            float bestScore = float.MaxValue, bestZ = 0f;

            if (enabled)
            {
                for (int s = 0; s < spaces.Count; s++)
                {
                    var sp = spaces[s];
                    if (!sp.Active || sp.Root == null || !sp.Root.gameObject.activeInHierarchy) continue;
                    if ((sp.Root.position - tip).sqrMagnitude > sp.Radius * sp.Radius) continue;
                    for (int i = sp.Btns.Count - 1; i >= 0; i--)
                    {
                        var b = sp.Btns[i];
                        if (b.Rt == null) { sp.Btns.RemoveAt(i); continue; }
                        if (!b.Enabled || !b.Rt.gameObject.activeInHierarchy) continue;
                        Vector3 ls = b.Rt.lossyScale;
                        if (ls.x < 1e-6f) continue;
                        Vector3 lp = b.Rt.InverseTransformPoint(tip);
                        float m = TipRadius / ls.x;
                        Rect r = b.Rt.rect;
                        if (lp.x < r.xMin - m || lp.x > r.xMax + m || lp.y < r.yMin - m || lp.y > r.yMax + m) continue;
                        float z = lp.z * ls.z;                       // metres; negative = in front of the screen
                        if (z > 0.006f || z < -0.05f) continue;
                        if (!InMasks(b, tip)) continue;
                        float score = -b.Priority * 10f + Mathf.Abs(z);
                        if (score < bestScore) { bestScore = score; best = b; bestZ = z; }
                    }
                }
            }

            if (hover != best)
            {
                if (hover != null) hover.Hover = false;
                hover = best;
                if (hover != null) hover.Hover = true;
            }

            bool now = best != null && bestZ > -0.012f;
            if (now && !touching)
            {
                touching = true;
                down = best;
                dragged = false;
                accum = 0f;
                drag = best.OnDrag != null ? best : best.Proxy;
                if (drag != null && drag.Rt != null) lastY = drag.Rt.InverseTransformPoint(tip).y;
                if (best.Proxy == null && best.OnDrag == null && best.OnTap != null) Fire(best);
            }
            else if (touching && !now)
            {
                if (down != null && down.Rt != null && down.Proxy != null && !dragged && down.OnTap != null && down.Enabled) Fire(down);
                if (drag != null && drag.OnRelease != null) drag.OnRelease();
                touching = false; down = null; drag = null;
            }
            else if (touching && drag != null && drag.Rt != null)
            {
                float y = drag.Rt.InverseTransformPoint(tip).y;
                float dy = y - lastY;
                lastY = y;
                accum += Mathf.Abs(dy);
                if (accum > 4f) dragged = true;
                if (dragged && drag.OnDrag != null) drag.OnDrag(dy);
            }
        }

        private void Fire(Btn b)
        {
            if (Time.unscaledTime - lastFire < 0.15f) return;
            lastFire = Time.unscaledTime;
            b.Press = 1f;
            try { if (b.OnTap != null) b.OnTap(); }
            catch (Exception e) { Plugin.Log.LogError("button: " + e); }
            if (Pressed != null) Pressed(b);
        }

        private static bool InMasks(Btn b, Vector3 tip)
        {
            if (b.Masks == null) b.Masks = b.Rt.GetComponentsInParent<RectMask2D>(true);
            for (int i = 0; i < b.Masks.Length; i++)
            {
                var mk = b.Masks[i];
                if (mk == null) continue;
                var mr = mk.rectTransform;
                Vector3 q = mr.InverseTransformPoint(tip);
                if (!mr.rect.Contains(new Vector2(q.x, q.y))) return false;
            }
            return true;
        }
    }

    // ===================================================================== list rows

    internal abstract class Item
    {
        public RectTransform Rt;
        public float H = 22f;
        protected float Timer;   // text rows refresh at 10 Hz to avoid per-frame string garbage
        public virtual float Height { get { return H; } }
        public virtual void Tick(float dt) { }
        public virtual void Kill() { if (Rt != null) UnityEngine.Object.Destroy(Rt.gameObject); }
    }

    internal class ScrollView
    {
        public RectTransform Root, Content;
        public Host Hst;
        public readonly List<Item> Items = new List<Item>();
        public float Offset, Gap = 4f;
        private float vel;
        private int lastDrag = -10;
        private readonly float W, H;
        private readonly RectTransform bar;

        public ScrollView(Space sp, Transform parent, float x, float y, float w, float h)
        {
            W = w; H = h;
            Root = UI.Rect("scroll", parent, x, y, w, h);
            Root.gameObject.AddComponent<RectMask2D>();
            Content = UI.Rect("content", Root, 0, 0, w, 10);

            var sr = UI.Rect("surface", Root, 0, 0, w, h);
            var surface = new Btn { Rt = sr, Priority = 0 };
            sp.Btns.Add(surface);
            surface.OnDrag = d =>
            {
                Offset += d;
                vel = Mathf.Lerp(vel, d / Mathf.Max(Time.deltaTime, 0.006f), 0.4f);
                lastDrag = Time.frameCount;
            };

            Hst = new Host { Parent = Content, W = w - 6f, Sp = sp, Proxy = surface };
            bar = UI.Rect("bar", Root, w - 3f, 0, 3f, 20f);
            UI.AddImg(bar, () => new Color(Skin.Accent.r, Skin.Accent.g, Skin.Accent.b, 0.6f), 1.5f);
        }

        public T Add<T>(T it) where T : Item
        {
            Items.Add(it);
            return it;
        }

        public void Clear()
        {
            for (int i = 0; i < Items.Count; i++) Items[i].Kill();
            Items.Clear();
            Offset = 0f; vel = 0f;
        }

        public void Tick(float dt)
        {
            float y = 0f;
            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];
                it.Tick(dt);
                float h = it.Height;
                it.Rt.anchoredPosition = new Vector2(0f, -y);
                it.Rt.sizeDelta = new Vector2(Hst.W, h);
                bool vis = y + h > Offset - 4f && y < Offset + H + 4f;
                if (it.Rt.gameObject.activeSelf != vis) it.Rt.gameObject.SetActive(vis);
                y += h + Gap;
            }
            float contentH = Mathf.Max(0f, y - Gap);
            float max = Mathf.Max(0f, contentH - H);

            bool held = Time.frameCount - lastDrag <= 2;
            if (!held)
            {
                Offset += vel * dt;
                vel *= Mathf.Exp(-5f * dt);
                if (Offset < 0f) { Offset = Mathf.Lerp(Offset, 0f, 1f - Mathf.Exp(-14f * dt)); vel = 0f; }
                else if (Offset > max) { Offset = Mathf.Lerp(Offset, max, 1f - Mathf.Exp(-14f * dt)); vel = 0f; }
            }
            else Offset = Mathf.Clamp(Offset, -25f, max + 25f);

            Content.anchoredPosition = new Vector2(0f, Offset);

            bool show = max > 1f;
            bar.gameObject.SetActive(show);
            if (show)
            {
                float bh = Mathf.Max(16f, H * H / contentH);
                float frac = Mathf.Clamp01(Offset / max);
                bar.sizeDelta = new Vector2(3f, bh);
                bar.anchoredPosition = new Vector2(W - 3f, -(frac * (H - bh)));
            }
        }
    }

    internal class RowSection : Item
    {
        public RowSection(Host h, string text)
        {
            H = 16f;
            Rt = UI.Rect("sec", h.Parent, 0, 0, h.W, H);
            UI.Label(Rt, 4, 0, h.W - 8, H, text, 9, () => Skin.Accent, TextAnchor.LowerLeft, true);
        }
    }

    internal class RowText : Item
    {
        private readonly Text t;
        private readonly Func<string> get;
        private string last;

        public RowText(Host h, Func<string> text, int size, Func<Color> col, float height, TextAnchor anchor)
        {
            H = height; get = text;
            Rt = UI.Rect("text", h.Parent, 0, 0, h.W, H);
            t = UI.Label(Rt, 6, 0, h.W - 12, H, text(), size, col, anchor, false);
        }

        public override void Tick(float dt)
        {
            Timer -= dt;
            if (Timer > 0f && last != null) return;
            Timer = 0.1f;
            string s = get();
            if (s != last) { last = s; t.text = s; }
        }
    }

    internal class RowKV : Item
    {
        private readonly Text v;
        private readonly Image box;
        private readonly Func<string> val;
        private readonly Func<Color> swatch, valCol;
        private string last;

        public RowKV(Host h, string key, Func<string> value, Func<Color> swatchCol, Func<Color> valueCol, float height)
        {
            H = height; val = value; swatch = swatchCol; valCol = valueCol;
            Rt = UI.Rect("kv", h.Parent, 0, 0, h.W, H);
            UI.AddImg(Rt, () => Skin.Panel, 5f);
            UI.Label(Rt, 8, 0, 86, H, key, 10, () => Skin.Sub, TextAnchor.MiddleLeft, false);
            float vx = 92f;
            float vw = h.W - vx - 8f - (swatch != null ? 22f : 0f);
            v = UI.Label(Rt, vx, 0, vw, H, "", 10, null, TextAnchor.MiddleRight, true);
            if (swatch != null)
            {
                var br = UI.Rect("sw", Rt, h.W - 24f, (H - 12f) / 2f, 16, 12);
                box = UI.AddImg(br, null, 3f);
            }
        }

        public override void Tick(float dt)
        {
            Timer -= dt;
            if (Timer <= 0f || last == null)
            {
                Timer = 0.1f;
                string s = val();
                if (s != last) { last = s; v.text = s; }
            }
            if (valCol != null) v.color = valCol();
            if (box != null) box.color = swatch();
        }
    }

    internal class RowButton : Item
    {
        public readonly Btn B;
        private readonly Func<string> label;
        private readonly Func<bool> enabledWhen;
        private string last;

        public RowButton(Host h, Func<string> text, Action tap, Func<Color> col, Func<Color> textCol, float height, Func<bool> enabled)
        {
            H = height; label = text; enabledWhen = enabled;
            Rt = UI.Rect("rowbtn", h.Parent, 0, 0, h.W, H);
            B = UI.Button(h.Sp, Rt, 0, 0, h.W, H, text(), 11, tap, 6f, col, textCol);
            B.Proxy = h.Proxy;
        }

        public override void Tick(float dt)
        {
            Timer -= dt;
            if (Timer <= 0f || last == null)
            {
                Timer = 0.1f;
                string s = label();
                if (s != last) { last = s; B.Label.text = s; }
            }
            if (enabledWhen != null) B.Enabled = enabledWhen();
        }
    }

    internal class BtnSpec
    {
        public Func<string> Text;
        public Action Tap;
        public Func<Color> Col, TextCol;
        public Func<bool> EnabledWhen;
        public int Size = 10;
    }

    internal class RowButtons : Item
    {
        private readonly Btn[] bs;
        private readonly BtnSpec[] sp;
        private readonly string[] last;

        public RowButtons(Host h, float height, params BtnSpec[] specs)
        {
            H = height; sp = specs; bs = new Btn[specs.Length]; last = new string[specs.Length];
            Rt = UI.Rect("btns", h.Parent, 0, 0, h.W, H);
            int n = specs.Length;
            float gap = 4f;
            float w = (h.W - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                bs[i] = UI.Button(h.Sp, Rt, i * (w + gap), 0, w, H, specs[i].Text(), specs[i].Size, specs[i].Tap, 6f, specs[i].Col, specs[i].TextCol);
                bs[i].Proxy = h.Proxy;
            }
        }

        public override void Tick(float dt)
        {
            Timer -= dt;
            bool refreshText = Timer <= 0f || last[0] == null;
            if (refreshText) Timer = 0.1f;
            for (int i = 0; i < bs.Length; i++)
            {
                if (refreshText)
                {
                    string s = sp[i].Text();
                    if (s != last[i]) { last[i] = s; bs[i].Label.text = s; }
                }
                if (sp[i].EnabledWhen != null) bs[i].Enabled = sp[i].EnabledWhen();
            }
        }
    }

    internal class RowStepper : Item
    {
        private readonly Text v;
        private readonly Func<string> val;
        private string last;

        public RowStepper(Host h, string label, Func<string> value, Action dec, Action inc)
        {
            H = 26f; val = value;
            Rt = UI.Rect("stepper", h.Parent, 0, 0, h.W, H);
            UI.AddImg(Rt, () => Skin.Panel, 6f);
            UI.Label(Rt, 8, 0, 56, H, label, 10, () => Skin.Sub, TextAnchor.MiddleLeft, false);
            float plusX = h.W - 8f - 24f;
            float valX = plusX - 4f - 40f;
            float minusX = valX - 4f - 24f;
            var minus = UI.Button(h.Sp, Rt, minusX, 2, 24, 22, "-", 14, dec, 6f, null, null);
            var plus = UI.Button(h.Sp, Rt, plusX, 2, 24, 22, "+", 14, inc, 6f, null, null);
            minus.Proxy = h.Proxy; plus.Proxy = h.Proxy;
            v = UI.Label(Rt, valX, 0, 40, H, "", 11, null, TextAnchor.MiddleCenter, true);
        }

        public override void Tick(float dt)
        {
            Timer -= dt;
            if (Timer > 0f && last != null) return;
            Timer = 0.1f;
            string s = val();
            if (s != last) { last = s; v.text = s; }
        }
    }

    /// <summary>Header button that unfolds a list of child rows (expand on press, contract on press).</summary>
    internal class RowExpand : Item
    {
        public readonly Btn Head;
        public readonly Host Inner;
        public readonly List<Item> Kids = new List<Item>();
        private readonly RectTransform body;
        private readonly Func<string> title;
        private readonly float W;
        private float open, target, kidsH;
        private string last;

        public bool IsOpen { get { return target > 0.5f; } }

        public RowExpand(Host h, Func<string> titleFn, Func<Color> textCol, Func<Color> btnCol, float headH)
        {
            H = headH; W = h.W; title = titleFn;
            Rt = UI.Rect("expand", h.Parent, 0, 0, h.W, headH);
            Head = UI.Button(h.Sp, Rt, 0, 0, h.W, headH, titleFn(), 11, Toggle, 6f, btnCol, textCol);
            Head.Proxy = h.Proxy;
            body = UI.Rect("body", Rt, 0, headH + 3f, h.W, 0f);
            body.gameObject.AddComponent<RectMask2D>();
            body.gameObject.SetActive(false);
            Inner = new Host { Parent = body, W = h.W - 8f, Sp = h.Sp, Proxy = h.Proxy };
        }

        public void Toggle() { target = target > 0.5f ? 0f : 1f; }
        public void Collapse() { target = 0f; }
        public void Add(Item it) { Kids.Add(it); }

        public void ClearKids()
        {
            for (int i = 0; i < Kids.Count; i++) Kids[i].Kill();
            Kids.Clear();
        }

        public override float Height
        {
            get { return H + (open > 0.001f ? 3f + Ease.Smooth(open) * kidsH : 0f); }
        }

        public override void Tick(float dt)
        {
            string s = title();
            if (s != last) { last = s; Head.Label.text = s; }
            open = Mathf.MoveTowards(open, target, dt * 5f);

            float y = 0f;
            for (int i = 0; i < Kids.Count; i++)
            {
                var k = Kids[i];
                k.Tick(dt);
                float kh = k.Height;
                k.Rt.anchoredPosition = new Vector2(8f, -y);
                k.Rt.sizeDelta = new Vector2(Inner.W, kh);
                y += kh + 3f;
            }
            kidsH = Mathf.Max(0f, y - 3f);
            body.sizeDelta = new Vector2(W, Ease.Smooth(open) * kidsH);
            bool on = open > 0.001f;
            if (body.gameObject.activeSelf != on) body.gameObject.SetActive(on);
        }
    }
}