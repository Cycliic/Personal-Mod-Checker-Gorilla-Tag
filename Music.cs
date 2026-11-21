using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ModChecker
{
    // =====================================================================================================
    // MusicPlayer: works with ANY audio streaming service.
    //
    // Windows has one shared "media session" system that Spotify, Apple Music, TIDAL, Amazon Music, Deezer,
    // YouTube Music / SoundCloud / etc. in a browser, VLC... all report to (it is what the volume flyout and the
    // keyboard media keys use). A small hidden PowerShell helper reads it (title, artist, album, album picture,
    // playing / paused, position, length) and this class turns that into the player. The buttons send the normal
    // media keys, so they control whichever app is the current media session.
    // If the helper cannot start, Spotify is still read from its window title as a fallback.
    // =====================================================================================================

    internal static class MusicPlayer
    {
        public static string Title = "", Artist = "", Album = "", AlbumArtist = "", Service = "", Status = "Looking for music...";
        public static bool Running, Playing, HasTrack;
        public static float Elapsed, Duration;           // seconds (Duration 0 = unknown)
        public static Texture2D Art;
        public static int TrackVersion, ArtVersion;      // change counters the views watch

        private sealed class Snap
        {
            public bool None;
            public string App = "", Title = "", Artist = "", Album = "", AlbumArtist = "", Thumb = "", Diag = "";
            public bool Playing;
            public float Pos, Dur;
        }

        private static volatile Snap latest;
        private static Snap applied;
        private static volatile bool bridgeSeen, bridgeDead;
        private static System.Diagnostics.Process proc;
        private static bool started, bridgeFailed;
        private static float startedAt, appliedAt, basePos, stateLockUntil, nextTitlePoll, nextBrowserScan, trackChangedAt;
        private static string lastKey = "", lastThumb = "", browserService = "", lastTitleText = "", lastDiag = "";
        private static MonoBehaviour host;
        private static bool artFromApp, onlineTried;
        private static int artId;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        public static void Init(MonoBehaviour h)
        {
            host = h;
            Application.quitting += Shutdown;
            if (!started && !bridgeFailed) StartBridge();
        }

        public static void Shutdown()
        {
            try { if (proc != null && !proc.HasExited) proc.Kill(); } catch { }
            proc = null;
        }

        // ------------------------------------------------------------ the PowerShell helper

        // Prints one line per half second:  M|app|title|artist|album|albumArtist|playing|position|length|thumbnailFile
        // (text fields are base64 so nothing can break the line), or N when no app has a media session.
        // It exits by itself when the game closes. Plain Windows PowerShell 5.1 + the built-in WinRT media API.
        private const string BridgeScript = @"
$ErrorActionPreference = 'SilentlyContinue'
$parentPid = __PARENT__
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
function Await($op, $type) { $t = $asTask.MakeGenericMethod($type).Invoke($null, @($op)); [void]$t.Wait(-1); $t.Result }
$null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType = WindowsRuntime]
$null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties, Windows.Media.Control, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.IRandomAccessStreamWithContentType, Windows.Storage.Streams, ContentType = WindowsRuntime]
$mgr = Await ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
function B64($s) { if ($null -eq $s) { return '' }; [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$s)) }
$inv = [Globalization.CultureInfo]::InvariantCulture
$lastKey = ''
$thumb = ''
$n = 0
$tries = 0
$diag = ''
while ($true) {
  if (-not (Get-Process -Id $parentPid -ErrorAction SilentlyContinue)) { exit }
  try {
    $s = $null
    foreach ($x in $mgr.GetSessions()) { if ([int]$x.GetPlaybackInfo().PlaybackStatus -eq 4) { $s = $x; break } }
    if ($null -eq $s) { $s = $mgr.GetCurrentSession() }
    if ($null -eq $s) { [Console]::Out.WriteLine('N') }
    else {
      $pi = $s.GetPlaybackInfo()
      $tl = $s.GetTimelineProperties()
      $props = Await ($s.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
      $key = $s.SourceAppUserModelId + '|' + $props.Title + '|' + $props.Artist + '|' + $props.AlbumTitle
      if ($key -ne $lastKey) { $lastKey = $key; $thumb = ''; $tries = 0; $diag = '' }
      # apps usually publish the album picture a moment AFTER the song name, so keep trying for about 6 seconds
      if ($thumb -eq '' -and $tries -lt 12) {
        $tries++
        try {
          $ErrorActionPreference = 'Stop'
          $ref = $props.Thumbnail
          if ($null -ne $ref) {
            $st = Await ($ref.OpenReadAsync()) ([Windows.Storage.Streams.IRandomAccessStreamWithContentType])
            if ($st.Size -gt 0) {
              $n++
              $path = Join-Path $env:TEMP ('modchecker_art_' + $PID + '_' + $n + '.img')
              $in = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($st)
              $fs = [System.IO.File]::Create($path)
              $in.CopyTo($fs)
              $fs.Close()
              $in.Close()
              $thumb = $path
              $diag = ''
            }
          }
        } catch { $diag = [string]$_.Exception.Message }
        $ErrorActionPreference = 'SilentlyContinue'
        if ($thumb -eq '' -and $tries -ge 12 -and $diag -eq '') { $diag = 'this app provides no album picture' }
      }
      $playing = 0
      if ([int]$pi.PlaybackStatus -eq 4) { $playing = 1 }
      $pos = $tl.Position.TotalSeconds
      if ($playing -eq 1) { $pos += ([DateTimeOffset]::Now - $tl.LastUpdatedTime).TotalSeconds }
      $dur = $tl.EndTime.TotalSeconds - $tl.StartTime.TotalSeconds
      $line = 'M|' + (B64 $s.SourceAppUserModelId) + '|' + (B64 $props.Title) + '|' + (B64 $props.Artist) + '|' + (B64 $props.AlbumTitle) + '|' + (B64 $props.AlbumArtist) + '|' + $playing + '|' + $pos.ToString('0.0', $inv) + '|' + $dur.ToString('0.0', $inv) + '|' + (B64 $thumb) + '|' + (B64 $diag)
      [Console]::Out.WriteLine($line)
    }
    [Console]::Out.Flush()
  } catch { }
  Start-Sleep -Milliseconds 100
}
";

        private static void StartBridge()
        {
            started = true;
            startedAt = Time.unscaledTime;
            try
            {
                string script = BridgeScript.Replace("__PARENT__", System.Diagnostics.Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
                string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand " + enc)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.ASCII
                };
                proc = System.Diagnostics.Process.Start(psi);
                var t = new Thread(ReadLoop) { IsBackground = true, Name = "ModCheckerMusic" };
                t.Start();
            }
            catch (Exception e)
            {
                bridgeFailed = true;
                if (Plugin.Log != null) Plugin.Log.LogWarning("Music helper could not start (" + e.Message + "); falling back to reading Spotify's window title.");
            }
        }

        private static void ReadLoop()
        {
            try
            {
                string line;
                while ((line = proc.StandardOutput.ReadLine()) != null)
                {
                    Snap s = ParseLine(line);
                    if (s == null) continue;
                    latest = s;
                    bridgeSeen = true;
                }
            }
            catch { }
            bridgeDead = true;
        }

        private static string D(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return "";
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); } catch { return ""; }
        }

        private static Snap ParseLine(string line)
        {
            if (line == "N") return new Snap { None = true };
            if (!line.StartsWith("M|", StringComparison.Ordinal)) return null;
            string[] p = line.Split('|');
            if (p.Length < 10) return null;
            var s = new Snap();
            s.App = D(p[1]); s.Title = D(p[2]); s.Artist = D(p[3]); s.Album = D(p[4]); s.AlbumArtist = D(p[5]);
            s.Playing = p[6] == "1";
            float v;
            if (float.TryParse(p[7], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) s.Pos = v;
            if (float.TryParse(p[8], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) s.Dur = v;
            s.Thumb = D(p[9]);
            if (p.Length > 10) s.Diag = D(p[10]);
            return s;
        }

        // ------------------------------------------------------------ per frame

        /// <param name="active">the menu is on screen: start the helper and keep the state fresh</param>
        public static void Tick(float dt, bool active)
        {
            if (!started && !bridgeFailed) StartBridge();

            var s = latest;
            if (s != null && !ReferenceEquals(s, applied))
            {
                applied = s;
                appliedAt = Time.unscaledTime;
                try { Apply(s); } catch { }
            }
            else if (active && (bridgeFailed || bridgeDead || (!bridgeSeen && started && Time.unscaledTime - startedAt > 8f)))
            {
                if (Time.unscaledTime >= nextTitlePoll) { nextTitlePoll = Time.unscaledTime + 0.8f; PollSpotifyTitle(); }
                appliedAt = Time.unscaledTime;
            }

            if (Running)
            {
                Elapsed = Playing ? basePos + (Time.unscaledTime - appliedAt) : basePos;
                if (Duration > 0f && Elapsed > Duration) Elapsed = Duration;
            }

            // no picture from the app after a couple of seconds: look the song up online (Apple's public iTunes search)
            if (Running && HasTrack && Art == null && !onlineTried && host != null && Cfg.MusicArtOnline.Value
                && Time.unscaledTime - trackChangedAt > 0.1f)
            {
                onlineTried = true;
                host.StartCoroutine(OnlineArt(Artist, Title, artId));
            }
        }

        private static void Apply(Snap s)
        {
            if (s.None)
            {
                if (Running) { Running = false; Playing = false; TrackVersion++; }
                Status = "Nothing is playing in any app";
                return;
            }

            bool wasRunning = Running;
            Running = true;
            Status = "";

            string key = s.App + "|" + s.Title + "|" + s.Artist + "|" + s.Album;
            if (key != lastKey)
            {
                lastKey = key;
                // a new song: forget the old picture and look again
                if (Art != null) { UnityEngine.Object.Destroy(Art); Art = null; ArtVersion++; }
                artFromApp = false; onlineTried = false; lastThumb = ""; lastDiag = "";
                trackChangedAt = Time.unscaledTime;
                artId++;
                Title = s.Title; Artist = s.Artist; Album = s.Album; AlbumArtist = s.AlbumArtist;
                HasTrack = s.Title.Length > 0;
                Service = DetectService(s.App);
                TrackVersion++;
            }
            else if (!wasRunning) TrackVersion++;

            // right after our own Play/Pause press keep what we showed for a moment, so the button doesn't flicker
            if (Time.unscaledTime >= stateLockUntil) Playing = s.Playing;
            Duration = s.Dur > 1f ? s.Dur : 0f;
            basePos = Mathf.Max(0f, s.Pos);

            if (s.Thumb != lastThumb) { lastThumb = s.Thumb; LoadArt(s.Thumb); }
            if (s.Diag.Length > 0 && s.Diag != lastDiag)
            {
                lastDiag = s.Diag;
                if (Plugin.Log != null) Plugin.Log.LogInfo("Music helper: album picture - " + s.Diag);
            }
        }

        private static void LoadArt(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    if (Art != null) { UnityEngine.Object.Destroy(Art); Art = null; ArtVersion++; }
                    return;
                }
                if (!System.IO.File.Exists(path)) return;
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(bytes))
                {
                    if (Art != null) UnityEngine.Object.Destroy(Art);
                    Art = tex;
                    artFromApp = true;
                    ArtVersion++;
                }
                else
                {
                    UnityEngine.Object.Destroy(tex);
                    if (Plugin.Log != null) Plugin.Log.LogInfo("Music: the album picture from the app could not be decoded (" + bytes.Length + " bytes).");
                }
                try { System.IO.File.Delete(path); } catch { }
            }
            catch { }
        }

        // ------------------------------------------------------------ album picture, online fallback

        private static string Clean(string s)
        {
            s = Regex.Replace(s ?? "", @"\s*[\(\[].*?[\)\]]", "");
            int d = s.IndexOf(" - ", StringComparison.Ordinal);
            if (d > 0) s = s.Substring(0, d);
            return s.Trim();
        }

        /// <summary>Reads one value (string or number) out of a JSON text without a JSON library.</summary>
        private static string Json(string json, string key)
        {
            string k = "\"" + key + "\"";
            int i = json.IndexOf(k, StringComparison.Ordinal);
            if (i < 0) return "";
            i = json.IndexOf(':', i + k.Length);
            if (i < 0) return "";
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '"') return "";
            var sb = new StringBuilder();
            for (i++; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); continue; }
                if (c == '"') break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static IEnumerator OnlineArt(string artist, string song, int id)
        {
            string term = Clean(artist) + " " + Clean(song);
            string url = "https://itunes.apple.com/search?media=music&entity=song&limit=1&term=" + UnityWebRequest.EscapeURL(term);
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 8;
                yield return req.SendWebRequest();
                if (id != artId || req.result != UnityWebRequest.Result.Success) yield break;
                string art = Json(req.downloadHandler.text, "artworkUrl100");
                if (string.IsNullOrEmpty(art)) yield break;
                art = art.Replace("100x100", "600x600");
                using (var treq = UnityWebRequestTexture.GetTexture(art))
                {
                    treq.timeout = 10;
                    yield return treq.SendWebRequest();
                    if (id != artId || artFromApp || treq.result != UnityWebRequest.Result.Success) yield break;
                    var tex = DownloadHandlerTexture.GetContent(treq);
                    if (Art != null) UnityEngine.Object.Destroy(Art);
                    Art = tex;
                    ArtVersion++;
                }
            }
        }

        // ------------------------------------------------------------ which service is it?

        private static readonly string[] BrowserNames = { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "chromium" };

        private static bool IsBrowserId(string a)
        {
            foreach (var b in BrowserNames) if (a.Contains(b)) return true;
            return a.Contains("edge");
        }

        /// <summary>Browser players: the service is read from the browser's window title (the tab you have open).</summary>
        private static string BrowserService()
        {
            if (Time.unscaledTime < nextBrowserScan) return browserService;
            nextBrowserScan = Time.unscaledTime + 3f;
            string found = "";
            try
            {
                foreach (var name in BrowserNames)
                {
                    var procs = System.Diagnostics.Process.GetProcessesByName(name);
                    foreach (var p in procs)
                    {
                        try
                        {
                            string t = p.MainWindowTitle;
                            if (!string.IsNullOrEmpty(t) && found.Length == 0) found = ServiceFromText(t);
                        }
                        catch { }
                        finally { try { p.Dispose(); } catch { } }
                    }
                    if (found.Length > 0) break;
                }
            }
            catch { }
            browserService = found;
            return browserService;
        }

        private static string ServiceFromText(string t)
        {
            string l = t.ToLowerInvariant();
            if (l.Contains("youtube music")) return "YouTube Music";
            if (l.Contains("spotify")) return "Spotify";
            if (l.Contains("apple music")) return "Apple Music";
            if (l.Contains("soundcloud")) return "SoundCloud";
            if (l.Contains("deezer")) return "Deezer";
            if (l.Contains("tidal")) return "TIDAL";
            if (l.Contains("amazon music")) return "Amazon Music";
            if (l.Contains("pandora")) return "Pandora";
            if (l.Contains("qobuz")) return "Qobuz";
            if (l.Contains("napster")) return "Napster";
            if (l.Contains("youtube")) return "YouTube";
            return "";
        }

        private static string DetectService(string appId)
        {
            string a = (appId ?? "").ToLowerInvariant();
            if (a.Contains("spotify")) return "Spotify";
            if (a.Contains("applemusic") || a.Contains("itunes")) return "Apple Music";
            if (a.Contains("tidal")) return "TIDAL";
            if (a.Contains("amazon")) return "Amazon Music";
            if (a.Contains("deezer")) return "Deezer";
            if (a.Contains("soundcloud")) return "SoundCloud";
            if (a.Contains("pandora")) return "Pandora";
            if (a.Contains("youtubemusic") || a.Contains("ytmusic") || a.Contains("youtube music")) return "YouTube Music";
            if (a.Contains("qobuz")) return "Qobuz";
            if (a.Contains("napster")) return "Napster";

            // a browser (or an unknown app id such as Firefox's): the open tab's title tells which site
            string web = BrowserService();
            if (web.Length > 0 && (IsBrowserId(a) || a.Length == 0 || IsHexId(a))) return web;
            if (IsBrowserId(a)) return "Web player";

            // some other player (VLC, foobar...): name it
            string name = (appId ?? "").Split('!')[0];
            name = name.Replace(".exe", "").Replace(".EXE", "");
            int us = name.IndexOf('_');
            if (us > 0) name = name.Substring(0, us);
            return name.Length > 0 ? (name.Length > 18 ? name.Substring(0, 18) : name) : "Music";
        }

        private static bool IsHexId(string a)
        {
            if (a.Length < 12) return false;
            foreach (char c in a) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        // ------------------------------------------------------------ fallback: Spotify's window title

        private static void PollSpotifyTitle()
        {
            System.Diagnostics.Process[] procs = null;
            try
            {
                procs = System.Diagnostics.Process.GetProcessesByName("Spotify");
                string title = null;
                foreach (var p in procs)
                {
                    string t = null;
                    try { t = p.MainWindowTitle; } catch { }
                    if (string.IsNullOrEmpty(t)) continue;
                    title = t;
                    break;
                }

                if (title == null)
                {
                    if (Running) { Running = false; Playing = false; TrackVersion++; }
                    Status = "Open a music app (Spotify, Apple Music, ...)";
                    return;
                }
                Running = true;
                Status = "";
                title = title.Trim();
                int dash = title.IndexOf(" - ", StringComparison.Ordinal);
                bool idle = title.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase) || dash <= 0;
                if (Time.unscaledTime >= stateLockUntil) Playing = !idle;
                if (idle) return;
                if (title != lastTitleText)
                {
                    lastTitleText = title;
                    Artist = title.Substring(0, dash).Trim();
                    Title = title.Substring(dash + 3).Trim();
                    Album = ""; AlbumArtist = ""; Service = "Spotify";
                    HasTrack = true;
                    Duration = 0f; basePos = 0f;
                    TrackVersion++;
                }
            }
            catch { }
            finally
            {
                if (procs != null) foreach (var p in procs) { try { p.Dispose(); } catch { } }
            }
        }

        // ------------------------------------------------------------ controls (the normal media keys)

        public static void PlayPause()
        {
            Key(0xB3);
            Playing = !Playing;                                  // show the change straight away...
            stateLockUntil = Time.unscaledTime + 1.2f;           // ...and don't let an old reading flip it back
        }

        public static void Next() { Key(0xB0); }
        public static void Previous() { Key(0xB1); }

        private static void Key(byte vk)
        {
            try
            {
                keybd_event(vk, 0, 1, UIntPtr.Zero);             // key down (extended key)
                keybd_event(vk, 0, 3, UIntPtr.Zero);             // key up
            }
            catch { }
        }
    }

    // =====================================================================================================
    // AudioLevel: how loud the music is right now (Windows' own audio meter for the default output device),
    // shaped so that beats and bass hits stand out. Drives the moving shapes of the music theme.
    // =====================================================================================================

    internal static class AudioLevel
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioMeterInformation
        {
            [PreserveSig] int GetPeakValue(out float peak);
        }

        public static float Value;           // 0..1, what the visuals use
        public static bool Available;

        private static IAudioMeterInformation meter;
        private static float nextInit, env, slow, smooth;
        private static bool warned;

        private static bool Init()
        {
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return false;   // render, multimedia
                Guid iid = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
                object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0 || o == null) return false;
                meter = (IAudioMeterInformation)o;
                Available = true;
                return true;
            }
            catch (Exception e)
            {
                if (!warned && Plugin.Log != null) { warned = true; Plugin.Log.LogWarning("Audio level unavailable (" + e.Message + "); the music theme moves gently instead of following the beat."); }
                Available = false;
                meter = null;
                return false;
            }
        }

        private static float ReadPeak()
        {
            if (meter == null)
            {
                if (Time.unscaledTime < nextInit) return 0f;
                nextInit = Time.unscaledTime + 10f;
                if (!Init()) return 0f;
            }
            try
            {
                float p;
                if (meter.GetPeakValue(out p) == 0) return Mathf.Clamp01(p);
            }
            catch { meter = null; Available = false; }
            return 0f;
        }

        public static void Tick(float dt, bool playing)
        {
            float peak = playing ? ReadPeak() : 0f;

            // fast envelope (attack 20 ms, release 220 ms) and a slow average (~1.5 s): a beat is the envelope above the average
            float a = peak > env ? 1f - Mathf.Exp(-dt / 0.02f) : 1f - Mathf.Exp(-dt / 0.22f);
            env += (peak - env) * a;
            slow += (env - slow) * (1f - Mathf.Exp(-dt / 1.5f));
            float beat = Mathf.Max(0f, env - slow);

            float target = playing ? Mathf.Clamp01(env * 0.9f + beat * 2.4f) : 0f;
            smooth += (target - smooth) * (1f - Mathf.Exp(-dt / 0.04f));
            Value = smooth;
        }
    }

    // =====================================================================================================
    // Palettes: every service gets its own colours. Playing music recolours the whole menu in them; the
    // "second" colour (black, or white for Apple Music / Pandora) moves around and pulses with the music.
    // =====================================================================================================

    internal static class MusicPalette
    {
        private static readonly Dictionary<string, Skin.Theme> cache = new Dictionary<string, Skin.Theme>();

        private static Color H(string h)
        {
            Color c;
            if (!ColorUtility.TryParseHtmlString("#" + h, out c)) c = Color.gray;
            return c;
        }

        private static Skin.Theme Make(string name, string body, string top, string bottom, string accent, string moving, float alpha)
        {
            return new Skin.Theme
            {
                Name = name,
                Body = H(body),
                Top = H(top),
                Bottom = H(bottom),
                Accent = H(accent),
                Moving = H(moving),
                MovingAlpha = alpha,
                Reactive = true
            };
        }

        public static Skin.Theme For(string service)
        {
            Skin.Theme t;
            if (cache.TryGetValue(service, out t)) return t;
            switch (service)
            {
                case "Spotify": t = Make(service, "0c4a22", "0f6a30", "08381a", "1ed760", "000000", 0.75f); break;   // green + black
                case "Apple Music": t = Make(service, "9e1424", "c4182e", "8a1020", "ff7a8a", "ffffff", 0.30f); break;   // red + white
                case "YouTube Music": t = Make(service, "6e0a0a", "b00d0d", "5a0707", "ff4545", "000000", 0.75f); break;   // red + black
                case "YouTube": t = Make(service, "6e0a0a", "b00d0d", "5a0707", "ff4545", "000000", 0.75f); break;
                case "Amazon Music": t = Make(service, "0b4f57", "10808c", "0a3d44", "25d1da", "000000", 0.75f); break;   // teal + black
                case "TIDAL": t = Make(service, "0a4a52", "0e6b78", "08383f", "00ffff", "000000", 0.75f); break;   // cyan + black
                case "Deezer": t = Make(service, "3b1070", "5a1fa8", "2a0a50", "c58bff", "000000", 0.75f); break;   // purple + black
                case "SoundCloud": t = Make(service, "7a2a00", "c24100", "5c1f00", "ff7733", "000000", 0.75f); break;   // orange + black
                case "Pandora": t = Make(service, "1a3070", "2a4cb0", "122350", "7da2ff", "ffffff", 0.30f); break;   // blue + white
                case "Qobuz": t = Make(service, "10305a", "184a8c", "0c2444", "5aa8ff", "000000", 0.75f); break;
                case "Napster": t = Make(service, "10305a", "184a8c", "0c2444", "5aa8ff", "000000", 0.75f); break;
                default: t = Make(service, "3a2a6e", "5a3fa8", "2a1d52", "b08cff", "000000", 0.75f); break;   // any other player
            }
            cache[service] = t;
            return t;
        }
    }

    // =====================================================================================================
    // BassFx: soft moving shapes layered on every screen while a music theme is active. They drift around and
    // swell with the music level, so the "second colour" moves like the bass.
    // =====================================================================================================

    internal static class BassFx
    {
        private class Layer
        {
            public RectTransform Box;
            public float W, H;
            public RectTransform[] Rt = new RectTransform[3];
            public Image[] Img = new Image[3];
        }

        private static readonly List<Layer> layers = new List<Layer>();
        private static float clock;

        /// <summary>Adds the moving layer on top of a screen image (same rectangle, clipped to it).</summary>
        public static void Attach(Transform parent, float x, float y, float w, float h)
        {
            var l = new Layer { W = w, H = h };
            l.Box = UI.Rect("bass", parent, x, y, w, h);
            l.Box.gameObject.AddComponent<RectMask2D>();
            for (int i = 0; i < 3; i++)
            {
                var rt = UI.Rect("blob", l.Box, 0, 0, 10, 10);
                rt.pivot = new Vector2(0.5f, 0.5f);
                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = Gfx.Soft;
                img.raycastTarget = false;
                l.Rt[i] = rt;
                l.Img[i] = img;
            }
            l.Box.gameObject.SetActive(false);
            layers.Add(l);
        }

        public static void Tick(float dt, float level, bool active)
        {
            clock += dt;
            for (int k = layers.Count - 1; k >= 0; k--)
            {
                var l = layers[k];
                if (l.Box == null) { layers.RemoveAt(k); continue; }
                if (l.Box.gameObject.activeSelf != active) l.Box.gameObject.SetActive(active);
                if (!active || !l.Box.gameObject.activeInHierarchy) continue;

                float push = 1f + level * 0.9f;
                Color c = Skin.Moving;
                for (int i = 0; i < 3; i++)
                {
                    float ox = Mathf.Cos(clock * (0.35f + 0.17f * i) + i * 2.1f) * l.W * 0.34f * push;
                    float oy = Mathf.Sin(clock * (0.29f + 0.13f * i) + i * 3.7f) * l.H * 0.34f * push;
                    float size = Mathf.Max(l.W, l.H) * (0.55f + 0.2f * i) * (1f + level * 1.1f);
                    l.Rt[i].sizeDelta = new Vector2(size, size);
                    l.Rt[i].anchoredPosition = new Vector2(l.W / 2f + ox, -(l.H / 2f + oy));
                    c.a = Skin.MovingAlpha * (0.5f + 0.5f * level);
                    l.Img[i].color = c;
                }
            }
        }
    }

    // =====================================================================================================
    // The music screen. The same class builds both layouts:
    //   hand mode (portrait page, 176 x 208): album picture on top, details, progress, buttons
    //   tablet mode (landscape, 244 x 176): details and buttons on the left, a BIGGER album picture on the right
    // =====================================================================================================

    internal class MusicView
    {
        private readonly bool land;
        private readonly RawImage art;
        private readonly Image artIcon, playIcon, fill;
        private readonly RectTransform fillRt;
        private readonly Text title, artist, album, meta, timeL, timeR, extra;
        private readonly float barW;
        private int tv = -1, av = -1;
        private float timer;

        private static string Fmt(float s)
        {
            int t = Mathf.Max(0, Mathf.FloorToInt(s));
            return (t / 60) + ":" + (t % 60).ToString("00");
        }

        private Btn IconButton(Space sp, Transform parent, float x, float y, float w, float h, string icon, Action tap, out Image iconImg)
        {
            var b = UI.Button(sp, parent, x, y, w, h, null, 0, tap, 7f, null, null);
            float s = Mathf.Min(h - 8f, 22f);
            var img = UI.Icon(b.Rt, icon, (w - s) / 2f, (h - s) / 2f, s);
            Skin.Bind(() => { if (img == null) return false; img.color = Skin.Text; return true; });
            iconImg = img;
            return b;
        }

        private static void Wrap(Text t, TextAnchor a)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.alignment = a;
        }

        public MusicView(MenuController m, Transform parent, bool landscape)
        {
            land = landscape;
            var sp = m.Main.Sp;
            Image dummy;

            // ---- album picture (placeholder + picture)
            float ax = land ? 116f : 40f, ay = land ? 4f : 0f, asz = land ? 124f : 96f;
            var panel = UI.Rect("art_panel", parent, ax, ay, asz, asz);
            UI.AddImg(panel, () => Skin.Panel, 8f);
            artIcon = UI.Icon(panel, "music", asz * 0.28f, asz * 0.28f, asz * 0.44f);
            Skin.Bind(() => { if (artIcon == null) return false; artIcon.color = Skin.Sub; return true; });
            var artRt = UI.Rect("art", panel, 3, 3, asz - 6f, asz - 6f);
            art = artRt.gameObject.AddComponent<RawImage>();
            art.raycastTarget = false;
            art.gameObject.SetActive(false);

            // ---- texts + bar + buttons
            if (!land)
            {
                title = UI.Label(parent, 4, 100, 168, 28, "", 12, null, TextAnchor.UpperCenter, true);
                Wrap(title, TextAnchor.UpperCenter);
                artist = UI.Label(parent, 4, 128, 168, 14, "", 10, () => Skin.Sub, TextAnchor.MiddleCenter, false);
                album = UI.Label(parent, 4, 142, 168, 12, "", 9, () => Skin.Sub, TextAnchor.MiddleCenter, false);
                meta = UI.Label(parent, 4, 154, 168, 12, "", 8, () => Skin.Sub, TextAnchor.MiddleCenter, false);
                barW = 160f;
                BuildBar(parent, 8, 168, barW, out fillRt, out fill);
                timeL = UI.Label(parent, 8, 173, 80, 10, "", 8, () => Skin.Sub, TextAnchor.MiddleLeft, false);
                timeR = UI.Label(parent, 88, 173, 80, 10, "", 8, () => Skin.Sub, TextAnchor.MiddleRight, false);

                IconButton(sp, parent, 2, 184, 38, 24, "prev", MusicPlayer.Previous, out dummy);
                IconButton(sp, parent, 42, 184, 52, 24, "pause", MusicPlayer.PlayPause, out playIcon);
                IconButton(sp, parent, 96, 184, 38, 24, "next", MusicPlayer.Next, out dummy);
                IconButton(sp, parent, 138, 184, 38, 24, "tablet", m.EnterTablet, out dummy);   // tablet mode
            }
            else
            {
                title = UI.Label(parent, 6, 6, 106, 44, "", 12, null, TextAnchor.UpperLeft, true);
                Wrap(title, TextAnchor.UpperLeft);
                artist = UI.Label(parent, 6, 52, 106, 14, "", 10, () => Skin.Sub, TextAnchor.MiddleLeft, false);
                album = UI.Label(parent, 6, 66, 106, 24, "", 9, () => Skin.Sub, TextAnchor.UpperLeft, false);
                Wrap(album, TextAnchor.UpperLeft);
                meta = UI.Label(parent, 6, 92, 106, 12, "", 8, () => Skin.Sub, TextAnchor.MiddleLeft, false);
                barW = 106f;
                BuildBar(parent, 6, 110, barW, out fillRt, out fill);
                timeL = UI.Label(parent, 6, 116, 52, 10, "", 8, () => Skin.Sub, TextAnchor.MiddleLeft, false);
                timeR = UI.Label(parent, 60, 116, 52, 10, "", 8, () => Skin.Sub, TextAnchor.MiddleRight, false);
                extra = UI.Label(parent, 116, 132, 124, 40, "", 9, () => Skin.Sub, TextAnchor.UpperLeft, false);
                Wrap(extra, TextAnchor.UpperLeft);

                IconButton(sp, parent, 6, 136, 32, 34, "prev", MusicPlayer.Previous, out dummy);
                IconButton(sp, parent, 42, 136, 44, 34, "pause", MusicPlayer.PlayPause, out playIcon);
                IconButton(sp, parent, 90, 136, 32, 34, "next", MusicPlayer.Next, out dummy);
            }
        }

        private static void BuildBar(Transform parent, float x, float y, float w, out RectTransform fillRt, out Image fill)
        {
            UI.AddImg(UI.Rect("bar", parent, x, y, w, 5), () => Skin.Panel, 2.5f);
            fillRt = UI.Rect("bar_fill", parent, x, y, 0.5f, 5);
            fill = UI.AddImg(fillRt, () => Skin.Accent, 2.5f);
        }

        private void RefreshTexts()
        {
            bool has = MusicPlayer.HasTrack;
            title.text = has ? MusicPlayer.Title : (MusicPlayer.Running ? "Nothing playing" : "No music found");
            artist.text = has ? MusicPlayer.Artist : (MusicPlayer.Running ? "Press play in your music app" : MusicPlayer.Status);
            album.text = has ? MusicPlayer.Album : "";
            if (land)
            {
                string e = "";
                if (has && MusicPlayer.AlbumArtist.Length > 0 && MusicPlayer.AlbumArtist != MusicPlayer.Artist) e += MusicPlayer.AlbumArtist + "\n";
                if (MusicPlayer.Service.Length > 0) e += MusicPlayer.Service;
                extra.text = e;
            }
        }

        public void Tick(float dt)
        {
            if (tv != MusicPlayer.TrackVersion) { tv = MusicPlayer.TrackVersion; RefreshTexts(); }
            if (av != MusicPlayer.ArtVersion)
            {
                av = MusicPlayer.ArtVersion;
                art.texture = MusicPlayer.Art;
            }
            bool hasArt = MusicPlayer.Art != null && MusicPlayer.HasTrack;
            if (art.gameObject.activeSelf != hasArt) art.gameObject.SetActive(hasArt);
            if (artIcon.gameObject.activeSelf == hasArt) artIcon.gameObject.SetActive(!hasArt);

            timer -= dt;
            if (timer > 0f) return;
            timer = 0.2f;

            // the button shows what pressing it will do: pause while playing, play while paused
            Sprite want = Gfx.Icon(MusicPlayer.Playing ? "pause" : "play");
            if (playIcon.sprite != want) playIcon.sprite = want;

            float frac = MusicPlayer.Duration > 0f ? Mathf.Clamp01(MusicPlayer.Elapsed / MusicPlayer.Duration) : 0f;
            if (fillRt != null) fillRt.sizeDelta = new Vector2(Mathf.Max(0.5f, barW * frac), 5f);
            timeL.text = MusicPlayer.HasTrack ? Fmt(MusicPlayer.Elapsed) : "";
            timeR.text = MusicPlayer.HasTrack ? (MusicPlayer.Duration > 0f ? Fmt(MusicPlayer.Duration) : "--:--") : "";
            meta.text = MusicPlayer.Running
                ? (MusicPlayer.Service.Length > 0 ? MusicPlayer.Service + "  |  " : "") + (MusicPlayer.Playing ? "Playing" : "Paused")
                : "";
        }
    }
}