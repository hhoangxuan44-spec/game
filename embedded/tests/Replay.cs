// usage: Tests <scenario.json> [savePath]   scenario = [{"path": "...", "body": {...}}, ...]
using System;
using System.Collections.Generic;
using System.IO;
using MonsuOffline;

static class Replay
{
    static int Main(string[] args)
    {
        List<object> steps = (List<object>)Json.Parse(File.ReadAllText(args[0]));
        Game g = new Game(args.Length > 1 ? args[1] : null);
        foreach (object o in steps)
        {
            Dictionary<string, object> step = (Dictionary<string, object>)o;
            string body = Json.Write(step.ContainsKey("body") ? step["body"] : new Dictionary<string, object>());
            string resp = g.Handle((string)step["path"], body);
            Console.WriteLine((g.LastUnhandled ? "UNHANDLED " : "") + resp);
        }
        return 0;
    }
}
