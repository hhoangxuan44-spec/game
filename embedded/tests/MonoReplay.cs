// Same replay as Replay.cs but built with mcs against the *merged* Assembly-CSharp.dll and run on Mono,
// to prove the cloned/merged IL works on a Mono runtime.   usage: mono MonoReplay.exe <scenario.json>
using System;
using System.Collections.Generic;
using System.IO;
using MonsuOffline;

static class MonoReplay
{
    static int Main(string[] args)
    {
        // force-load every type of the merged server (this is what failed on the device: missing .override entries)
        int loaded = 0;
        foreach (Type t in typeof(Game).Assembly.GetTypes())
        {
            if (t.Namespace != "MonsuOffline" && !(t.DeclaringType != null && t.DeclaringType.Namespace == "MonsuOffline")) continue;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
            foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)) { }
            loaded++;
        }
        Console.Error.WriteLine("loaded " + loaded + " types");
        // JIT-compile every patched / added method: catches invalid IL and unresolvable references
        string[] jit = { "HttpRequestDispatcher.DispatchRequest", "MonsuOffline.Callback.Invoke", "MonsuOffline.Embedded.Dispatch",
                         "MonsuOffline.Embedded.Init", "MonsuOffline.Embedded.Trace", "MonsuOffline.DebugOverlay.OnGUI", "NetworkHealthSensor.DoHealthCheck",
                         "VkSdk.Initialize", "VkSdk.InitSDK", "VkSdk.Login", "VkSdk.Pay" };
        foreach (string name in jit)
        {
            int dot = name.LastIndexOf('.');
            Type t = typeof(Game).Assembly.GetType(name.Substring(0, dot));
            foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                if (m.Name == name.Substring(dot + 1)) { System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(m.MethodHandle); Console.Error.WriteLine("jit ok " + name); }
        }
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
