using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace ModChecker
{
    [BepInPlugin(".modchecker.gorillatag", "PMC", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            Cfg.Bind(Config);
            Sigs.Init(Paths.ConfigPath);
            Friends.Init(Paths.ConfigPath);

            Snd.Init(gameObject);

            var go = new GameObject("ModCheckerHost");
            DontDestroyOnLoad(go);
            go.AddComponent<MenuController>();

            Log.LogInfo("Mod Checker Menu loaded - press Y on the left controller in-game.");
        }
    }

    internal static class Cfg
    {
        public static ConfigEntry<string> Theme, ModAlerts, SavedPoseLeft, SavedPoseRight, MenuHand;
        public static ConfigEntry<float> NotifSeconds, NotifSize, FingerX, FingerY, FingerZ, MenuScale;
        public static ConfigEntry<bool> FriendAlert, ShowTip, LivePreview, MusicTheme, MusicArtOnline, NameTags, JoinLeaveAlerts;

        public static void Bind(ConfigFile c)
        {
            Theme = c.Bind("Menu", "Theme", Skin.PlayerTheme, "Player Color, Galaxy, Ocean, Navy, Sunset, Forest, Rose, Lava, Mint, Midnight, Candy, Gold, Arctic, Cyber");
            MenuScale = c.Bind("Menu", "TabletScale", 1.3f, "Size multiplier of the whole tablet (1 = original size).");
            LivePreview = c.Bind("Menu", "LivePlayerPreview", true, "Show the selected player as a live moving gorilla in the Information page.");
            NameTags = c.Bind("Gorilla", "NameTags", false, "Show player name tags above other players.");
            JoinLeaveAlerts = c.Bind("Server", "JoinLeaveAlerts", false, "Show on-screen notifications when players join or leave the room.");
            MenuHand = c.Bind("Hand", "MenuHandSide", "Left", "Which hand carries the menu: Left (default) or Right. The other hand points, touches and pulls the trigger. Y always toggles the menu. Takes effect on the next game start.");
            SavedPoseLeft = c.Bind("Hand", "SavedMenuPoseLeft", "", "Set automatically the first time you open the menu on the LEFT hand: where the tablet sits. It never changes after that. Clear it, or use Configuration > Style > Reset Menu Position, to set it again.");
            SavedPoseRight = c.Bind("Hand", "SavedMenuPoseRight", "", "Same as SavedMenuPoseLeft, used when MenuHandSide is Right.");

            MusicTheme = c.Bind("Music", "MusicTheme", true, "While music plays, recolour the menu in the streaming service's colours (Spotify green + black, Apple Music red + white, ...) with shapes that move with the music.");
            MusicArtOnline = c.Bind("Music", "OnlineAlbumArt", true, "When the music app gives no album picture, look the playing song up on Apple's public iTunes search (this sends the song and artist name to Apple). Turn off to stay fully offline.");
            FingerX = c.Bind("Hand", "FingertipX", 0f, "Index fingertip offset in a hand's local space (metres), used for the pointing hand's fingertip and ray. It also sets the finger direction the menu rests along.");
            FingerY = c.Bind("Hand", "FingertipY", -0.1f, "Fingertip offset Y (the game's hand points along -Y).");
            FingerZ = c.Bind("Hand", "FingertipZ", 0f, "Fingertip offset Z.");
            ShowTip = c.Bind("Hand", "ShowTriggerSphere", false, "Show the small trigger sphere on your index fingertip (the touch trigger works either way).");
            NotifSeconds = c.Bind("Notifications", "Duration", 4f, "Seconds a notification stays on screen.");
            NotifSize = c.Bind("Notifications", "SizePercent", 100f, "Notification text size in percent.");
            ModAlerts = c.Bind("Notifications", "ModAlerts", "All", "Mods, Cheats or All.");
            FriendAlert = c.Bind("Notifications", "FriendAlert", true, "Alert when a friend comes online / joins your room.");
        }
    }

    internal class Notifier
    {
        private class E
        {
            public Text T;
            public float Born, Life;
        }

        private readonly List<E> list = new List<E>();
        private GameObject go;
        private RectTransform rt;
        private Canvas cv;
        private Transform attached;

        private void Ensure()
        {
            if (go != null) return;

            go = new GameObject("ModCheckerNotes");
            cv = go.AddComponent<Canvas>();
            cv.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 16f;
            rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(900f, 360f);
            rt.localScale = Vector3.one * 0.0006f;
        }

        public void SetHidden(bool hide)
        {
            if (cv != null) cv.enabled = !hide;
        }

        public void Show(string msg, Color col)
        {
            Ensure();

            if (list.Count >= 5)
            {
                Object.Destroy(list[0].T.gameObject);
                list.RemoveAt(0);
            }

            int fs = Mathf.RoundToInt(34f * Cfg.NotifSize.Value / 100f);
            var t = UI.LabelC(rt, 0, 0, 900, 50, msg, fs, col, TextAnchor.MiddleCenter, true);
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.9f);
            o.effectDistance = new Vector2(2f, -2f);

            list.Add(new E
            {
                T = t,
                Born = Time.unscaledTime,
                Life = Mathf.Max(0.5f, Cfg.NotifSeconds.Value)
            });
        }

        public void Tick()
        {
            if (go == null) return;

            Transform head = G.HeadT;

            if (head != null && attached != head)
            {
                rt.SetParent(head, false);
                rt.localPosition = new Vector3(0f, 0.16f, 0.75f);
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one * 0.0006f;
                attached = head;
            }

            int fs = Mathf.RoundToInt(34f * Cfg.NotifSize.Value / 100f);
            float y = 0f;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                float age = Time.unscaledTime - e.Born;

                if (age > e.Life)
                {
                    Object.Destroy(e.T.gameObject);
                    list.RemoveAt(i);
                    continue;
                }

                e.T.fontSize = fs;
                e.T.rectTransform.anchoredPosition = new Vector2(0f, -y);

                float a = Mathf.Clamp01(Mathf.Min(age / 0.2f, (e.Life - age) / 0.5f));
                Color color = e.T.color;
                color.a = a;
                e.T.color = color;

                y += fs * 1.35f;
            }
        }
    }
}