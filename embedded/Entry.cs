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
            callback(text);
        }
    }
}
#endif
