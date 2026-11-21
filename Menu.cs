using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ModChecker
{
    internal enum PageId { Players, Info, Friends, Config, Music }
    internal enum EditMode { None, Name, Color }

    internal abstract class Page
    {
        public readonly MenuController M;
        public readonly RectTransform Root;
        protected Page(MenuController m, RectTransform root) { M = m; Root = root; }
        public abstract string Title { get; }
        public virtual void Show() { }
        public virtual void Hide() { }
        public virtual void Tick(float dt) { }

        // pages with sub pages show their tabs on the bottom panel
        public virtual string[] SubTabs { get { return null; } }
        public virtual int SubIndex { get { return 0; } }
        public virtual void SelectSub(int index) { }
    }

    // ===================================================================== tablet

    /// <summary>One physical slab (real mesh) with a world-space canvas lying on its front face.</summary>
    internal class Tablet
    {
        public Transform T;
        public Slab Slab;
        public Canvas Cv;
        public RectTransform CvRt;
        public Space Sp;
        public CanvasGroup Grp;
        public Vector3 Shown, Hidden;
        public float Anim;
        public float Size = 1f;
        public float HiddenScale = 0.6f;
        public bool Target;

        public static Tablet Make(Transform parent, string name, float wMm, float hMm, float tkMm, float radiusMm)
        {
            var t = new Tablet();
            var go = new GameObject(name);
            t.T = go.transform;
            t.T.SetParent(parent, false);

            t.Slab = Slab.Build(wMm / 1000f, hMm / 1000f, tkMm / 1000f, radiusMm / 1000f, Mathf.Min(radiusMm * 0.6f, tkMm * 0.3f) / 1000f);
            go.AddComponent<MeshFilter>().sharedMesh = t.Slab.Mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Gfx.BodyMat();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var slab = t.Slab;
            Skin.Bind(() => { if (go == null) return false; slab.Shade(Skin.Body); return true; });

            var cgo = new GameObject("Canvas");
            cgo.transform.SetParent(go.transform, false);
            var cv = cgo.AddComponent<Canvas>();
            cv.renderMode = RenderMode.WorldSpace;
            t.Cv = cv;
            var sc = cgo.AddComponent<CanvasScaler>();
            sc.dynamicPixelsPerUnit = 6f;
            t.Grp = cgo.AddComponent<CanvasGroup>();
            t.Grp.blocksRaycasts = false;
            t.CvRt = (RectTransform)cgo.transform;
            t.CvRt.sizeDelta = new Vector2(wMm, hMm);
            t.CvRt.localScale = Vector3.one * 0.001f;
            t.CvRt.localPosition = new Vector3(0f, 0f, -(tkMm / 2000f + 0.0004f));

            t.Sp = new Space { Root = go.transform, Radius = 0.3f };
            return t;
        }

        /// <summary>The UI is only drawn on the front: seen from behind the tablet is a plain slab.</summary>
        public void UpdateFacing(Vector3 eye)
        {
            if (Cv == null) return;
            bool front = Vector3.Dot(eye - T.position, -T.forward) > 0f;
            if (Cv.enabled != front) Cv.enabled = front;
        }

        /// <summary>Slide-out / contract animation (editors, bottom panel, side panel).</summary>
        public void Tick(float dt)
        {
            Anim = Mathf.MoveTowards(Anim, Target ? 1f : 0f, dt * 3.2f);
            float e = Ease.OutCubic(Anim);
            T.localPosition = Vector3.Lerp(Hidden, Shown, e);
            T.localScale = Vector3.one * Mathf.Lerp(HiddenScale, 1f, e) * Size;
            Grp.alpha = Ease.Smooth((Anim - 0.45f) / 0.55f);
            bool on = Anim > 0.001f;
            if (T.gameObject.activeSelf != on) T.gameObject.SetActive(on);
            Sp.Active = Anim > 0.9f;
        }
    }

    // ===================================================================== carry handles

    /// <summary>A solid rounded bar on the side of the tablet. Grip it with a hand to carry the tablet around.</summary>
    internal class Handle
    {
        public Transform T;
        private Material mat;

        public static Handle Make(Transform parent, string name, Vector3 localPos)
        {
            var h = new Handle();
            var go = new GameObject(name);
            h.T = go.transform;
            h.T.SetParent(parent, false);
            h.T.localPosition = localPos;

            // 120 x 22 x 22 mm, fully rounded: a grip bar. Its long side runs along the sideways menu's vertical.
            var slab = Slab.Build(0.12f, 0.022f, 0.022f, 0.011f, 0.006f);
            go.AddComponent<MeshFilter>().sharedMesh = slab.Mesh;
            var mr = go.AddComponent<MeshRenderer>();
            h.mat = Gfx.BodyMat();
            mr.sharedMaterial = h.mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Skin.Bind(() => { if (go == null) return false; slab.Shade(Skin.Body); return true; });
            go.SetActive(false);
            return h;
        }

        /// <summary>Grow / shrink with the tablet mode animation.</summary>
        public void SetScale(float s)
        {
            bool on = s > 0.01f;
            if (T.gameObject.activeSelf != on) T.gameObject.SetActive(on);
            if (on) T.localScale = Vector3.one * s;
        }

        /// <summary>Lights up when a hand is close enough to grab it.</summary>
        public void SetGlow(bool on)
        {
            mat.color = on ? Color.Lerp(Color.white, Skin.Accent, 0.65f) : Color.white;
        }

        public void SetPose(Vector3 pos, Quaternion rot)
        {
            T.localPosition = pos;
            T.localRotation = rot;
        }
    }

    // ===================================================================== iPhone-style wheel

    internal class Wheel
    {
        public int Value;
        public float Pos;
        public Action<int> Changed;

        private const float Spacing = 22f;
        private readonly Text[] items = new Text[10];
        private readonly RectTransform[] rts = new RectTransform[10];
        private readonly float W, Hh, Radius;
        private float vel, target;
        private bool snap;

        public Wheel(Space sp, Transform parent, float x, float y, float w, float h)
        {
            W = w; Hh = h; Radius = h * 0.46f;
            var area = UI.Rect("wheel", parent, x, y, w, h);
            area.gameObject.AddComponent<RectMask2D>();

            var bandRt = UI.Rect("band", area, 2, h / 2f - 14f, w - 4f, 28f);
            UI.AddImg(bandRt, () => new Color(Skin.Accent.r, Skin.Accent.g, Skin.Accent.b, 0.22f), 7f);

            for (int i = 0; i < 10; i++)
            {
                items[i] = UI.LabelC(area, 0, 0, w, 30, i.ToString(), 24, Color.white, TextAnchor.MiddleCenter, true);
                rts[i] = items[i].rectTransform;
                rts[i].pivot = new Vector2(0.5f, 0.5f);
            }

            var sr = UI.Rect("surface", area, 0, 0, w, h);
            var surf = new Btn { Rt = sr, Priority = 1 };
            surf.OnDrag = d =>
            {
                Pos = Mathf.Clamp(Pos + d / Spacing, -0.4f, 9.4f);
                snap = false;
                vel = Mathf.Lerp(vel, (d / Spacing) / Mathf.Max(Time.deltaTime, 0.006f), 0.4f);
            };
            surf.OnRelease = () =>
            {
                target = Mathf.Clamp(Mathf.Round(Pos + vel * 0.12f), 0f, 9f);
                snap = true;
                vel = 0f;
            };
            sp.Btns.Add(surf);
        }

        public void Set(int v)
        {
            Pos = v; Value = v; target = v; snap = false; vel = 0f;
        }

        public void Tick(float dt)
        {
            if (snap)
            {
                Pos = Mathf.Lerp(Pos, target, 1f - Mathf.Exp(-16f * dt));
                if (Mathf.Abs(Pos - target) < 0.005f) { Pos = target; snap = false; }
            }
            int v = Mathf.Clamp(Mathf.RoundToInt(Pos), 0, 9);
            if (v != Value)
            {
                Value = v;
                Snd.Soft();
                if (Changed != null) Changed(v);
            }

            for (int i = 0; i < 10; i++)
            {
                float ang = (i - Pos) * 0.55f;
                if (Mathf.Abs(ang) > 1.5f)
                {
                    if (items[i].gameObject.activeSelf) items[i].gameObject.SetActive(false);
                    continue;
                }
                if (!items[i].gameObject.activeSelf) items[i].gameObject.SetActive(true);
                float c = Mathf.Cos(ang);
                float y = Mathf.Sin(ang) * Radius;
                rts[i].anchoredPosition = new Vector2(W / 2f, -(Hh / 2f + y));
                float s = 0.75f + 0.25f * c;
                rts[i].localScale = new Vector3(s, Mathf.Max(0.15f, c) * s, 1f);
                Color col = i == Value ? Skin.Accent : Skin.Text;
                col.a = c * c;
                items[i].color = col;
            }
        }
    }

    // ===================================================================== controller

    internal class MenuController : MonoBehaviour
    {
        public static MenuController I;

        public readonly Roster Roster = new Roster();
        public readonly Notifier Notes = new Notifier();
        public Tablet Main { get { return main; } }

        public PInfo Selected, Hovered;
        public EditMode Editing;
        public string NameBuf = "";
        public readonly int[] RGB = new int[3];
        public string HopStatus = "";
        public LivePreview Preview;

        // The menu lives on ONE hand (the LEFT by default, config "MenuHandSide"); the other hand points and pulls the
        // trigger. Y (left controller) toggles the menu either way.
        private bool menuOnLeft = true;
        private bool MenuOnLeft { get { return menuOnLeft; } }
        private ConfigEntry<string> PoseEntry { get { return menuOnLeft ? Cfg.SavedPoseLeft : Cfg.SavedPoseRight; } }

        private Tablet main, side, bottom, mini, kb, wheelT;
        private readonly Btn[] bottomBtns = new Btn[5];
        private string[] bottomTabs;
        private GameObject rootGO;
        private Transform root, group;
        private readonly List<Space> spaces = new List<Space>();
        private readonly Touch touch = new Touch();     // the pointing hand's index finger
        private readonly Touch touch2 = new Touch();    // the menu hand's index finger (only while the menu floats)
        private readonly Dictionary<PageId, Page> pages = new Dictionary<PageId, Page>();
        private PageId page = PageId.Players;
        private RectTransform pageHost, chromeRoot, tabUI;
        private CanvasGroup chromeCG, tabCG;
        private Text titleText, roomText, kbPreview;
        private Image wheelSwatch;
        private readonly Wheel[] wheels = new Wheel[3];
        private MusicView landView;

        private bool built, failed, open, hopping, poseReady;
        private float openT, tabT, colorApplyAt = -1f, colorPoll, roomPoll, notReadyLog;
        private Color lastPlayerColor = Color.clear;
        private PInfo lastHover;
        private readonly Dictionary<string, int> errs = new Dictionary<string, int>();

        private LineRenderer laser;
        private GameObject tipDot, ring;
        private Material tipMat, ringMat, laserMat;

        // floating: the whole menu leaves the hand and hangs in the air (tablet mode, or while typing a name / colour)
        private bool tabletMode, wasFloating, floatInit;
        private float handleT;

        // mini player on top of the floating menu (other pages), and the music theme
        private Text miniTitle;
        private Image miniPlayIcon;
        private string miniLast = "";
        private float miniTimer, offTimer;
        private string themeService = "";

        /// <summary>Tablet mode turns the menu sideways only for the music screen; every other page floats upright.</summary>
        private bool Landscape { get { return tabletMode && page == PageId.Music; } }
        private Handle handleL, handleR;
        private Vector3 airPos;
        private Quaternion airRot = Quaternion.identity;
        private float returnT;
        private readonly bool[] gripOn = new bool[2];                 // [0] = left hand, [1] = right hand
        private readonly Vector3[] gripPos = new Vector3[2];
        private readonly Quaternion[] gripRot = new Quaternion[2];

        private bool Floating { get { return tabletMode || Editing != EditMode.None; } }

        public bool LaserActive
        {
            get { return open && openT > 0.6f && page == PageId.Info && Selected == null && Editing == EditMode.None; }
        }

        private void Awake()
        {
            I = this;
            menuOnLeft = !string.Equals(Cfg.MenuHand.Value, "Right", StringComparison.OrdinalIgnoreCase);
            Roster.Flagged += OnFlagged;
            Friends.CameOnline += OnFriendOnline;
            MusicPlayer.Init(this);
        }

        private void OnDestroy()
        {
            if (Preview != null) Preview.Dispose();
            MusicPlayer.Shutdown();
            Roster.Flagged -= OnFlagged;
            Friends.CameOnline -= OnFriendOnline;
        }

        private void LateUpdate()
        {
            try { Frame(); }
            catch (Exception e) { Guard("frame", e); }
        }

        private void Guard(string tag, Exception e)
        {
            int n;
            errs.TryGetValue(tag, out n);
            if (n < 3) Plugin.Log.LogError("[" + tag + "] " + e);
            errs[tag] = n + 1;
        }

        private static void SetActiveIf(GameObject g, bool on)
        {
            if (g.activeSelf != on) g.SetActive(on);
        }

        // ------------------------------------------------------------ frame

        private void Frame()
        {
            float dt = Time.deltaTime;
            if (!G.Ready)
            {
                notReadyLog -= dt;
                if (notReadyLog <= 0f)
                {
                    notReadyLog = 10f;
                    Plugin.Log.LogInfo("Mod Checker waiting for the game: " + G.WhyNotReady());
                }
                return;
            }
            if (!built)
            {
                if (failed) { try { Notes.Tick(); } catch { } return; }
                try
                {
                    Build();
                    built = true;
                    Plugin.Log.LogInfo("Mod Checker menu built. Press Y on the left controller (or F9). The menu lives on your " + (menuOnLeft ? "LEFT" : "RIGHT") + " hand.");
                    Notes.Show("MOD CHECKER READY - PRESS Y", Skin.Green);
                }
                catch (Exception e)
                {
                    failed = true;
                    Plugin.Log.LogError("Menu build failed: " + e);
                    Notes.Show("MOD CHECKER FAILED - SEE BEPINEX LOG", Skin.Red);
                    return;
                }
            }

            // Background systems are isolated so that one of them failing can never stop the menu from opening.
            try { Inp.Poll(MenuOnLeft); } catch (Exception e) { Guard("input", e); }
            try { Roster.Tick(dt); } catch (Exception e) { Guard("roster", e); }
            try { Friends.Tick(Roster); } catch (Exception e) { Guard("friends", e); }
            try { Notes.Tick(); } catch (Exception e) { Guard("notes", e); }
            try { WatchPlayerColor(dt); } catch (Exception e) { Guard("theme", e); }
            HandleInput();

            openT = Mathf.MoveTowards(openT, open ? 1f : 0f, dt / (open ? 0.16f : 0.12f));
            float s = open ? Ease.OutBack(openT) : Ease.Smooth(openT);
            root.localScale = Vector3.one * Mathf.Max(0.001f, s) * Cfg.MenuScale.Value;
            bool vis = openT > 0.001f;
            if (rootGO.activeSelf != vis) rootGO.SetActive(vis);
            bool interactive = open && openT > 0.85f;
            main.Sp.Active = interactive;

            kb.Target = open && Editing == EditMode.Name;
            wheelT.Target = open && Editing == EditMode.Color;

            Transform palmHand = MenuOnLeft ? G.LeftHand : G.RightHand;     // carries the menu
            Transform pointHand = MenuOnLeft ? G.RightHand : G.LeftHand;    // points and touches
            Transform head = G.HeadT;
            if (palmHand == null || pointHand == null || head == null) return;

            if (vis)
            {
                try { UpdatePose(palmHand, head, dt); } catch (Exception e) { Guard("pose", e); }
            }

            // the music player: only looks at Spotify while a music screen is showing
            try { MusicPlayer.Tick(dt, vis); } catch (Exception e) { Guard("music", e); }

            if (!vis)
            {
                if (tipDot.activeSelf) tipDot.SetActive(false);
                laser.enabled = false;
                ring.SetActive(false);
                return;
            }

            try { AnimateTabletMode(dt); } catch (Exception e) { Guard("tabletmode", e); }
            try { UpdateMusicTheme(dt); } catch (Exception e) { Guard("musictheme", e); }
            try { UpdateTipsAndLaser(pointHand, palmHand, interactive); } catch (Exception e) { Guard("laser", e); }

            try
            {
                for (int i = 0; i < spaces.Count; i++)
                {
                    var sp = spaces[i];
                    if (!sp.Root.gameObject.activeInHierarchy) continue;
                    for (int b = 0; b < sp.Btns.Count; b++) sp.Btns[b].Refresh(dt);
                }
            }
            catch (Exception e) { Guard("buttons", e); }

            try { pages[page].Tick(dt); } catch (Exception e) { Guard("page", e); }
            try { if (tabT > 0.55f) landView.Tick(dt); } catch (Exception e) { Guard("tabletview", e); }
            try
            {
                RefreshBottom();
                RefreshMini(dt);
                bottom.Tick(dt);
                mini.Tick(dt);
                side.Tick(dt);
                kb.Tick(dt);
                wheelT.Tick(dt);
                for (int i = 0; i < 3; i++) if (wheelT.Anim > 0.001f) wheels[i].Tick(dt);
            }
            catch (Exception e) { Guard("panels", e); }

            try
            {
                string tt = pages[page].Title;
                if (tt != titleText.text) titleText.text = tt;

                Vector3 eye = head.position;
                main.UpdateFacing(eye); side.UpdateFacing(eye); bottom.UpdateFacing(eye); mini.UpdateFacing(eye);
                kb.UpdateFacing(eye); wheelT.UpdateFacing(eye);

                if (Editing == EditMode.Name) kbPreview.text = NameBuf + "_";
                if (Editing == EditMode.Color)
                {
                    wheelSwatch.color = new Color(RGB[0] / 9f, RGB[1] / 9f, RGB[2] / 9f, 1f);
                    if (colorApplyAt > 0f && Time.unscaledTime >= colorApplyAt)
                    {
                        colorApplyAt = -1f;
                        G.SetColor(RGB[0], RGB[1], RGB[2]);
                    }
                }

                roomPoll -= dt;
                if (roomPoll <= 0f)
                {
                    roomPoll = 0.5f;
                    roomText.text = G.InRoom ? G.RoomCode + "  " + G.RoomPlayers + "/" + G.RoomMax : "Not in a room";
                }
            }
            catch (Exception e) { Guard("ui", e); }
        }

        private void WatchPlayerColor(float dt)
        {
            colorPoll -= dt;
            if (colorPoll > 0f) return;
            colorPoll = 0.4f;
            if (Cfg.Theme.Value != Skin.PlayerTheme) return;
            Color c = G.LocalColor();
            if (Mathf.Abs(c.r - lastPlayerColor.r) + Mathf.Abs(c.g - lastPlayerColor.g) + Mathf.Abs(c.b - lastPlayerColor.b) > 0.02f)
            {
                lastPlayerColor = c;
                Skin.Apply(c);
            }
        }

        public void ApplyTheme(string name)
        {
            Cfg.Theme.Value = name;
            lastPlayerColor = G.LocalColor();
            Skin.Apply(lastPlayerColor);
        }

        // ------------------------------------------------------------ input

        private void HandleInput()
        {
            if (Inp.YDown)
            {
                Plugin.Log.LogInfo("Y pressed (menu open: " + open + ", tablet mode: " + tabletMode + ", editing: " + Editing + ")");
                if (Editing != EditMode.None) EndEdit(true);               // done typing (the menu flies back to your hand, unless it is in tablet mode)
                else if (tabletMode) ExitTablet();                         // tablet mode: Y goes back to hand mode
                else if (!open) SetOpen(true);
                else SetOpen(false);
            }
            if (Inp.BDown && open && Floating)                              // B brings the floating menu in front of you again
            {
                Transform hd = G.HeadT;
                if (hd != null) { PlaceInFront(hd); floatInit = true; Snd.Select(); }
            }
            if (open && Inp.XDown && page == PageId.Info && Selected != null)
            {
                Selected = null;   // X resets the selected player and brings the laser back
                Snd.Soft();
            }
        }

        private void SetOpen(bool v)
        {
            if (open == v) return;
            open = v;
            if (v) rootGO.SetActive(true);
            else
            {
                EndEdit(true);
                tabletMode = false;
                wasFloating = false;
                returnT = 0f;
            }
        }

        // ------------------------------------------------------------ pose: ONE fixed spot on the menu hand

        // The tablet rests across the index + middle fingers of the menu hand and is locked there: same spot, same
        // angle, every single time, whatever your head or hand is doing when you press Y. The spot is worked out
        // ONCE (the first time the menu opens), snapped to the hand's own axes, and saved in the config
        // ("SavedMenuPose..."). After that nothing recalculates it. To choose a different spot: hold your hand the way
        // you want, look at it, and press Configuration > Style > Reset Menu Position.
        private Vector3 localPos;
        private Quaternion localRot = Quaternion.identity;

        private static readonly Vector3[] Axes =
            { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        private static string F(float v) { return v.ToString("R", CultureInfo.InvariantCulture); }

        private void SavePose()
        {
            PoseEntry.Value = string.Join(",", new[]
            {
                F(localPos.x), F(localPos.y), F(localPos.z),
                F(localRot.x), F(localRot.y), F(localRot.z), F(localRot.w)
            });
        }

        private void LoadPose()
        {
            try
            {
                string v = PoseEntry.Value;
                if (string.IsNullOrEmpty(v)) return;
                var p = v.Split(',');
                if (p.Length != 7) return;
                var f = new float[7];
                for (int i = 0; i < 7; i++) f[i] = float.Parse(p[i], CultureInfo.InvariantCulture);
                localPos = new Vector3(f[0], f[1], f[2]);
                localRot = Quaternion.Normalize(new Quaternion(f[3], f[4], f[5], f[6]));
                poseReady = true;
            }
            catch { poseReady = false; }
        }

        public void ResetPose()
        {
            poseReady = false;
            PoseEntry.Value = "";
            Snd.Soft();
        }

        /// <summary>
        /// Picks the hand-aligned pose that faces you best right now: the screen normal is one of the hand's own axes
        /// (perpendicular to the fingers) and the top of the screen is another, so the tablet lies flat on the fingers.
        /// </summary>
        private void CalibrateToHand(Transform hand, Transform head)
        {
            Vector3 hp = hand.position;
            Quaternion hr = hand.rotation;
            Vector3 toHead = (head.position - hp).normalized;

            float flat = Mathf.InverseLerp(0.7f, 0.95f, Mathf.Abs(Vector3.Dot(toHead, head.up)));
            Vector3 viewerUp = Vector3.Slerp(head.up, head.forward, flat);

            Vector3 f = new Vector3(Cfg.FingerX.Value, Cfg.FingerY.Value, Cfg.FingerZ.Value);
            if (f.sqrMagnitude < 1e-6f) f = new Vector3(0f, -0.1f, 0f);
            Vector3 fDir = f.normalized;

            float best = float.MinValue;
            Vector3 bestN = Vector3.back;
            Quaternion bestQ = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            foreach (var n in Axes)
            {
                if (Mathf.Abs(Vector3.Dot(n, fDir)) > 0.5f) continue;        // screen normal perpendicular to the fingers
                foreach (var u in Axes)
                {
                    if (Mathf.Abs(Vector3.Dot(u, n)) > 0.5f) continue;       // screen up perpendicular to the normal
                    float score = Vector3.Dot(hr * n, toHead) * 2f + Vector3.Dot(hr * u, viewerUp);
                    if (score > best)
                    {
                        best = score;
                        bestN = n;
                        bestQ = Quaternion.LookRotation(-n, u);              // the screen faces +n (towards you)
                    }
                }
            }

            localRot = bestQ;
            localPos = f * 1.1f + bestN * 0.03f;                              // over the index + middle fingers, resting on them
        }

        private void UpdatePose(Transform hand, Transform head, float dt)
        {
            if (!poseReady)
            {
                CalibrateToHand(hand, head);
                poseReady = true;
                SavePose();
                Notes.Show("MENU POSITION SAVED", Skin.Green);
            }

            bool fl = Floating;
            if (fl && !wasFloating) { floatInit = false; gripOn[0] = false; gripOn[1] = false; }
            if (!fl && wasFloating)
            {
                returnT = 0.8f;                                               // flying back to the hand
                gripOn[0] = false; gripOn[1] = false;
                if (handleL != null) { handleL.SetGlow(false); handleR.SetGlow(false); }
            }
            wasFloating = fl;

            if (fl) { UpdateFloat(dt, head); return; }

            Quaternion hr = hand.rotation * localRot;
            Vector3 hp = hand.position + hand.rotation * localPos;
            if (returnT > 0f)
            {
                returnT -= dt;
                float f = 1f - Mathf.Exp(-10f * dt);
                root.position = Vector3.Lerp(root.position, hp, f);
                root.rotation = Quaternion.Slerp(root.rotation, hr, f);
            }
            else
            {
                root.rotation = hr;                                           // locked to the hand, no smoothing
                root.position = hp;
            }
        }

        // ------------------------------------------------------------ floating: tablet mode and typing

        /// <summary>Tablet mode (music screen): the menu turns sideways and floats in front of you.</summary>
        public void EnterTablet()
        {
            if (!open || tabletMode) return;
            tabletMode = true;
            Snd.Soft();
        }

        public void ToggleTablet()
        {
            if (tabletMode) ExitTablet(); else EnterTablet();
        }

        /// <summary>Y in tablet mode: back to hand mode.</summary>
        private void ExitTablet()
        {
            tabletMode = false;
            Snd.Soft();
        }

        /// <summary>In front of your face. Typing: a little higher, because the keyboard hangs below the menu.</summary>
        private void PlaceInFront(Transform head)
        {
            Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(head.up, Vector3.up);
            flat.Normalize();
            bool typing = Editing != EditMode.None && !tabletMode;
            airPos = head.position + flat * (typing ? 0.6f : 0.75f) + Vector3.up * (typing ? 0.22f : -0.1f);
            airRot = Quaternion.LookRotation(flat, Vector3.up);               // screen faces you, upright
        }

        /// <summary>
        /// While floating the menu stays exactly where it was put in the air; it does not follow your head or hand.
        /// In tablet mode, gripping near a handle picks it up: it then moves and turns with that hand
        /// (with both hands it follows the middle of the two).
        /// </summary>
        private void UpdateFloat(float dt, Transform head)
        {
            if (!floatInit) { PlaceInFront(head); floatInit = true; }

            int n = 0;
            if (tabletMode)
            {
                Transform[] hands = { G.LeftHand, G.RightHand };
                float reach = 0.17f * Mathf.Max(0.5f, Cfg.MenuScale.Value);
                bool glowL = false, glowR = false;
                Vector3 sumPos = Vector3.zero;
                Quaternion rotA = Quaternion.identity, rotB = Quaternion.identity;

                for (int i = 0; i < 2; i++)
                {
                    Transform hd = hands[i];
                    if (hd == null) continue;

                    bool nearL = (hd.position - handleL.T.position).sqrMagnitude < reach * reach;
                    bool nearR = (hd.position - handleR.T.position).sqrMagnitude < reach * reach;
                    if (nearL) glowL = true;
                    if (nearR) glowR = true;

                    bool gripping = Inp.Grip(i == 0) > 0.7f;
                    if (!gripping) gripOn[i] = false;
                    else if (!gripOn[i] && (nearL || nearR))
                    {
                        gripOn[i] = true;
                        gripPos[i] = Quaternion.Inverse(hd.rotation) * (root.position - hd.position);
                        gripRot[i] = Quaternion.Inverse(hd.rotation) * root.rotation;
                        Inp.Haptic(i == 0, 0.4f, 0.05f);
                    }

                    if (gripOn[i])
                    {
                        sumPos += hd.position + hd.rotation * gripPos[i];
                        Quaternion r = hd.rotation * gripRot[i];
                        if (n == 0) rotA = r; else rotB = r;
                        n++;
                    }
                }
                handleL.SetGlow(glowL || gripOn[0] || gripOn[1]);
                handleR.SetGlow(glowR || gripOn[0] || gripOn[1]);

                if (n > 0)
                {
                    airPos = sumPos / n;
                    airRot = n == 2 ? Quaternion.Slerp(rotA, rotB, 0.5f) : rotA;
                }
            }

            float f = 1f - Mathf.Exp(-(n > 0 ? 40f : 9f) * dt);
            root.position = Vector3.Lerp(root.position, airPos, f);
            root.rotation = Quaternion.Slerp(root.rotation, airRot, f);
        }

        /// <summary>
        /// Tablet mode: the whole menu rolls 90 degrees into landscape, the side panel contracts into it, and the two
        /// carry handles grow out of its sides. Leaving tablet mode reverses all of it.
        /// </summary>
        private void AnimateTabletMode(float dt)
        {
            // sideways only for the music screen; other pages float upright
            float target = Landscape ? 1f : 0f;
            tabT = Mathf.MoveTowards(tabT, target, dt / 0.75f);
            float tk = Ease.Smooth(tabT);
            group.localRotation = Quaternion.Euler(0f, 0f, 90f * tk);

            side.Target = target < 0.5f && tabT < 0.35f;

            // the two carry handles: out in tablet mode on every page, turned with the layout (always vertical)
            handleT = Mathf.MoveTowards(handleT, tabletMode ? 1f : 0f, dt / 0.5f);
            float hv = Ease.Smooth(handleT);
            handleL.SetScale(hv);
            handleR.SetScale(hv);
            Quaternion hr = Quaternion.Euler(0f, 0f, 90f * (1f - tk));
            handleL.SetPose(Vector3.Lerp(new Vector3(-0.125f, 0f, 0f), new Vector3(0f, 0.150f, 0f), tk), hr);
            handleR.SetPose(Vector3.Lerp(new Vector3(0.178f, 0f, 0f), new Vector3(0f, -0.150f, 0f), tk), hr);

            chromeCG.alpha = 1f - Ease.Smooth(Mathf.Clamp01(tabT * 3f));
            SetActiveIf(chromeRoot.gameObject, tabT < 0.34f);
            tabCG.alpha = Ease.Smooth(Mathf.Clamp01((tabT - 0.6f) / 0.4f));
            SetActiveIf(tabUI.gameObject, tabT > 0.58f);
        }

        // ------------------------------------------------------------ music: theme + mini player

        /// <summary>
        /// While music plays the whole menu takes the streaming service's colours (Spotify green + black, Apple Music
        /// red + white, ...) and the second colour moves with the music. It goes back to your theme a few seconds after
        /// the music stops.
        /// </summary>
        private void UpdateMusicTheme(float dt)
        {
            bool want = Cfg.MusicTheme.Value && MusicPlayer.Running && MusicPlayer.Playing;
            AudioLevel.Tick(dt, want);
            if (want) offTimer = 0f; else offTimer += dt;

            string svc = want ? (MusicPlayer.Service.Length > 0 ? MusicPlayer.Service : "Music") : (offTimer < 4f ? themeService : "");
            if (svc != themeService)
            {
                themeService = svc;
                Skin.Override = svc.Length > 0 ? MusicPalette.For(svc) : null;
                Skin.Apply(lastPlayerColor);
            }
            BassFx.Tick(dt, AudioLevel.Value, Skin.Reactive);
        }

        private static string Ellipsize(string t, int max)
        {
            return t.Length <= max ? t : t.Substring(0, max - 1) + "...";
        }

        /// <summary>
        /// The panel on top of the floating menu: the song name and Previous / Pause-Unpause / Next. It is there in
        /// tablet mode on every page except the music page, while a song is loaded.
        /// </summary>
        private void RefreshMini(float dt)
        {
            mini.Target = open && tabletMode && page != PageId.Music && MusicPlayer.Running && MusicPlayer.HasTrack;
            if (mini.Anim < 0.02f) return;

            string t = MusicPlayer.Title + (MusicPlayer.Artist.Length > 0 ? "  -  " + MusicPlayer.Artist : "");
            if (t != miniLast) { miniLast = t; miniTitle.text = Ellipsize(t, 34); }

            miniTimer -= dt;
            if (miniTimer > 0f) return;
            miniTimer = 0.2f;
            Sprite want = Gfx.Icon(MusicPlayer.Playing ? "pause" : "play");
            if (miniPlayIcon.sprite != want) miniPlayIcon.sprite = want;
        }

        private Image MiniButton(float x, string icon, Action tap)
        {
            var b = UI.Button(mini.Sp, mini.CvRt, x, 28, 52, 26, null, 0, tap, 7f, null, null);
            var img = UI.Icon(b.Rt, icon, 16, 3, 20);
            Skin.Bind(() => { if (img == null) return false; img.color = Skin.Text; return true; });
            return img;
        }

        private void BuildMini()
        {
            var cv = mini.CvRt;     // 200 x 64
            var bez = UI.Rect("bezel", cv, 4, 4, 192, 56);
            UI.AddImg(bez, () => new Color(Skin.Accent.r, Skin.Accent.g, Skin.Accent.b, 0.55f), 12f);
            UI.Screen(cv, 6, 6, 188, 52, 10f);
            miniTitle = UI.Label(cv, 12, 8, 176, 16, "", 11, null, TextAnchor.MiddleCenter, true);
            MiniButton(14, "prev", MusicPlayer.Previous);
            miniPlayIcon = MiniButton(74, "pause", MusicPlayer.PlayPause);
            MiniButton(134, "next", MusicPlayer.Next);
        }

        // ------------------------------------------------------------ fingertips + laser

        private void UpdateTipsAndLaser(Transform rh, Transform lh, bool interactive)
        {
            Vector3 off = new Vector3(Cfg.FingerX.Value, Cfg.FingerY.Value, Cfg.FingerZ.Value);
            Vector3 tip = rh.TransformPoint(off);

            bool showDot = openT > 0.5f && Cfg.ShowTip.Value;
            if (tipDot.activeSelf != showDot) tipDot.SetActive(showDot);
            tipDot.transform.position = tip;
            tipMat.color = Skin.Accent;

            touch.Update(tip, spaces, interactive);

            // While the menu floats (typing, or tablet mode) the menu hand's index finger works too, so BOTH index
            // fingers can type. It is switched off while the menu is on that hand, or the tablet resting on its
            // fingers would press itself.
            touch2.Update(lh.TransformPoint(off), spaces, interactive && Floating);

            bool active = LaserActive;
            laser.enabled = active;
            Hovered = null;
            if (!active) { ring.SetActive(false); lastHover = null; return; }

            // the ray points the same way as the fingertip offset, so tuning FingertipX/Y/Z in the config aims both
            Vector3 dir = rh.TransformDirection(off.sqrMagnitude > 1e-6f ? off.normalized : Vector3.down).normalized;
            float bestD = float.MaxValue, bestT = 8f;
            PInfo best = null;
            Vector3 bestAim = Vector3.zero;
            for (int i = 0; i < Roster.Players.Count; i++)
            {
                var p = Roster.Players[i];
                Vector3 aim;
                if (p.IsLocal || !p.TryAim(out aim)) continue;
                Vector3 to = aim - tip;
                float t = Vector3.Dot(to, dir);
                if (t < 0.05f || t > 80f) continue;
                float d = (to - dir * t).magnitude;
                float rad = 0.38f + t * 0.015f;
                if (d < rad && d < bestD) { bestD = d; best = p; bestT = t; bestAim = aim; }
            }
            Hovered = best;
            if (best != lastHover)
            {
                lastHover = best;
            }

            Color ac = Skin.Accent; ac.a = 0.9f;
            laserMat.color = ac;
            laser.SetPosition(0, tip);
            laser.SetPosition(1, tip + dir * bestT);

            if (best != null)
            {
                ring.SetActive(true);
                ring.transform.position = bestAim;
                Color rc = Skin.Accent; rc.a = 0.28f;
                ringMat.color = rc;
                if (Inp.TrigDown) Select(best);
            }
            else ring.SetActive(false);
        }

        // ------------------------------------------------------------ selection / pages

        public void Select(PInfo p)
        {
            Selected = p;
            Snd.Select();
            Inp.Haptic(!MenuOnLeft, 0.5f, 0.08f);
        }

        public void ShowInfo(PInfo p)
        {
            Selected = p;
            SetPage(PageId.Info);
        }

        public void SetPage(PageId id)
        {
            if (id == page) return;
            if (Editing != EditMode.None) EndEdit(true);
            pages[page].Hide();
            pages[page].Root.gameObject.SetActive(false);
            page = id;
            Snd.Select();
            pages[page].Root.gameObject.SetActive(true);
            pages[page].Show();
            titleText.text = pages[page].Title;
        }

        public void Notify(string msg, Color c) { Notes.Show(msg, c); }

        private void OnFlagged(PInfo p, bool cheat)
        {
            string mode = Cfg.ModAlerts.Value;
            if (cheat && mode == "Mods") return;
            if (!cheat && mode == "Cheats") return;
            Notes.Show(p.Name + (cheat ? " [CHEATS]" : " [MODS]"), cheat ? Skin.Red : Skin.Green);
        }

        private void OnFriendOnline(FriendEntry f)
        {
            if (Cfg.FriendAlert.Value) Notes.Show("FRIEND " + f.Name + " ONLINE", new Color(0.4f, 0.9f, 1f));
        }

        // ------------------------------------------------------------ editing (keyboard / colour wheel)

        public void BeginEdit(EditMode m)
        {
            if (Editing != EditMode.None || !open || m == EditMode.None) return;
            Editing = m;                      // the menu now floats in front of your face (see UpdatePose)
            if (m == EditMode.Name) NameBuf = G.LocalName();
            else
            {
                var c = G.ColorCode(G.LocalColor());
                for (int i = 0; i < 3; i++) { RGB[i] = c[i]; wheels[i].Set(c[i]); }
                colorApplyAt = -1f;
            }
            Snd.Soft();
        }

        /// <summary>Finishes typing / picking (Y, ENTER or DONE): applies it and the menu flies back to your hand.</summary>
        public void EndEdit(bool commit)
        {
            if (Editing == EditMode.None) return;
            var m = Editing;
            Editing = EditMode.None;
            if (!commit) return;
            if (m == EditMode.Name)
            {
                string n = G.Sanitize(NameBuf);
                if (n.Length > 0 && n != G.LocalName())
                {
                    if (!G.SetName(n)) { Notes.Show("NAME NOT ALLOWED", Skin.Red); Snd.Error(); }
                }
            }
            else if (colorApplyAt > 0f)
            {
                colorApplyAt = -1f;
                G.SetColor(RGB[0], RGB[1], RGB[2]);
            }
        }

        private void TypeChar(char c)
        {
            if (Editing != EditMode.Name) return;
            if (NameBuf.Length < 12) NameBuf += c;
        }

        private void Backspace()
        {
            if (Editing == EditMode.Name && NameBuf.Length > 0) NameBuf = NameBuf.Substring(0, NameBuf.Length - 1);
        }

        // ------------------------------------------------------------ server / friends actions

        public void ServerHop()
        {
            if (!hopping) StartCoroutine(HopCo());
        }

        private IEnumerator HopCo()
        {
            hopping = true;
            HopStatus = "Hopping...";
            object trig = G.LastJoinTrigger;
            G.Disconnect();
            float t = 0f;
            while (G.InRoom && t < 6f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(1.5f);
            bool ok = G.JoinPublic(trig);
            HopStatus = ok ? "Joining a new lobby..." : "Could not start the join";
            if (!ok) Snd.Error();
            yield return new WaitForSeconds(3f);
            HopStatus = "";
            hopping = false;
        }

        public void DoDisconnect()
        {
            if (G.Disconnect()) HopStatus = "Disconnected";
            else { HopStatus = "Disconnect failed"; Snd.Error(); }
        }

        public void JoinFriend(FriendEntry f)
        {
            StartCoroutine(JoinFriendCo(f));
        }

        private IEnumerator JoinFriendCo(FriendEntry f)
        {
            string target = f.Room;
            if (string.IsNullOrEmpty(target) && f.Raw == null) yield break;
            if (!string.IsNullOrEmpty(target) && G.InRoom && G.RoomCode == target) yield break;

            string before = G.RoomCode;
            bool viaGame = f.Raw != null && Friends.JoinViaGame(f.Raw);   // the game's own join (it handles leaving the current lobby)
            if (!viaGame)
            {
                if (string.IsNullOrEmpty(target)) yield break;
                if (G.InRoom)
                {
                    G.Disconnect();
                    float t = 0f;
                    while (G.InRoom && t < 6f) { t += Time.deltaTime; yield return null; }
                    yield return new WaitForSeconds(1.5f);
                }
                G.JoinRoom(target);
            }

            float w = 0f;
            bool joined = false;
            while (w < 8f && !joined)
            {
                w += Time.deltaTime;
                joined = !string.IsNullOrEmpty(target) ? G.RoomCode == target : (G.InRoom && G.RoomCode != before);
                yield return null;
            }
            if (!joined)
            {
                f.FullUntil = Time.time + 3f;   // "LOBBY FULL" on the button for 3 seconds
                Snd.Error();
            }
        }

        // ------------------------------------------------------------ build

        private GameObject Prim(PrimitiveType type, float scale, Color c, out Material mat)
        {
            var g = GameObject.CreatePrimitive(type);
            var col = g.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            g.transform.localScale = Vector3.one * scale;
            var rend = g.GetComponent<Renderer>();
            mat = Gfx.FlatMat(c);
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            DontDestroyOnLoad(g);
            return g;
        }

        private void Build()
        {
            lastPlayerColor = G.LocalColor();
            Skin.Apply(lastPlayerColor);

            rootGO = new GameObject("ModCheckerMenu");
            DontDestroyOnLoad(rootGO);
            root = rootGO.transform;

            // everything sits in a group so the whole menu can roll sideways in tablet mode
            group = new GameObject("Group").transform;
            group.SetParent(root, false);

            // slimmer tablets (18 mm)
            main = Tablet.Make(group, "Main", 200f, 260f, 18f, 18f);

            side = Tablet.Make(group, "Side", 44f, 260f, 18f, 18f);        // side panel on the right of the menu
            side.Shown = new Vector3(0.130f, 0f, 0f);
            side.Hidden = new Vector3(0.02f, 0f, 0.03f);                    // contracts into the menu
            side.HiddenScale = 0.05f;
            side.Anim = 1f; side.Target = true;
            side.T.localPosition = side.Shown;

            // bottom panel: the tabs of the current page; it only slides out on pages that have sub pages
            bottom = Tablet.Make(group, "Bottom", 200f, 40f, 14f, 14f);
            bottom.Shown = new Vector3(0f, -0.158f, 0f);
            bottom.Hidden = new Vector3(0f, -0.10f, 0.02f);

            // mini player on TOP of the menu: song name + Previous / Pause-Unpause / Next (tablet mode, other pages)
            mini = Tablet.Make(group, "MiniPlayer", 200f, 64f, 14f, 14f);
            mini.Shown = new Vector3(0f, 0.170f, 0f);
            mini.Hidden = new Vector3(0f, 0.10f, 0.02f);

            // two handles on the sides, to carry the tablet in tablet mode (they are only visible then)
            handleL = Handle.Make(group, "HandleLeft", new Vector3(0f, 0.150f, 0f));
            handleR = Handle.Make(group, "HandleRight", new Vector3(0f, -0.150f, 0f));

            // the editors sit below the bottom panel and are larger than the menu so keys and wheels are easy to hit
            kb = Tablet.Make(group, "Keyboard", 240f, 196f, 14f, 16f);
            kb.Size = 1.15f;
            kb.Shown = new Vector3(0f, -(0.186f + 0.196f * kb.Size / 2f), 0f);
            kb.Hidden = new Vector3(0f, -0.075f, 0.03f);

            wheelT = Tablet.Make(group, "ColorPicker", 200f, 150f, 14f, 16f);
            wheelT.Size = 1.15f;
            wheelT.Shown = new Vector3(0f, -(0.186f + 0.150f * wheelT.Size / 2f), 0f);
            wheelT.Hidden = new Vector3(0f, -0.075f, 0.03f);

            spaces.Add(main.Sp); spaces.Add(side.Sp); spaces.Add(bottom.Sp); spaces.Add(mini.Sp); spaces.Add(kb.Sp); spaces.Add(wheelT.Sp);

            if (Cfg.LivePreview.Value) Preview = new LivePreview();
            LoadPose();

            BuildMainChrome();
            BuildSide();
            BuildBottom();
            BuildMini();
            BuildKeyboard();
            BuildWheelTablet();
            BuildPages();

            laser = new GameObject("ModCheckerLaser").AddComponent<LineRenderer>();
            DontDestroyOnLoad(laser.gameObject);
            laserMat = Gfx.FlatMat(Color.white);
            laser.sharedMaterial = laserMat;
            laser.positionCount = 2;
            laser.useWorldSpace = true;
            laser.startWidth = 0.006f;
            laser.endWidth = 0.003f;
            laser.numCapVertices = 4;
            laser.shadowCastingMode = ShadowCastingMode.Off;
            laser.enabled = false;

            tipDot = Prim(PrimitiveType.Sphere, 0.014f, Skin.Accent, out tipMat);
            ring = Prim(PrimitiveType.Sphere, 0.45f, new Color(1f, 1f, 1f, 0.25f), out ringMat);
            ring.SetActive(false);
            tipDot.SetActive(false);

            touch.Pressed = b => { Snd.Tick(); Inp.Haptic(!MenuOnLeft, 0.25f, 0.04f); };     // pointing hand
            touch2.Pressed = b => { Snd.Tick(); Inp.Haptic(MenuOnLeft, 0.25f, 0.04f); };     // menu hand
            rootGO.SetActive(false);
        }

        private void BuildMainChrome()
        {
            var cv = main.CvRt;
            UI.Screen(cv, 6, 6, 188, 248, 14f);

            // portrait UI (hand mode)
            chromeRoot = UI.Rect("chrome", cv, 0, 0, 200, 260);
            chromeCG = chromeRoot.gameObject.AddComponent<CanvasGroup>();
            titleText = UI.Label(chromeRoot, 16, 8, 130, 20, "PLAYERS", 13, null, TextAnchor.MiddleLeft, true);
            roomText = UI.Label(chromeRoot, 16, 27, 130, 11, "", 8, () => Skin.Sub, TextAnchor.MiddleLeft, false);

            // tablet mode button (icon only), on every page: the menu floats in front of you with carry handles
            var tb = UI.Button(main.Sp, chromeRoot, 154, 8, 32, 26, null, 0, ToggleTablet, 7f, null, null);
            var tbi = UI.Icon(tb.Rt, "tablet", 6, 3, 20);
            Skin.Bind(() => { if (tbi == null) return false; tbi.color = Skin.Text; return true; });
            pageHost = UI.Rect("pages", chromeRoot, 12, 40, 176, 208);

            // landscape UI for tablet mode: laid out 244 x 176 and turned -90 degrees so that it reads upright once
            // the whole menu has rolled +90 degrees
            tabUI = UI.Rect("tablet_ui", cv, 0, 0, 244, 176);
            tabUI.pivot = new Vector2(0.5f, 0.5f);
            tabUI.anchoredPosition = new Vector2(100f, -130f);
            tabUI.localRotation = Quaternion.Euler(0f, 0f, -90f);
            tabCG = tabUI.gameObject.AddComponent<CanvasGroup>();
        }

        private void BuildSide()
        {
            var cv = side.CvRt;
            var bez = UI.Rect("bezel", cv, 2, 4, 40, 252);
            UI.AddImg(bez, () => new Color(Skin.Accent.r, Skin.Accent.g, Skin.Accent.b, 0.55f), 14f);
            UI.Screen(cv, 4, 6, 36, 248, 12f);

            // icons only, no text
            string[] icons = { "players", "info", "friends", "config", "music" };
            PageId[] ids = { PageId.Players, PageId.Info, PageId.Friends, PageId.Config, PageId.Music };
            for (int i = 0; i < 5; i++)
            {
                PageId id = ids[i];
                var b = UI.Button(side.Sp, cv, 5, 10 + i * 48, 34, 44, null, 0, () => SetPage(id), 8f,
                    () => Color.Lerp(Skin.Button, Skin.Accent, page == id ? 0.7f : 0f), null);
                var ic = UI.Icon(b.Rt, icons[i], 4, 9, 26);
                Skin.Bind(() => { if (ic == null) return false; ic.color = Skin.Text; return true; });
            }
        }

        private void BuildBottom()
        {
            var cv = bottom.CvRt;
            var bez = UI.Rect("bezel", cv, 4, 4, 192, 32);
            UI.AddImg(bez, () => new Color(Skin.Accent.r, Skin.Accent.g, Skin.Accent.b, 0.55f), 12f);
            UI.Screen(cv, 6, 6, 188, 28, 10f);
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                bottomBtns[i] = UI.Button(bottom.Sp, cv, 10, 9, 30, 22, "", 7, () => { pages[page].SelectSub(idx); Snd.Select(); }, 6f,
                    () => Color.Lerp(Skin.Button, Skin.Accent, (pages.ContainsKey(page) && pages[page].SubIndex == idx) ? 0.7f : 0f), null);
            }
        }

        private void RefreshBottom()
        {
            string[] tabs = pages[page].SubTabs;
            if (!ReferenceEquals(tabs, bottomTabs))
            {
                bottomTabs = tabs;
                int n = tabs == null ? 0 : Mathf.Min(5, tabs.Length);
                float gap = 3f;
                float w = n > 0 ? (180f - gap * (n - 1)) / n : 30f;
                for (int i = 0; i < 5; i++)
                {
                    bool vis = i < n;
                    bottomBtns[i].Rt.gameObject.SetActive(vis);
                    if (!vis) continue;
                    bottomBtns[i].Rt.anchoredPosition = new Vector2(10f + i * (w + gap), -9f);
                    bottomBtns[i].Rt.sizeDelta = new Vector2(w, 22f);
                    bottomBtns[i].Label.text = tabs[i];
                }
            }
            bottom.Target = open && bottomTabs != null && !Landscape;
        }

        private void BuildKeyboard()
        {
            // 240 x 196 canvas: bigger keys with proper gaps between them
            var cv = kb.CvRt;
            var bez = UI.Rect("bezel", cv, 4, 4, 232, 188);
            UI.AddImg(bez, () => new Color(Skin.Accent.r, Skin.Accent.g, Skin.Accent.b, 0.55f), 16f);
            UI.Screen(cv, 6, 6, 228, 184, 14f);
            kbPreview = UI.Label(cv, 14, 12, 212, 28, "", 18, () => Skin.Accent, TextAnchor.MiddleCenter, true);

            string[] rows = { "1234567890", "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" };
            const float kw = 18f, kh = 22f, gap = 4f, delW = 30f;
            for (int r = 0; r < rows.Length; r++)
            {
                string row = rows[r];
                float rowW = row.Length * kw + (row.Length - 1) * gap + (r == 3 ? gap + delW : 0f);
                float x0 = (240f - rowW) / 2f;
                float y = 46f + r * 28f;
                for (int i = 0; i < row.Length; i++)
                {
                    char ch = row[i];
                    UI.Button(kb.Sp, cv, x0 + i * (kw + gap), y, kw, kh, ch.ToString(), 12, () => TypeChar(ch), 5f, null, null);
                }
                if (r == 3)
                    UI.Button(kb.Sp, cv, x0 + row.Length * (kw + gap), y, delW, kh, "DEL", 9, Backspace, 5f, null, null);
            }
            UI.Button(kb.Sp, cv, 14, 162, 80, 22, "CLEAR", 10, () => { if (Editing == EditMode.Name) NameBuf = ""; }, 5f, null, null);
            UI.Button(kb.Sp, cv, 146, 162, 80, 22, "ENTER", 10, () => EndEdit(true), 5f,
                () => Color.Lerp(Skin.Button, Skin.Accent, 0.55f), null);
        }

        private void BuildWheelTablet()
        {
            var cv = wheelT.CvRt;
            var bez = UI.Rect("bezel", cv, 4, 4, 192, 142);
            UI.AddImg(bez, () => new Color(Skin.Accent.r, Skin.Accent.g, Skin.Accent.b, 0.55f), 16f);
            UI.Screen(cv, 6, 6, 188, 138, 14f);
            UI.Label(cv, 12, 10, 176, 16, "COLOR CODE (0-9)   Y = done", 9, () => Skin.Sub, TextAnchor.MiddleCenter, true);
            string[] names = { "R", "G", "B" };
            for (int i = 0; i < 3; i++)
            {
                float x = 14f + i * 62f;
                UI.Label(cv, x, 26, 56, 12, names[i], 9, () => Skin.Accent, TextAnchor.MiddleCenter, true);
                int idx = i;
                wheels[i] = new Wheel(wheelT.Sp, cv, x, 38, 56, 78);
                wheels[i].Changed = v => { RGB[idx] = v; colorApplyAt = Time.unscaledTime + 0.35f; };
            }
            var sw = UI.Rect("swatch", cv, 12, 122, 118, 16);
            wheelSwatch = UI.AddImg(sw, null, 6f);
            UI.Button(wheelT.Sp, cv, 136, 120, 52, 20, "DONE", 9, () => EndEdit(true), 5f,
                () => Color.Lerp(Skin.Button, Skin.Accent, 0.55f), null);
        }


        private void BuildPages()
        {
            pages[PageId.Players] = new PlayersPage(this, UI.Rect("p_players", pageHost, 0, 0, 176, 208));
            pages[PageId.Info] = new InfoPage(this, UI.Rect("p_info", pageHost, 0, 0, 176, 208));
            pages[PageId.Friends] = new FriendsPage(this, UI.Rect("p_friends", pageHost, 0, 0, 176, 208));
            pages[PageId.Config] = new ConfigPage(this, UI.Rect("p_config", pageHost, 0, 0, 176, 208));
            pages[PageId.Music] = new MusicPage(this, UI.Rect("p_music", pageHost, 0, 0, 176, 208));

            foreach (var kv in pages) kv.Value.Root.gameObject.SetActive(kv.Key == page);

            // the same music player, landscape, with the bigger album picture on the right (tablet mode)
            landView = new MusicView(this, tabUI, true);
            tabUI.gameObject.SetActive(false);

            titleText.text = pages[page].Title;
            pages[page].Show();
        }
    }
}