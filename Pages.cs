using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace ModChecker
{
    // ===================================================================== players

    internal class PlayerRow : Item
    {
        private readonly PInfo p;
        private readonly Text name, sub, flag, ping;
        private readonly Image box;
        private string lName, lPlat, lFlag;
        private int lPing = -2;

        public PlayerRow(Host h, MenuController m, PInfo player)
        {
            p = player;
            H = 34f;
            Rt = UI.Rect("player", h.Parent, 0, 0, h.W, H);
            var b = UI.Button(h.Sp, Rt, 0, 0, h.W, H, null, 0, () => m.ShowInfo(player), 7f, null, null);
            b.Proxy = h.Proxy;
            name = UI.Label(b.Rt, 9, 3, h.W - 70, 16, "", 12, null, TextAnchor.MiddleLeft, true);
            sub = UI.Label(b.Rt, 9, 19, 100, 13, "", 9, () => Skin.Sub, TextAnchor.MiddleLeft, false);
            box = UI.AddImg(UI.Rect("color", b.Rt, 60, 21, 12, 10), null, 3f);
            flag = UI.LabelC(b.Rt, h.W - 62, 3, 54, 14, "", 9, Color.white, TextAnchor.MiddleRight, true);
            ping = UI.Label(b.Rt, h.W - 62, 19, 54, 13, "", 9, () => Skin.Sub, TextAnchor.MiddleRight, false);
        }

        public override void Tick(float dt)
        {
            string nm = p.IsLocal ? p.Name + " (you)" : p.Name;
            if (nm != lName) { lName = nm; name.text = nm; }
            if (p.Platform != lPlat)
            {
                lPlat = p.Platform;
                sub.text = p.Platform;
                box.rectTransform.anchoredPosition = new Vector2(9f + sub.preferredWidth + 6f, -21f);   // colour box right next to the platform
            }
            box.color = p.Color;
            if (p.Ping != lPing) { lPing = p.Ping; ping.text = p.Ping >= 0 ? p.Ping + " ms" : ""; }
            string fl = p.Cheats.Count > 0 ? "CHEATS" : (p.Mods.Count > 0 ? "MODS" : "");
            if (fl != lFlag)
            {
                lFlag = fl;
                flag.text = fl;
                flag.color = p.Cheats.Count > 0 ? Skin.Red : Skin.Green;
            }
        }
    }

    internal class PlayersPage : Page
    {
        private readonly ScrollView sv;
        private readonly Dictionary<string, PlayerRow> rows = new Dictionary<string, PlayerRow>();
        private readonly Text empty;
        private int ver = -1;

        public PlayersPage(MenuController m, RectTransform root) : base(m, root)
        {
            sv = new ScrollView(m.Main.Sp, root, 0, 0, 176, 208);
            empty = UI.Label(root, 8, 70, 160, 60, "No players found.\nJoin a room to see everyone.", 11, () => Skin.Sub, TextAnchor.MiddleCenter, false);
        }

        public override string Title { get { return "PLAYERS"; } }

        public override void Tick(float dt)
        {
            var ros = M.Roster;
            if (ros.Version != ver) { ver = ros.Version; Rebuild(); }
            bool none = ros.Players.Count == 0;
            if (empty.gameObject.activeSelf != none) empty.gameObject.SetActive(none);
            sv.Tick(dt);
        }

        private void Rebuild()
        {
            var keep = new HashSet<string>();
            foreach (var p in M.Roster.Players) keep.Add(p.Key);
            foreach (var key in new List<string>(rows.Keys))
                if (!keep.Contains(key)) { rows[key].Kill(); rows.Remove(key); }

            sv.Items.Clear();
            foreach (var p in M.Roster.Players)
            {
                PlayerRow r;
                if (!rows.TryGetValue(p.Key, out r)) { r = new PlayerRow(sv.Hst, M, p); rows[p.Key] = r; }
                sv.Items.Add(r);
            }
        }
    }

    // ===================================================================== information

    internal class RowHeader : Item
    {
        private readonly PInfo p;
        private readonly Image icon;
        private readonly Text name;
        private string last;

        public RowHeader(Host h, PInfo player, LivePreview preview)
        {
            p = player;
            H = 100f;
            Rt = UI.Rect("header", h.Parent, 0, 0, h.W, H);
            UI.AddImg(Rt, () => Skin.Panel, 7f);

            if (preview != null && !player.IsLocal)
            {
                // the selected player, live and moving (a camera renders their gorilla into this texture)
                var pr = UI.Rect("preview", Rt, 6, 6, 88, 88);
                var raw = pr.gameObject.AddComponent<RawImage>();
                raw.texture = preview.Tex;
                raw.raycastTarget = false;
                UI.LabelC(Rt, 10, 8, 40, 12, "LIVE", 8, Skin.Red, TextAnchor.MiddleLeft, true);
            }
            else icon = UI.Icon(Rt, "gorilla", 12, 12, 76);

            UI.Label(Rt, 102, 12, h.W - 108, 14, "PLAYER", 8, () => Skin.Sub, TextAnchor.MiddleLeft, false);
            name = UI.LabelC(Rt, 102, 28, h.W - 108, 28, "", 15, Color.white, TextAnchor.MiddleLeft, true);
        }

        public override void Tick(float dt)
        {
            if (icon != null) icon.color = p.Color;
            if (p.Name != last) { last = p.Name; name.text = p.Name; }
        }
    }

    /// <summary>
    /// Renders the selected player's gorilla (their real rig, moving in real time) into a texture shown in the Information page.
    /// A second camera is placed in front of their head and looks at them; it only renders while the page is open.
    /// </summary>
    internal class LivePreview
    {
        public readonly RenderTexture Tex;
        private readonly GameObject go;
        private readonly Camera cam;
        private int frame;

        public LivePreview()
        {
            Tex = new RenderTexture(256, 256, 16);
            Tex.name = "ModCheckerPreview";
            go = new GameObject("ModCheckerPreviewCam");
            UnityEngine.Object.DontDestroyOnLoad(go);
            cam = go.AddComponent<Camera>();
            cam.enabled = false;                       // rendered by hand, only when needed
            cam.targetTexture = Tex;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 40f;
            cam.depth = -50f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
        }

        public void Tick(PInfo p)
        {
            if (p == null || R.Dead(p.HeadT)) return;
            if ((++frame & 1) != 0) return;            // every other frame is plenty
            Transform h = p.HeadT;
            Vector3 pos = h.position + h.forward * 0.85f + Vector3.up * 0.02f;
            cam.backgroundColor = Skin.Bottom;
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation((h.position + Vector3.down * 0.14f) - pos, Vector3.up);
            cam.Render();
        }

        public void Dispose()
        {
            if (go != null) UnityEngine.Object.Destroy(go);
            if (Tex != null) Tex.Release();
        }
    }

    internal class InfoPage : Page
    {
        private readonly ScrollView sv;
        private readonly RectTransform prompt;
        private readonly Text promptName;
        private PInfo shown;
        private RowExpand mods, cheats;
        private int modsVer = -1;

        public InfoPage(MenuController m, RectTransform root) : base(m, root)
        {
            sv = new ScrollView(m.Main.Sp, root, 0, 0, 176, 208);
            prompt = UI.Rect("prompt", root, 0, 0, 176, 208);
            UI.Label(prompt, 0, 36, 176, 22, "AIM AT A PLAYER", 14, () => Skin.Accent, TextAnchor.MiddleCenter, true);
            UI.Label(prompt, 0, 62, 176, 40, "Point with your right index finger\nand pull the right trigger", 10, () => Skin.Sub, TextAnchor.MiddleCenter, false);
            promptName = UI.Label(prompt, 0, 112, 176, 24, "", 16, null, TextAnchor.MiddleCenter, true);
            UI.Label(prompt, 0, 168, 176, 20, "X = reset the selected player", 9, () => Skin.Sub, TextAnchor.MiddleCenter, false);
        }

        public override string Title { get { return "INFORMATION"; } }

        public override void Tick(float dt)
        {
            if (M.Selected != null && !M.Roster.Players.Contains(M.Selected))
            {
                M.Selected = null;
                M.Notify("Selected player left", new Color(0.8f, 0.8f, 0.8f));
            }
            if (M.Selected != shown) { shown = M.Selected; BuildDetail(); }

            bool has = shown != null;
            if (prompt.gameObject.activeSelf == has) prompt.gameObject.SetActive(!has);
            if (sv.Root.gameObject.activeSelf != has) sv.Root.gameObject.SetActive(has);

            if (!has)
            {
                promptName.text = M.Hovered != null ? M.Hovered.Name : "";
                return;
            }
            if (M.Preview != null && !shown.IsLocal) M.Preview.Tick(shown);
            if (shown.SigVersion != modsVer)
            {
                modsVer = shown.SigVersion;
                Fill(mods, shown.Mods, Skin.Green);
                Fill(cheats, shown.Cheats, Skin.Red);
            }
            sv.Tick(dt);
        }

        private static string JoinedText(PInfo p)
        {
            var span = DateTime.Now - p.Joined;
            string ago = span.TotalMinutes < 1 ? Mathf.FloorToInt((float)span.TotalSeconds) + "s"
                       : span.TotalHours < 1 ? Mathf.FloorToInt((float)span.TotalMinutes) + "m"
                       : (int)span.TotalHours + "h";
            return p.Joined.ToString("HH:mm:ss") + " (" + ago + ")";
        }

        private static void Fill(RowExpand e, List<Sig> list, Color col)
        {
            if (e == null) return;
            e.ClearKids();
            if (list.Count == 0)
            {
                e.Add(new RowText(e.Inner, () => "None detected", 10, () => Skin.Sub, 18f, TextAnchor.MiddleLeft));
                return;
            }
            foreach (var s in list)
            {
                string nm = s.Name;
                e.Add(new RowText(e.Inner, () => nm, 10, () => col, 18f, TextAnchor.MiddleLeft));
            }
        }

        private void BuildDetail()
        {
            sv.Clear();
            mods = null; cheats = null; modsVer = -1;
            var p = shown;
            if (p == null) return;
            var h = sv.Hst;

            sv.Add(new RowHeader(h, p, M.Preview));
            sv.Add(new RowKV(h, "Name", () => p.Name, null, null, 20f));
            sv.Add(new RowKV(h, "Platform", () => p.Platform, null, null, 20f));
            sv.Add(new RowKV(h, "FPS", () => p.Fps > 0 ? p.Fps.ToString() : "--", null, null, 20f));
            sv.Add(new RowKV(h, "Ping", () => p.Ping >= 0 ? p.Ping + " ms" : "N/A", null, null, 20f));
            sv.Add(new RowKV(h, "Color code", () => p.CodeText, () => p.Color, null, 20f));
            sv.Add(new RowKV(h, "Joined", () => JoinedText(p), null, null, 20f));
            sv.Add(new RowKV(h, "Camera movement", () => Mathf.RoundToInt(p.HeadDps) + " deg/s", null, null, 20f));
            sv.Add(new RowKV(h, "Move speed", () => p.Speed.ToString("0.0") + " m/s", null, null, 20f));

            mods = sv.Add(new RowExpand(h, () => "Mods (" + p.Mods.Count + ")", () => Skin.Green, null, 26f));
            cheats = sv.Add(new RowExpand(h, () => "Cheats (" + p.Cheats.Count + ")", () => Skin.Red, null, 26f));

            if (p.IsLocal) return;

            var rep = sv.Add(new RowExpand(h, () => "Report", null, () => new Color(0.45f, 0.14f, 0.14f), 26f));
            string[] labels = { "Hate Speech", "Toxicity", "Cheating" };
            string[] ids = { "HateSpeech", "Toxicity", "Cheating" };
            for (int i = 0; i < 3; i++)
            {
                string label = labels[i], id = ids[i];
                rep.Add(new RowButton(rep.Inner, () => label, () =>
                {
                    bool ok = G.Report(p, id);
                    M.Notify(ok ? "Reported " + p.Name + ": " + label : "Report failed (game API changed)", ok ? Skin.Green : Skin.Red);
                    rep.Collapse();
                }, null, null, 24f, null));
            }
            rep.Add(new RowButton(rep.Inner, () => "Cancel", () => rep.Collapse(), null, null, 24f, null));

            sv.Add(new RowButton(h, () => p.Muted ? "Unmute" : "Mute", () =>
            {
                p.Muted = !p.Muted;
                if (p.UserId.Length > 0)
                {
                    if (p.Muted) M.Roster.MutedIds.Add(p.UserId); else M.Roster.MutedIds.Remove(p.UserId);
                }
                G.SetMute(p, p.Muted);
            }, null, null, 26f, null));

            sv.Add(new RowButton(h, () => Friends.Has(p.UserId) ? "Remove Friend" : "Add Friend", () => Friends.Toggle(p),
                null, null, 26f, () => p.UserId.Length > 0));
        }
    }

    // ===================================================================== friends

    internal class FriendRow : Item
    {
        private readonly FriendEntry e;
        private readonly Image icon;
        private readonly Text full, lbl, status;
        private string lLbl, lStatus;

        public FriendRow(Host h, MenuController m, FriendEntry entry)
        {
            e = entry;
            H = 38f;
            Rt = UI.Rect("friend", h.Parent, 0, 0, h.W, H);
            UI.AddImg(Rt, () => Skin.Panel, 7f);

            // logo-only button (no name on it); it shows LOBBY FULL for 3 s when the join fails
            var logo = UI.Button(h.Sp, Rt, 4, 4, 48, 30, null, 0, () => { if (e.Online) m.JoinFriend(e); }, 7f,
                () => e.FullUntil > Time.time ? new Color(0.6f, 0.1f, 0.1f) : Skin.Button, null);
            logo.Proxy = h.Proxy;
            icon = UI.Icon(logo.Rt, "gorilla", 12, 3, 24);
            full = UI.LabelFill(logo.Rt, "LOBBY\nFULL", 8, null, TextAnchor.MiddleCenter, true);
            full.gameObject.SetActive(false);

            lbl = UI.LabelC(Rt, 58, 5, h.W - 64, 16, "", 11, Color.white, TextAnchor.MiddleLeft, true);
            lbl.supportRichText = true;
            status = UI.Label(Rt, 58, 21, h.W - 64, 14, "", 9, () => Skin.Sub, TextAnchor.MiddleLeft, false);
        }

        public override void Tick(float dt)
        {
            bool isFull = e.FullUntil > Time.time;
            if (full.gameObject.activeSelf != isFull) full.gameObject.SetActive(isFull);
            if (icon.gameObject.activeSelf == isFull) icon.gameObject.SetActive(!isFull);
            icon.color = e.Online ? e.Color : new Color(e.Color.r, e.Color.g, e.Color.b, 0.4f);

            string l = e.Name + " <color=#" + (e.Online ? "59ff80" : "9a9a9a") + ">[" + (e.Online ? "Online" : "Offline") + "]</color>";
            if (l != lLbl) { lLbl = l; lbl.text = l; }

            string s = isFull ? "LOBBY FULL"
                     : (e.Online ? (!string.IsNullOrEmpty(e.Room) && e.Room == G.RoomCode ? "In your lobby" : "Tap the logo to join") : "Offline");
            if (s != lStatus) { lStatus = s; status.text = s; }
        }
    }

    internal class FriendsPage : Page
    {
        private readonly ScrollView sv;
        private readonly Text empty;
        private int ver = -1;

        public FriendsPage(MenuController m, RectTransform root) : base(m, root)
        {
            sv = new ScrollView(m.Main.Sp, root, 0, 0, 176, 208);
            empty = UI.Label(root, 8, 60, 160, 90, "No friends found.\nYour Gorilla Tag friends appear here\nautomatically. You can also press\nAdd Friend on a player's Information page.", 10, () => Skin.Sub, TextAnchor.MiddleCenter, false);
        }

        public override string Title { get { return "FRIENDS"; } }

        public override void Tick(float dt)
        {
            if (Friends.Version != ver)
            {
                ver = Friends.Version;
                sv.Clear();
                foreach (var f in Friends.List) sv.Add(new FriendRow(sv.Hst, M, f));
            }
            bool none = Friends.List.Count == 0;
            if (empty.gameObject.activeSelf != none) empty.gameObject.SetActive(none);
            sv.Tick(dt);
        }
    }

    // ===================================================================== music (Spotify)

    /// <summary>(any streaming service) Hand mode: album picture on top, song details, progress and Previous / Pause-Play / Next buttons.</summary>
    internal class MusicPage : Page
    {
        private readonly MusicView view;

        public MusicPage(MenuController m, RectTransform root) : base(m, root)
        {
            view = new MusicView(m, root, false);
        }

        public override string Title { get { return "MUSIC"; } }
        public override void Tick(float dt) { view.Tick(dt); }
    }

    // ===================================================================== configuration

    internal class ConfigPage : Page
    {
        // the tabs live on the bottom panel; SERVER is split into HOP and LOBBY
        private static readonly string[] SubNames = { "STYLE", "GORILLA", "HOP", "LOBBY", "ALERTS" };
        private static readonly string[] AlertModes = { "Mods", "Cheats", "All" };

        private int sub;
        private bool rain;
        private readonly ScrollView styleV, gorV, hopV, lobbyV, alertV;

        public ConfigPage(MenuController m, RectTransform root) : base(m, root)
        {
            var sp = m.Main.Sp;
            styleV = new ScrollView(sp, root, 0, 0, 176, 208);
            gorV = new ScrollView(sp, root, 0, 0, 176, 208);
            hopV = new ScrollView(sp, root, 0, 0, 176, 208);
            lobbyV = new ScrollView(sp, root, 0, 0, 176, 208);
            alertV = new ScrollView(sp, root, 0, 0, 176, 208);

            BuildStyle();
            BuildGorilla();
            BuildHop();
            BuildLobby();
            BuildAlerts();
        }

        public override string Title { get { return "CONFIG - " + SubNames[sub]; } }
        public override string[] SubTabs { get { return SubNames; } }
        public override int SubIndex { get { return sub; } }
        public override void SelectSub(int index) { SetSub(index); }

        public override void Hide() { M.EndEdit(true); }

        private void SetSub(int i)
        {
            if (i == sub || i < 0 || i >= SubNames.Length) return;
            if (sub == 1) M.EndEdit(true);   // leaving the Gorilla options retracts keyboard / wheel
            sub = i;
        }

        private static void SetA(Component c, bool on)
        {
            if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        public override void Tick(float dt)
        {
            SetA(styleV.Root, sub == 0);
            SetA(gorV.Root, sub == 1);
            SetA(hopV.Root, sub == 2);
            SetA(lobbyV.Root, sub == 3);
            SetA(alertV.Root, sub == 4);

            switch (sub)
            {
                case 0: styleV.Tick(dt); break;
                case 1: gorV.Tick(dt); break;
                case 2: hopV.Tick(dt); break;
                case 3: lobbyV.Tick(dt); break;
                default: alertV.Tick(dt); break;
            }
        }

        private static BtnSpec Spec(string text, Action tap)
        {
            return new BtnSpec { Text = () => text, Tap = tap };
        }

        private static Color ThemeColor(string nm)
        {
            bool sel = Cfg.Theme.Value == nm;
            if (nm == Skin.PlayerTheme)
            {
                Color c = G.LocalColor();
                return sel ? Color.Lerp(c, Color.white, 0.25f) : Color.Lerp(c, Color.black, 0.45f);
            }
            var t = Skin.Find(nm);
            return t == null ? Skin.Button : Skin.ThemeButton(t, sel);
        }

        // ---------------------------------------------------------------- STYLE: theme, weather, time

        private void BuildStyle()
        {
            var h = styleV.Hst;
            styleV.Add(new RowSection(h, "THEME"));
            var names = new List<string>();
            names.Add(Skin.PlayerTheme);
            foreach (var t in Skin.Themes) names.Add(t.Name);
            for (int i = 0; i < names.Count; i += 3)
            {
                var specs = new List<BtnSpec>();
                for (int j = i; j < Mathf.Min(i + 3, names.Count); j++)
                {
                    string nm = names[j];
                    specs.Add(new BtnSpec
                    {
                        Text = () => nm == Skin.PlayerTheme ? "Player" : nm,
                        Tap = () => M.ApplyTheme(nm),
                        Col = () => ThemeColor(nm),
                        Size = 8
                    });
                }
                styleV.Add(new RowButtons(h, 24f, specs.ToArray()));
            }

            styleV.Add(new RowSection(h, "WEATHER"));
            styleV.Add(new RowButton(h, () => "Rain: " + (rain ? "On" : "Off"), () => { rain = !rain; G.SetRain(rain); }, null, null, 26f, null));

            styleV.Add(new RowSection(h, "TIME"));
            styleV.Add(new RowStepper(h, "Time", () => (G.TimeIndex + 1) + "/" + G.TimeSlots,
                () => G.SetTime(G.TimeIndex - 1), () => G.SetTime(G.TimeIndex + 1)));
            styleV.Add(new RowButtons(h, 24f, Spec("Morning", () => G.SetTime(G.SlotMorning)), Spec("Noon", () => G.SetTime(G.SlotNoon))));
            styleV.Add(new RowButtons(h, 24f, Spec("Evening", () => G.SetTime(G.SlotEvening)), Spec("Midnight", () => G.SetTime(G.SlotMidnight))));

            styleV.Add(new RowSection(h, "MENU POSITION"));
            styleV.Add(new RowButton(h, () => "Reset Menu Position", () => M.ResetPose(), null, null, 26f, null));
            styleV.Add(new RowText(h, () => "The menu always sits in one spot on\nyour left hand. To move it: hold your\nhand how you want, look at it, press this.", 9, () => Skin.Sub, 40f, TextAnchor.UpperLeft));
        }

        // ---------------------------------------------------------------- GORILLA: name + colour

        private static string RgbText(int[] c) { return c[0] + "  " + c[1] + "  " + c[2]; }

        private void BuildGorilla()
        {
            var h = gorV.Hst;
            gorV.Add(new RowSection(h, "NAME"));
            gorV.Add(new RowKV(h, "Name", () => M.Editing == EditMode.Name ? M.NameBuf + "_" : G.LocalName(), null, null, 22f));
            gorV.Add(new RowButton(h, () => M.Editing == EditMode.Name ? "Editing... (Y = done)" : "Edit Name",
                () => M.BeginEdit(EditMode.Name), null, null, 28f, () => M.Editing == EditMode.None));

            gorV.Add(new RowSection(h, "COLOR"));
            gorV.Add(new RowKV(h, "Code",
                () => M.Editing == EditMode.Color ? RgbText(M.RGB) : RgbText(G.ColorCode(G.LocalColor())),
                () => M.Editing == EditMode.Color ? new Color(M.RGB[0] / 9f, M.RGB[1] / 9f, M.RGB[2] / 9f, 1f) : G.LocalColor(),
                null, 22f));
            gorV.Add(new RowButton(h, () => M.Editing == EditMode.Color ? "Editing... (Y = done)" : "Edit Color",
                () => M.BeginEdit(EditMode.Color), null, null, 28f, () => M.Editing == EditMode.None));

            gorV.Add(new RowText(h, () => "One editor at a time.\nY retracts it and the menu\nreturns to your palm.", 9, () => Skin.Sub, 40f, TextAnchor.UpperLeft));
        }

        // ---------------------------------------------------------------- SERVER: hop + lobby

        private static void CycleQueue()
        {
            int i = Array.IndexOf(G.Queues, G.Queue());
            G.SetQueue(G.Queues[(i + 1) % G.Queues.Length]);
        }

        private static void CycleMode()
        {
            int i = Array.IndexOf(G.Modes, G.GameMode());
            G.SetGameMode(G.Modes[(i + 1) % G.Modes.Length]);
        }

        private void BuildHop()
        {
            var h = hopV.Hst;
            hopV.Add(new RowButton(h, () => "Queue: " + G.Queue(), CycleQueue, null, null, 26f, null));
            hopV.Add(new RowButton(h, () => "Gamemode: " + G.GameMode(), CycleMode, null, null, 26f, null));
            hopV.Add(new RowButton(h, () => "Server Hop", M.ServerHop, () => Color.Lerp(Skin.Button, Skin.Accent, 0.55f), null, 28f, null));
            hopV.Add(new RowButton(h, () => "Disconnect", M.DoDisconnect, () => new Color(0.5f, 0.14f, 0.14f), null, 28f, null));
            hopV.Add(new RowText(h, () => M.HopStatus, 10, () => Skin.Sub, 20f, TextAnchor.MiddleCenter));
        }

        private void BuildLobby()
        {
            var h = lobbyV.Hst;
            lobbyV.Add(new RowKV(h, "Code", () => G.InRoom ? G.RoomCode : "-", null, null, 22f));
            lobbyV.Add(new RowKV(h, "Players", () => G.InRoom ? G.RoomPlayers + "/" + G.RoomMax : "-", null, null, 22f));
            lobbyV.Add(new RowKV(h, "Gamemode", () => G.GameMode(), null, null, 22f));
            lobbyV.Add(new RowKV(h, "Queue", () => G.Queue(), null, null, 22f));
        }

        // ---------------------------------------------------------------- ALERTS: notifications

        private void BuildAlerts()
        {
            var h = alertV.Hst;

            alertV.Add(new RowSection(h, "NOTIFICATIONS"));

            alertV.Add(new RowStepper(h, "Duration", () => Cfg.NotifSeconds.Value.ToString("0") + "s",
                () => Cfg.NotifSeconds.Value = Mathf.Max(1f, Cfg.NotifSeconds.Value - 1f),
                () => Cfg.NotifSeconds.Value = Mathf.Min(15f, Cfg.NotifSeconds.Value + 1f)));

            alertV.Add(new RowStepper(h, "Size", () => Cfg.NotifSize.Value.ToString("0") + "%",
                () => Cfg.NotifSize.Value = Mathf.Max(50f, Cfg.NotifSize.Value - 10f),
                () => Cfg.NotifSize.Value = Mathf.Min(200f, Cfg.NotifSize.Value + 10f)));

            alertV.Add(new RowButton(h, () => "Mod Alerts: " + Cfg.ModAlerts.Value, () =>
            {
                int i = Array.IndexOf(AlertModes, Cfg.ModAlerts.Value);
                Cfg.ModAlerts.Value = AlertModes[(i + 1) % AlertModes.Length];
            }, null, null, 26f, null));

            alertV.Add(new RowButton(h, () => "Friend Alert: " + (Cfg.FriendAlert.Value ? "On" : "Off"),
                () => Cfg.FriendAlert.Value = !Cfg.FriendAlert.Value, null, null, 26f, null));

            alertV.Add(new RowButtons(h, 24f,
                Spec("Test Mods", () => M.Notify("PLAYER [MODS]", Skin.Green)),
                Spec("Test Cheats", () => M.Notify("PLAYER [CHEATS]", Skin.Red))));
        }
    }
}
