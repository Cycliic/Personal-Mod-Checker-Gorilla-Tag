using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ModChecker
{
    // ===================================================================== data

    internal class Sig
    {
        public string Name;
        public bool Cheat;
        public List<string> Keys = new List<string>();
    }

    internal class PInfo
    {
        public string Key;
        public Component Rig;
        public object Net;
        public Transform HeadT;
        public string Name = "?", UserId = "", Platform = "?";
        public int Actor;
        public bool IsLocal;
        public Color Color = Color.gray;
        public int Fps;
        public int Ping = -1;
        public DateTime Joined = DateTime.Now;
        public float HeadDps, Speed;
        public List<Sig> Mods = new List<Sig>();
        public List<Sig> Cheats = new List<Sig>();
        public int SigVersion;
        public bool Muted, AlertedCheat, AlertedMod;
        internal Vector3 lastPos;
        internal Quaternion lastRot;
        internal bool hasLast;

        /// <summary>Where the laser should aim: their head if we have it, otherwise roughly chest height on their rig.</summary>
        public bool TryAim(out Vector3 point)
        {
            if (!R.Dead(HeadT)) { point = HeadT.position; return true; }
            if (!R.Dead(Rig)) { point = Rig.transform.position + Vector3.up * 0.8f; return true; }
            point = Vector3.zero;
            return false;
        }

        public int[] Code { get { return G.ColorCode(Color); } }
        public string CodeText { get { var c = Code; return c[0] + " " + c[1] + " " + c[2]; } }
    }

    // ===================================================================== signatures

    /// <summary>
    /// Mod detection works on Photon custom player properties: most menus publish a property whose name
    /// identifies them. A key matches when it equals the entry (case-insensitive) or, for entries of 5+
    /// characters, contains it. These keys are best-effort - menus rename them. Add your own lines to
    /// BepInEx/config/ModChecker.signatures.txt (format: Name|Cheat or Mod|key1,key2).
    /// </summary>
    internal static class Sigs
    {
        public static readonly List<Sig> All = new List<Sig>();

        private static void Add(string name, bool cheat, params string[] keys)
        {
            var s = new Sig { Name = name, Cheat = cheat };
            foreach (var k in keys) s.Keys.Add(k.ToLowerInvariant());
            All.Add(s);
        }

        public static void Init(string configDir)
        {
            All.Clear();

            // ---- cheats / illegal menus
            Add("ii's Stupid Menu", true, "ii's <3", "ii's stupid", "iis stupid", "iistupid", "iimenu");
            Add("Solar Menu", true, "solar");
            Add("Orbit Menu", true, "orbit");
            Add("Shiba Gold", true, "shibagold", "shiba");
            Add("VXV Menu", true, "vxv");
            Add("MonkePrime", true, "monkeprime");
            Add("Void Menu", true, "void", "voidmenu");
            Add("Hudxin Mod Menu", true, "hudxin");
            Add("Zolo's Troll Menu", true, "zolo");
            Add("NXO Pad", true, "nxo");
            Add("Rexon Menu", true, "rexon");
            Add("Tundra Client", true, "tundra");
            Add("Fizz Menu", true, "fizz");
            Add("Eyerock Mod Menu", true, "eyerock");
            Add("Eyeclown Menu", true, "eyeclown");
            Add("ThirteenGT Menu", true, "thirteen");
            Add("NotJurlyClient", true, "notjurly", "jurly");
            Add("Frozone Mod", true, "frozone");
            Add("Serial Lift Menu", true, "seriallift");
            Add("Seralyth Menu", true, "seralyth");
            Add("CosmeticX", true, "cosmeticx");
            Add("Genesis", true, "genesis");
            Add("Elux", true, "elux");
            Add("Violet", true, "violetpaid", "violetfreeuser", "violet");
            Add("Mist", true, "mistuser", "mist");
            Add("Obsidian", true, "obsidianmc", "obsidian");
            Add("Resurgence", true, "hgrehngio889584739_hugb");
            Add("Grate", true, "grateversion", "grate");
            Add("BananaOS", true, "bananaos");
            Add("Euphoria", true, "euphoria");
            Add("Cheat Menu", true, "cheat menu", "cheatmenu");
            Add("Untitled Menu", true, "untitled");

            // ---- allowed / cosmetic / quality-of-life mods
            Add("Bark Mod Menu", false, "bark");
            Add("GorillaShirts", false, "gorillashirts", "gshirts");
            Add("Utilla", false, "utilla");
            Add("Comfort", false, "comfort");
            Add("Computer Interface", false, "computerinterface", "computer interface");
            Add("GorillaFriends", false, "gorillafriends");
            Add("Custom Cosmetics", false, "ccmv2", "customcosmetics");
            Add("GorillaInfoWatch", false, "infowatch", "gorillainfowatch");
            Add("Monke Map Loader", false, "monkemap", "maploader");
            Add("Emotes", false, "emote");
            Add("MonkeStatistics", false, "monkestat");

            // ---- user additions
            try
            {
                string path = Path.Combine(configDir, "ModChecker.signatures.txt");
                if (!File.Exists(path))
                {
                    File.WriteAllText(path,
                        "# Extra signatures. One per line:  Name|Cheat or Mod|key1,key2,key3\r\n" +
                        "# Keys are matched against Photon custom player property names (case-insensitive).\r\n" +
                        "# Example:  My Menu|Cheat|mymenu,my menu v2\r\n");
                    return;
                }
                foreach (var raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    var parts = line.Split('|');
                    if (parts.Length < 3) continue;
                    var s = new Sig { Name = parts[0].Trim(), Cheat = parts[1].Trim().StartsWith("c", StringComparison.OrdinalIgnoreCase) };
                    foreach (var k in parts[2].Split(',')) if (k.Trim().Length > 0) s.Keys.Add(k.Trim().ToLowerInvariant());
                    if (s.Keys.Count > 0) All.Add(s);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("signatures file: " + e.Message); }
        }

        public static void Detect(IDictionary props, List<Sig> mods, List<Sig> cheats)
        {
            var keys = new List<string>();
            var vals = new List<string>();
            foreach (DictionaryEntry de in props)
            {
                string k = de.Key as string;
                if (k != null) keys.Add(k.ToLowerInvariant());
                string v = de.Value as string;
                if (v != null && v.Length < 48) vals.Add(v.ToLowerInvariant());
            }

            foreach (var s in All)
            {
                bool hit = false;
                foreach (var key in s.Keys)
                {
                    for (int i = 0; i < keys.Count && !hit; i++)
                        if (keys[i] == key || (key.Length >= 5 && keys[i].Contains(key))) hit = true;
                    for (int i = 0; i < vals.Count && !hit; i++)
                        if (vals[i] == key) hit = true;
                    if (hit) break;
                }
                if (!hit) continue;
                if (s.Cheat) cheats.Add(s); else mods.Add(s);
            }
        }
    }

    // ===================================================================== game adapter

    /// <summary>Every call into Gorilla Tag. If a game update renames something, fix it here.</summary>
    internal static class G
    {
        public static readonly string[] Queues = { "DEFAULT", "MINIGAMES", "COMPETITIVE" };
        public static readonly string[] Modes = { "INFECTION", "CASUAL", "HUNT", "BATTLE", "AMBUSH" };   // match your game version

        // time-of-day slots used by the day/night manager (adjust if your build differs)
        public const int SlotMidnight = 0, SlotMorning = 1, SlotNoon = 3, SlotEvening = 6;

        private static object tagger;

        public static object Tagger
        {
            get
            {
                if (R.Dead(tagger)) tagger = R.Get(R.FindType("GorillaTagger"), "Instance", "_instance");
                return R.Dead(tagger) ? null : tagger;
            }
        }

        public static object Computer { get { return R.Get(R.FindType("GorillaComputer"), "instance", "Instance"); } }
        private static object NS { get { return R.Get(R.FindType("NetworkSystem"), "Instance"); } }
        private static Type PN { get { return R.FindType("PhotonNetwork"); } }
        private static object PNC { get { return R.Get(R.FindType("PhotonNetworkController"), "Instance"); } }
        private static object Day { get { return R.Get(R.FindType("BetterDayNightManager"), "instance", "Instance"); } }

        public static Transform LeftHand { get { return R.Get<Transform>(Tagger, "leftHandTransform"); } }
        public static Transform RightHand { get { return R.Get<Transform>(Tagger, "rightHandTransform"); } }

        public static Transform HeadT
        {
            get
            {
                var cam = Camera.main;
                if (cam != null) return cam.transform;
                var c = R.Get<Collider>(Tagger, "headCollider");
                return c != null ? c.transform : null;
            }
        }

        public static bool Ready
        {
            get
            {
                if (Tagger == null) return false;
                var l = LeftHand; var r = RightHand; var h = HeadT;
                return l != null && r != null && h != null;
            }
        }

        public static string WhyNotReady()
        {
            if (R.FindType("GorillaTagger") == null) return "type GorillaTagger not found in any game assembly";
            if (Tagger == null) return "GorillaTagger.Instance is null (not spawned yet?)";
            if (LeftHand == null || RightHand == null) return "leftHandTransform / rightHandTransform not found on GorillaTagger";
            if (HeadT == null) return "no Camera.main and no headCollider";
            return "ready";
        }

        public static Component LocalRig { get { return R.Get<Component>(Tagger, "offlineVRRig"); } }

        /// <summary>Every active VRRig in the scene (slower than Rigs(), so the roster calls it every couple of seconds).</summary>
        public static List<Component> ScanRigs()
        {
            var res = new List<Component>();
            var t = R.FindType("VRRig");
            if (t == null) return res;
            try
            {
                foreach (var o in UnityEngine.Object.FindObjectsOfType(t))
                {
                    var c = o as Component;
                    if (c != null && c.gameObject.activeInHierarchy) res.Add(c);
                }
            }
            catch { }
            return res;
        }

        /// <summary>Everyone the network layer says is in the room.</summary>
        public static List<object> NetPlayers()
        {
            var l = R.List(R.Get(NS, "AllNetPlayers"));
            if (l.Count == 0) l = R.List(R.Get(PN, "PlayerList"));
            return l;
        }

        public static List<object> Rigs()
        {
            var gp = R.Get(R.FindType("GorillaParent"), "instance", "Instance");
            return R.List(R.Get(gp, "vrrigs"));
        }

        // ------------------------------------------------------------ per-player data

        public static object NetOf(object rig) { return R.Get(rig, "OwningNetPlayer", "Creator", "creator"); }

        public static IDictionary Props(object net)
        {
            if (net == null) return null;
            var d = R.Get(net, "CustomProperties") as IDictionary;
            if (d != null) return d;
            var pr = R.Call(net, "GetPlayerRef");
            return R.Get(pr, "CustomProperties") as IDictionary;
        }

        public static Color ColorOf(Component rig)
        {
            var o = R.Get(rig, "playerColor");
            if (o is Color) { var c = (Color)o; if (c.a > 0.01f) return c; }
            var rend = R.Get<Renderer>(rig, "mainSkin");
            if (rend != null && rend.material != null) return rend.material.color;
            return Color.gray;
        }

        public static int[] ColorCode(Color c)
        {
            return new[]
            {
                Mathf.Clamp(Mathf.RoundToInt(c.r * 9f), 0, 9),
                Mathf.Clamp(Mathf.RoundToInt(c.g * 9f), 0, 9),
                Mathf.Clamp(Mathf.RoundToInt(c.b * 9f), 0, 9)
            };
        }

        private static bool platformHinted, pingHinted;

        /// <summary>Best effort: an explicit platform value if the game exposes one, otherwise the cosmetic-login heuristic.</summary>
        public static string PlatformOf(Component rig, object net)
        {
            // 1) an explicit platform member on the rig
            object o = R.Get(rig, "platform", "Platform", "platformType", "PlatformType");
            string ps = o != null ? NicePlatform(o.ToString()) : null;
            if (ps != null) return ps;

            // 2) a platform entry in the network player's custom properties
            var props = Props(net);
            if (props != null)
            {
                foreach (DictionaryEntry de in props)
                {
                    string k = de.Key as string;
                    if (k == null || de.Value == null || !k.ToLowerInvariant().Contains("platform")) continue;
                    ps = NicePlatform(de.Value.ToString());
                    if (ps != null) return ps;
                }
            }

            // 3) the cosmetic-login string
            string c = R.Str(R.Get(rig, "concatStringOfCosmeticsAllowed"));
            if (!string.IsNullOrEmpty(c))
            {
                if (c.Contains("S. FIRST LOGIN")) return "Steam";
                if (c.Contains("FIRST LOGIN")) return "Meta PC";
                return "Quest/Pico";
            }

            if (!platformHinted && !R.Dead(rig))
            {
                platformHinted = true;
                Hint(rig, "Platform not found on VRRig. Candidate members: ", "platform", "cosmetic", "device");
            }
            return "Unknown";
        }

        private static string NicePlatform(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string r = raw.ToLowerInvariant();
            if (r.Contains("steam")) return "Steam";
            if (r.Contains("pico")) return "Pico";
            if (r.Contains("quest") || r.Contains("android") || r.Contains("standalone")) return "Quest";
            if (r.Contains("oculus") || r.Contains("meta") || r.Contains("pc")) return "Meta PC";
            return raw.Length < 16 ? raw : null;
        }

        /// <summary>Logs the names of rig members that might hold a missing value, so it can be fixed for your game version.</summary>
        private static void Hint(Component rig, string prefix, params string[] words)
        {
            if (Plugin.Log == null) return;
            try
            {
                var sb = new System.Text.StringBuilder(prefix);
                for (Type t = rig.GetType(); t != null && t != typeof(MonoBehaviour) && t != typeof(object); t = t.BaseType)
                {
                    foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (m.MemberType != MemberTypes.Field && m.MemberType != MemberTypes.Property) continue;
                        string n = m.Name.ToLowerInvariant();
                        foreach (var w in words) if (n.Contains(w)) { sb.Append(m.Name).Append(", "); break; }
                    }
                }
                Plugin.Log.LogInfo(sb.ToString());
            }
            catch { }
        }

        public static int FpsOf(Component rig, bool local)
        {
            if (local) return Mathf.RoundToInt(1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f));
            return R.Int(R.Get(rig, "fps"), 0);
        }

        private static double ToD(object o)
        {
            if (o is double) return (double)o;
            if (o is float) return (float)o;
            if (o is int) return (int)o;
            if (o is long) return (long)o;
            return double.NaN;
        }

        /// <summary>
        /// Local player: the network round-trip time. Other players: the age of their newest network snapshot
        /// compared with the network clock (the usual way mods estimate someone else's ping). -1 = not available.
        /// </summary>
        public static int PingOf(Component rig, bool local)
        {
            try
            {
                if (local)
                {
                    object p = R.Call(PN, "GetPing");                          // Photon PUN
                    if (p is int) return (int)p;
                    var runner = R.Get(NS, "runner", "Runner");                // Photon Fusion
                    double rtt = ToD(R.Call(runner, "GetPlayerRtt", R.Get(runner, "LocalPlayer")));
                    if (!double.IsNaN(rtt)) return Mathf.Clamp((int)(rtt * 1000.0), 0, 2000);
                    return -1;
                }

                var hist = R.List(R.Get(rig, "velocityHistoryList", "VelocityHistoryList"));
                if (hist.Count > 0)
                {
                    double newest = double.NaN;
                    foreach (var e in hist)
                    {
                        double t = ToD(R.Get(e, "time", "Time", "timestamp"));
                        if (!double.IsNaN(t) && (double.IsNaN(newest) || t > newest)) newest = t;
                    }
                    double now = ToD(R.Get(PN, "Time"));
                    if (double.IsNaN(now)) now = ToD(R.Get(NS, "SimTime", "simTime", "ServerTime"));
                    if (!double.IsNaN(newest) && !double.IsNaN(now))
                        return Mathf.Clamp((int)(Math.Abs(now - newest) * 1000.0), 0, 2000);
                }

                if (!pingHinted && !R.Dead(rig))
                {
                    pingHinted = true;
                    Hint(rig, "Ping source not found on VRRig. Candidate members: ", "ping", "latency", "rtt", "velocity", "time");
                }
            }
            catch { }
            return -1;
        }

        public static Transform HeadTransformOf(Component rig)
        {
            var head = R.Get(rig, "head");
            var t = R.Get<Transform>(head, "rigTarget");
            if (t != null) return t;
            return rig != null ? rig.transform : null;
        }

        // ------------------------------------------------------------ local player

        public static Color LocalColor()
        {
            var rig = LocalRig;
            if (!R.Dead(rig)) return ColorOf(rig);
            return new Color(PlayerPrefs.GetFloat("redValue", .5f), PlayerPrefs.GetFloat("greenValue", .5f), PlayerPrefs.GetFloat("blueValue", .5f), 1f);
        }

        public static string LocalName()
        {
            var comp = Computer;
            string s = R.Str(R.Get(comp, "savedName", "currentName"));
            if (!string.IsNullOrEmpty(s)) return s;
            s = R.Str(R.Path(NS, "LocalPlayer.NickName"));
            if (!string.IsNullOrEmpty(s)) return s;
            return PlayerPrefs.GetString("playerName", "GORILLA");
        }

        public static string Sanitize(string n)
        {
            if (n == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char ch in n.ToUpperInvariant())
                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')) { sb.Append(ch); if (sb.Length >= 12) break; }
            return sb.ToString();
        }

        /// <summary>Applies a name using the game's own rules (A-Z/0-9, 12 chars, banned-word check).</summary>
        public static bool SetName(string raw)
        {
            string n = Sanitize(raw);
            if (n.Length < 1) return false;
            var comp = Computer;
            object r;
            if (R.TryCall(comp, "CheckAutoBanListForName", out r, n) && r is bool && !(bool)r) return false;

            R.Set(comp, "currentName", n);
            R.Set(comp, "savedName", n);
            PlayerPrefs.SetString("playerName", n);
            PlayerPrefs.Save();
            if (!R.TryCall(NS, "SetMyNickName", out r, n)) R.Set(PN, "NickName", n);
            var tag = R.Get(comp, "offlineVRRigNametagText");
            if (tag != null) R.Set(tag, "text", n);
            R.TryCall(LocalRig, "UpdateName", out r);
            return true;
        }

        public static void SetColor(int r, int g, int b)
        {
            float fr = r / 9f, fg = g / 9f, fb = b / 9f;
            PlayerPrefs.SetFloat("redValue", fr);
            PlayerPrefs.SetFloat("greenValue", fg);
            PlayerPrefs.SetFloat("blueValue", fb);
            PlayerPrefs.Save();
            object o;
            if (!R.TryCall(Tagger, "UpdateColor", out o, fr, fg, fb))
                R.TryCall(LocalRig, "InitializeNoobMaterialLocal", out o, fr, fg, fb);
        }

        // ------------------------------------------------------------ room / network

        public static string RoomCode
        {
            get
            {
                string s = R.Str(R.Get(NS, "RoomName"));
                if (!string.IsNullOrEmpty(s)) return s;
                return R.Str(R.Path(PN, "CurrentRoom.Name"));
            }
        }

        public static bool InRoom { get { return !string.IsNullOrEmpty(RoomCode); } }

        public static int RoomPlayers
        {
            get
            {
                int n = R.Int(R.Get(NS, "RoomPlayerCount"), -1);
                if (n >= 0) return n;
                return R.Int(R.Path(PN, "CurrentRoom.PlayerCount"), 0);
            }
        }

        public static int RoomMax
        {
            get
            {
                int n = R.Int(R.Path(PN, "CurrentRoom.MaxPlayers"), 0);
                return n > 0 ? n : 10;
            }
        }

        public static string GameMode()
        {
            string s = R.Str(R.Unwrap(R.Get(Computer, "currentGameMode")));
            if (!string.IsNullOrEmpty(s) && s.Length < 24) return s.ToUpperInvariant();
            var props = R.Path(PN, "CurrentRoom.CustomProperties") as IDictionary;
            if (props != null && props.Contains("gameMode"))
            {
                string gm = (R.Str(props["gameMode"]) ?? "").ToUpperInvariant();
                foreach (var m in Modes) if (gm.Contains(m)) return m;
                return gm;
            }
            return "-";
        }

        public static string Queue()
        {
            string s = R.Str(R.Unwrap(R.Get(Computer, "currentQueue")));
            return string.IsNullOrEmpty(s) ? "DEFAULT" : s.ToUpperInvariant();
        }

        public static void SetQueue(string q)
        {
            R.Set(Computer, "currentQueue", q);
            PlayerPrefs.SetString("currentQueue", q);
        }

        public static void SetGameMode(string m)
        {
            object o;
            if (R.TryCall(Computer, "OnModeSelectButtonPress", out o, m, false)) return;
            var w = R.Get(Computer, "currentGameMode");
            if (w != null && !(w is string)) R.Set(w, "Value", m); else R.Set(Computer, "currentGameMode", m);
            PlayerPrefs.SetString("currentGameMode", m);
        }

        public static bool Disconnect()
        {
            object o;
            var ns = NS;
            if (ns != null && R.TryCall(ns, "ReturnToSinglePlayer", out o)) return true;
            var pnc = PNC;
            if (pnc != null && R.TryCall(pnc, "AttemptDisconnect", out o)) return true;
            return R.TryCall(PN, "Disconnect", out o);
        }

        private static object EnumArg(Type t, string preferred)
        {
            try { return Enum.Parse(t, preferred, true); }
            catch { var vals = Enum.GetValues(t); return vals.Length > 0 ? vals.GetValue(0) : null; }
        }

        public static bool JoinRoom(string code)
        {
            var pnc = PNC;
            if (pnc == null) return false;
            return R.CallLoose(pnc, "AttemptToJoinSpecificRoom", p =>
            {
                if (p.ParameterType == typeof(string)) return code;
                if (p.ParameterType.IsEnum) return EnumArg(p.ParameterType, "Solo");
                return R.DefaultFor(p.ParameterType);
            });
        }

        public static object LastJoinTrigger { get { return R.Get(PNC, "currentJoinTrigger"); } }

        public static bool JoinPublic(object trigger)
        {
            var pnc = PNC;
            if (pnc == null || trigger == null) return false;
            return R.CallLoose(pnc, "AttemptToJoinPublicRoom", p =>
            {
                if (p.ParameterType.IsInstanceOfType(trigger)) return trigger;
                if (p.ParameterType.IsEnum) return EnumArg(p.ParameterType, "Solo");
                return R.DefaultFor(p.ParameterType);
            });
        }

        // ------------------------------------------------------------ weather / time (client-side visuals)

        public static void SetRain(bool on)
        {
            var arr = R.Get(Day, "weatherCycle") as Array;
            if (arr == null || arr.Length == 0) return;
            try
            {
                Type et = arr.GetType().GetElementType();
                object v = Enum.Parse(et, on ? "Raining" : "None", true);
                for (int i = 0; i < arr.Length; i++) arr.SetValue(v, i);
            }
            catch (Exception e) { Plugin.Log.LogDebug("rain: " + e.Message); }
        }

        public static int TimeSlots
        {
            get { var a = R.Get(Day, "timeOfDayRange") as Array; return a != null && a.Length > 0 ? a.Length : 10; }
        }

        public static int TimeIndex { get { return R.Int(R.Get(Day, "currentTimeIndex"), 0); } }

        public static void SetTime(int idx)
        {
            int n = TimeSlots;
            idx = ((idx % n) + n) % n;
            object o;
            R.TryCall(Day, "SetTimeOfDay", out o, idx);
        }

        // ------------------------------------------------------------ report / mute

        private static object ScoreboardLine(PInfo p)
        {
            var upd = R.FindType("GorillaScoreboardTotalUpdater");
            foreach (var line in R.List(R.Get(upd, "allScoreboardLines")))
            {
                var lp = R.Get(line, "linePlayer");
                if (lp == null) continue;
                string uid = R.Str(R.Get(lp, "UserId"));
                if (!string.IsNullOrEmpty(uid) && uid == p.UserId) return line;
                if (R.Int(R.Get(lp, "ActorNumber"), -1) == p.Actor && p.Actor > 0) return line;
            }
            return null;
        }

        /// <summary>type = Toxicity, HateSpeech or Cheating. Returns false if the game call could not be made.</summary>
        public static bool Report(PInfo p, string type)
        {
            try
            {
                var line = ScoreboardLine(p);
                var bt = R.FindType("GorillaPlayerLineButton+ButtonType");
                if (line == null || bt == null) return false;
                object o;
                return R.TryCall(line, "PressButton", out o, true, Enum.Parse(bt, type, true));
            }
            catch { return false; }
        }

        public static void SetMute(PInfo p, bool on)
        {
            try
            {
                var line = ScoreboardLine(p);
                var bt = R.FindType("GorillaPlayerLineButton+ButtonType");
                object o;
                if (line != null && bt != null) R.TryCall(line, "PressButton", out o, on, Enum.Parse(bt, "Mute", true));
            }
            catch { }
            if (R.Dead(p.Rig)) return;
            R.Set(p.Rig, "muted", on);
            foreach (var a in p.Rig.GetComponentsInChildren<AudioSource>(true)) a.mute = on;
        }
    }

    // ===================================================================== roster

    internal class Roster
    {
        public readonly List<PInfo> Players = new List<PInfo>();
        public int Version;
        public event Action<PInfo, bool> Flagged;   // (player, isCheat)

        private readonly Dictionary<string, PInfo> map = new Dictionary<string, PInfo>();
        public readonly HashSet<string> MutedIds = new HashSet<string>();
        private float next, scanNext;
        private string lastRoom = "", lastStat = "";
        private List<Component> scanned = new List<Component>();

        public void Tick(float dt)
        {
            for (int i = 0; i < Players.Count; i++) Track(Players[i], dt);
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            Refresh();
        }

        private static void Track(PInfo p, float dt)
        {
            if (dt <= 0f || R.Dead(p.HeadT)) return;
            Vector3 pos = p.HeadT.position;
            Quaternion rot = p.HeadT.rotation;
            if (p.hasLast)
            {
                float dps = Quaternion.Angle(p.lastRot, rot) / dt;
                float sp = (pos - p.lastPos).magnitude / dt;
                p.HeadDps = Mathf.Lerp(p.HeadDps, Mathf.Min(dps, 2000f), 0.12f);
                p.Speed = Mathf.Lerp(p.Speed, Mathf.Min(sp, 60f), 0.12f);
            }
            p.lastPos = pos; p.lastRot = rot; p.hasLast = true;
        }

        private void Refresh()
        {
            string room = G.RoomCode ?? "";
            if (room != lastRoom) { lastRoom = room; map.Clear(); Players.Clear(); Version++; }

            var seen = new HashSet<string>();
            bool changed = false;
            var local = G.LocalRig;

            // Rigs come from the game's own list AND (every 2 s) a full scene scan, so nobody is missed.
            var rigs = new List<Component>();
            int fromList = 0, fromScan = 0;
            foreach (var o in G.Rigs())
            {
                var rg = o as Component;
                if (R.Dead(rg) || rigs.Contains(rg)) continue;
                rigs.Add(rg); fromList++;
            }
            if (Time.unscaledTime >= scanNext) { scanNext = Time.unscaledTime + 2f; scanned = G.ScanRigs(); }
            foreach (var rg in scanned)
            {
                if (R.Dead(rg) || rigs.Contains(rg)) continue;
                rigs.Add(rg); fromScan++;
            }

            // If rigs expose their owner at all, ignore ownerless (pooled / unused) rigs.
            bool ownersWork = false;
            foreach (var rg in rigs) if (G.NetOf(rg) != null) { ownersWork = true; break; }

            foreach (var rig in rigs)
            {
                object owner = G.NetOf(rig);
                if (owner == null && ownersWork && rig != local) continue;
                if (Update(rig, owner, local, seen)) changed = true;
            }
            if (!R.Dead(local) && !seen.Contains("local"))
                if (Update(local, G.NetOf(local), local, seen)) changed = true;

            // Players the network layer knows about even when their rig wasn't found still appear in the list.
            var nets = G.NetPlayers();
            foreach (var np in nets)
                if (Update(null, np, local, seen)) changed = true;

            string stat = fromList + "/" + fromScan + "/" + nets.Count + "/" + seen.Count;
            if (stat != lastStat)
            {
                lastStat = stat;
                Plugin.Log.LogInfo("Roster: rigs list=" + fromList + " scan=" + fromScan + ", network players=" + nets.Count + ", shown=" + seen.Count);
            }

            for (int i = Players.Count - 1; i >= 0; i--)
            {
                if (seen.Contains(Players[i].Key)) continue;
                map.Remove(Players[i].Key);
                Players.RemoveAt(i);
                changed = true;
            }

            if (changed)
            {
                Players.Sort((a, b) =>
                {
                    if (a.IsLocal != b.IsLocal) return a.IsLocal ? -1 : 1;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                Version++;
            }
        }

        private bool Update(Component rig, object net, Component local, HashSet<string> seen)
        {
            bool hasRig = !R.Dead(rig);
            if (!hasRig && net == null) return false;
            bool isLocal = (hasRig && rig == local) || (net != null && R.Get(net, "IsLocal") is bool && (bool)R.Get(net, "IsLocal"));
            string uid = R.Str(R.Get(net, "UserId")) ?? "";
            int actor = R.Int(R.Get(net, "ActorNumber"), 0);

            string key;
            if (isLocal) key = "local";
            else if (uid.Length > 0) key = uid;
            else if (actor > 0) key = "a" + actor;
            else if (hasRig) key = "r" + rig.GetInstanceID();
            else return false;
            if (!seen.Add(key)) return false;

            bool isNew = false;
            PInfo p;
            if (!map.TryGetValue(key, out p))
            {
                p = new PInfo { Key = key };
                map[key] = p;
                Players.Add(p);
                isNew = true;
            }

            if (hasRig) p.Rig = rig;
            p.Net = net; p.IsLocal = isLocal;
            if (uid.Length > 0) p.UserId = uid;
            if (actor > 0) p.Actor = actor;
            if (hasRig) p.HeadT = isLocal ? (G.HeadT ?? G.HeadTransformOf(rig)) : G.HeadTransformOf(rig);
            else if (isLocal) p.HeadT = G.HeadT;

            string name = R.Str(R.Get(net, "NickName"));
            if (string.IsNullOrEmpty(name) && hasRig) name = isLocal ? G.LocalName() : R.Str(R.Get(rig, "playerNameVisible"));
            if (string.IsNullOrEmpty(name) && isLocal) name = G.LocalName();
            if (!string.IsNullOrEmpty(name)) p.Name = name;
            else if (isNew) p.Name = "UNKNOWN";

            if (hasRig)
            {
                p.Color = G.ColorOf(rig);
                string pl = G.PlatformOf(rig, net);
                if (pl != "Unknown" || p.Platform == "?") p.Platform = pl;
                p.Fps = G.FpsOf(rig, isLocal);
                int ping = G.PingOf(rig, isLocal);
                if (ping >= 0) p.Ping = p.Ping < 0 ? ping : Mathf.RoundToInt(Mathf.Lerp(p.Ping, ping, 0.35f));
            }

            if (isNew && uid.Length > 0 && MutedIds.Contains(uid)) { p.Muted = true; G.SetMute(p, true); }

            var props = G.Props(net);
            if (props != null && !isLocal)
            {
                var m = new List<Sig>();
                var c = new List<Sig>();
                Sigs.Detect(props, m, c);
                if (m.Count != p.Mods.Count || c.Count != p.Cheats.Count)
                {
                    p.Mods = m; p.Cheats = c; p.SigVersion++;
                    if (c.Count > 0 && !p.AlertedCheat)
                    {
                        p.AlertedCheat = true;
                        if (Flagged != null) Flagged(p, true);
                    }
                    else if (m.Count > 0 && c.Count == 0 && !p.AlertedMod && !p.AlertedCheat)
                    {
                        p.AlertedMod = true;
                        if (Flagged != null) Flagged(p, false);
                    }
                }
            }
            return isNew;
        }
    }

    // ===================================================================== friends

    internal class FriendEntry
    {
        public string Name = "", UserId = "", Room = "";
        public bool Online, FromGame;
        public Color Color = Color.white;
        public float FullUntil;
        public object Raw;   // the game's own friend object, when it came from the game's friend list
    }

    /// <summary>
    /// Local friend list (saved to BepInEx/config/ModChecker.friends.txt, add people from the Information page)
    /// merged with the game's own friend list when it can be read. Presence is best-effort.
    /// </summary>
    internal static class Friends
    {
        public static readonly List<FriendEntry> List = new List<FriendEntry>();
        public static int Version;
        public static event Action<FriendEntry> CameOnline;
        private static string path;
        private static float next;

        public static void Init(string dir)
        {
            path = Path.Combine(dir, "ModChecker.friends.txt");
            try
            {
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path))
                {
                    var p = line.Split('|');
                    if (p.Length < 2 || p[1].Trim().Length == 0) continue;
                    List.Add(new FriendEntry { Name = p[0].Trim(), UserId = p[1].Trim() });
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("friends file: " + e.Message); }
            Version++;
        }

        private static void Save()
        {
            try
            {
                var lines = new List<string>();
                foreach (var f in List) if (!f.FromGame) lines.Add(f.Name + "|" + f.UserId);
                File.WriteAllLines(path, lines.ToArray());
            }
            catch (Exception e) { Plugin.Log.LogWarning("friends save: " + e.Message); }
        }

        public static bool Has(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            foreach (var f in List) if (f.UserId == uid) return true;
            return false;
        }

        public static void Toggle(PInfo p)
        {
            if (string.IsNullOrEmpty(p.UserId)) return;
            for (int i = 0; i < List.Count; i++)
                if (List[i].UserId == p.UserId) { List.RemoveAt(i); Save(); Version++; return; }
            List.Add(new FriendEntry { Name = p.Name, UserId = p.UserId, Color = p.Color });
            Save();
            Version++;
        }

        private class GF { public string Name, Id, Room; public bool Online; public object Raw; }

        private static bool friendsHinted;
        private static float nextRefresh;

        private static object FriendSystemObj
        {
            get { return R.Get(R.FindType("FriendSystem"), "Instance", "instance", "_instance"); }
        }

        /// <summary>Last resort: any list/dictionary on the friend system whose items look like friends.</summary>
        private static List<object> FindFriendList(object fs)
        {
            foreach (var kv in R.Members(fs))
            {
                object v = kv.Value;
                if (v == null || v is string) continue;
                List<object> items = null;
                var dict = v as IDictionary;
                if (dict != null) items = R.List(dict.Values);
                else if (v is IEnumerable) items = R.List(v);
                if (items == null || items.Count == 0 || items[0] == null) continue;
                if (items[0].GetType().Name.ToLowerInvariant().Contains("friend")) return items;
            }
            return new List<object>();
        }

        /// <summary>Reads name / id / room / online out of whatever the game's friend object looks like.</summary>
        private static GF Describe(object item)
        {
            if (item == null) return null;
            var g = new GF { Raw = item };
            var flat = new List<KeyValuePair<string, object>>(R.Members(item));
            foreach (var kv in R.Members(item))   // one level of nesting (e.g. the Presence object)
            {
                object v = kv.Value;
                if (v == null || v is string || v is IEnumerable) continue;
                Type vt = v.GetType();
                if (vt.IsPrimitive || vt.IsEnum) continue;
                flat.AddRange(R.Members(v));
            }
            foreach (var kv in flat)
            {
                string k = kv.Key.ToLowerInvariant();
                string str = kv.Value as string;
                if (!string.IsNullOrEmpty(str))
                {
                    if (g.Room == null && k.Contains("room")) g.Room = str;
                    else if (g.Name == null && (k == "username" || k == "name" || k == "displayname" || k.EndsWith("username") || k.EndsWith("playername"))) g.Name = str;
                    else if (g.Id == null && (k == "id" || k.EndsWith("playfabid") || k.EndsWith("userid") || k.EndsWith("linkid"))) g.Id = str;
                }
                else if (kv.Value is bool && (bool)kv.Value && (k == "online" || k == "isonline")) g.Online = true;
            }
            if (g.Name == null && g.Id == null) return null;
            if (!string.IsNullOrEmpty(g.Room)) g.Online = true;
            return g;
        }

        private static List<GF> GameFriends()
        {
            var res = new List<GF>();
            try
            {
                object fs = FriendSystemObj;
                if (fs == null) return res;

                // ask the game to refresh its friend list every so often
                if (Time.unscaledTime >= nextRefresh)
                {
                    nextRefresh = Time.unscaledTime + 20f;
                    object o;
                    if (!R.TryCall(fs, "RefreshFriendsList", out o) && !R.TryCall(fs, "RefreshFriends", out o))
                        R.TryCall(fs, "UpdateFriendsList", out o);
                }

                var items = R.List(R.Get(fs, "FriendsList", "Friends", "friendsList", "friends"));
                if (items.Count == 0) items = FindFriendList(fs);
                foreach (var item in items)
                {
                    var g = Describe(item);
                    if (g != null) res.Add(g);
                }

                if (res.Count == 0 && !friendsHinted)
                {
                    friendsHinted = true;
                    var names = new List<string>();
                    foreach (var kv in R.Members(fs)) names.Add(kv.Key);
                    Plugin.Log.LogInfo("Friends: no game friend list found (you may simply have none yet). FriendSystem members: " + string.Join(", ", names.ToArray()));
                }
            }
            catch { }
            return res;
        }

        /// <summary>Joins a friend with the game's own join call when it has one.</summary>
        public static bool JoinViaGame(object raw)
        {
            if (raw == null) return false;
            object o;
            return R.TryCall(FriendSystemObj, "JoinFriend", out o, raw);
        }

        public static int GameFriendCount { get; private set; }

        public static void Tick(Roster roster)
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 2f;

            var game = GameFriends();
            GameFriendCount = game.Count;
            foreach (var g in game)
            {
                bool known = false;
                foreach (var f in List)
                    if ((!string.IsNullOrEmpty(g.Id) && f.UserId == g.Id) || (string.IsNullOrEmpty(g.Id) && f.Name == g.Name)) { known = true; break; }
                if (known) continue;
                List.Add(new FriendEntry { Name = g.Name ?? "?", UserId = g.Id ?? "", FromGame = true, Raw = g.Raw });
                Version++;
            }

            foreach (var f in List)
            {
                bool was = f.Online;
                PInfo here = null;
                foreach (var p in roster.Players)
                    if (!p.IsLocal && ((p.UserId.Length > 0 && p.UserId == f.UserId) || (f.UserId.Length == 0 && p.Name == f.Name))) { here = p; break; }

                if (here != null)
                {
                    f.Online = true; f.Room = G.RoomCode ?? ""; f.Color = here.Color; f.Name = here.Name;
                }
                else
                {
                    f.Online = false; f.Room = "";
                    foreach (var g in game)
                        if ((!string.IsNullOrEmpty(g.Id) && g.Id == f.UserId) || (string.IsNullOrEmpty(g.Id) && g.Name == f.Name))
                        {
                            f.Online = g.Online; f.Room = g.Room ?? ""; f.Raw = g.Raw; break;
                        }
                }
                if (f.Online && !was && CameOnline != null) CameOnline(f);
            }
        }
    }
}