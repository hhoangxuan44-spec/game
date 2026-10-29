#if UNITY
// Glue between the patched HttpRequestDispatcher and the embedded Game.  Only compiled for the in-game build.
using System;
using System.Collections;
using System.IO;
using System.Text;

namespace MonsuOffline
{
    public static class Embedded
    {
        static Game game;
        static readonly System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
        static string logPath;
        static bool inited;

        /// <summary>Called once at start-up (patched into VkSdk.Initialize): collects errors, writes monsu_log.txt and shows them on screen.</summary>
        public static void Init()
        {
            if (inited) return;
            inited = true;
            try
            {
                logPath = Path.Combine(UnityEngine.Application.persistentDataPath, "monsu_log.txt");
                UnityEngine.Application.RegisterLogCallback(OnLog);
                UnityEngine.GameObject go = new UnityEngine.GameObject("MonsuDebug");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<DebugOverlay>();
                Trace("init, save dir " + UnityEngine.Application.persistentDataPath);
            }
            catch (Exception) { }
        }

        static void OnLog(string condition, string stackTrace, UnityEngine.LogType type)
        {
            if (type == UnityEngine.LogType.Log || type == UnityEngine.LogType.Warning) return;
            string st = stackTrace == null ? "" : stackTrace.Split('\n')[0];
            Trace(type + ": " + condition + " | " + st);
        }

        public static void Trace(string msg)
        {
            try
            {
                string line = DateTime.Now.ToString("HH:mm:ss") + " " + msg;
                lock (lines)
                {
                    lines.Add(line);
                    if (lines.Count > 60) lines.RemoveAt(0);
                }
                StreamWriter w = new StreamWriter(logPath, true, System.Text.Encoding.UTF8);
                w.Write(line + "\n");
                w.Close();
            }
            catch (Exception) { }
        }

        public static string Recent()
        {
            lock (lines)
            {
                int from = lines.Count > 12 ? lines.Count - 12 : 0;
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = from; i < lines.Count; i++) sb.Append(lines[i]).Append('\n');
                return sb.ToString();
            }
        }

        static Game G()
        {
            if (game == null) game = new Game(Path.Combine(UnityEngine.Application.persistentDataPath, "monsu_save.json"));
            return game;
        }

        /// <summary>Called instead of sending a WWW request.  The answer is delivered one frame later, like a real request.</summary>
        public static void Dispatch(UnityEngine.MonoBehaviour host, Action<string> callback, string url, byte[] body)
        {
            host.StartCoroutine(Run(callback, url, body));
        }

        static IEnumerator Run(Action<string> callback, string url, byte[] body)
        {
            yield return null;
            string text;
            try
            {
                text = G().Handle(url, body == null ? "" : Encoding.UTF8.GetString(body));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("MonsuOffline: " + e);
                text = "{\"error\":{\"code\":\"internal\",\"message\":\"offline server error\"}}";
            }
            Trace("api " + url.Substring(url.IndexOf("/api/") + 5) + " -> " + text.Length + " bytes");
            callback(text);
        }
    }

    /// <summary>Shows the most recent log lines in the top-left corner (diagnostics for the first real-device runs).</summary>
    public class DebugOverlay : UnityEngine.MonoBehaviour
    {
        void OnGUI()
        {
            UnityEngine.GUI.color = UnityEngine.Color.yellow;
            UnityEngine.GUI.Label(new UnityEngine.Rect(8, 8, UnityEngine.Screen.width - 16, UnityEngine.Screen.height / 2), Embedded.Recent());
        }
    }
}
#endif
