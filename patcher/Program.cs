// Monsu offline patcher.
//
// Edits Assembly-CSharp.dll (Unity 4.6 / Mono) so the game
//   1. talks to a local server instead of the (dead) cloud servers, and
//   2. never calls into the Java UC/Fangcun SDK (login / pay / init / submit / quit).
//
// Every change is listed below and mirrored in README.zh.md.
//
//   NetworkingProperties.get_SERVER_URL   -> returns the URL given on the command line
//   NetworkingProperties.SetServerUrlBy*  -> no-op (they used to overwrite the URL)
//   VkSdk.Initialize                      -> no Java calls; Channel = 0, ForumUrl = ""
//   VkSdk.InitSDK                         -> stores callbacks, then feeds onFinish an "init ok" message
//   VkSdk.Login                           -> feeds onFinish a "login ok" message with a fixed fake user
//   VkSdk.Pay                             -> stores callback, feeds onFinish a "pay ok" message
//   VkSdk.Submit / VkSdk.TryQuit          -> no-op
//
// With --embed <MonsuOffline.dll> (see Embed.cs) the offline server is compiled into the game as well:
//   HttpRequestDispatcher.DispatchRequest -> answered in-process instead of sending a WWW request
//   NetworkHealthSensor.DoHealthCheck     -> reports "connected" without a request
//
// Running the tool on an already patched DLL is detected and refused to modify it again.

using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class Program
{
    const string InitOk = "{\"errCode\":100,\"originLogin\":false}";
    const string LoginOk = "{\"errCode\":200,\"channelTag\":0,\"userName\":\"Monsu\",\"userId\":\"local-player\",\"token\":\"offline\"}";
    const string PayOk = "{\"errCode\":500,\"orderNO\":\"offline-order\",\"sdkOrderNO\":\"offline-sdk-order\"}";

    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: patcher <in.dll> <out.dll> [server_url]");
            return 2;
        }
        string url = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : "http://127.0.0.1:8083/api/";
        if (!url.EndsWith("/")) url += "/";
        // --embed <MonsuOffline.dll>: also embed the offline server into the game (no external server needed)
        string embed = null;
        for (int a = 0; a < args.Length - 1; a++) if (args[a] == "--embed") embed = args[a + 1];

        // Cecil needs UnityEngine.dll / mscorlib.dll etc. next to the input to resolve constants while writing.
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[0])));
        var asm = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { AssemblyResolver = resolver });
        var mod = asm.MainModule;
        var net = mod.GetType("NetworkingProperties");
        var vk = mod.GetType("VkSdk");

        var getUrl = Method(net, "get_SERVER_URL");
        if (getUrl.Body.Instructions[0].OpCode == OpCodes.Ldstr)
        {
            Console.WriteLine("already patched: nothing to do");
            if (args[0] != args[1]) System.IO.File.Copy(args[0], args[1], true);
            return 0;
        }

        // 1. server address
        Replace(getUrl, il => { il.Emit(OpCodes.Ldstr, url); il.Emit(OpCodes.Ret); });
        Replace(Method(net, "SetServerUrlByChannel"), il => il.Emit(OpCodes.Ret));
        Replace(Method(net, "SetServerUrlByRegion"), il => il.Emit(OpCodes.Ret));

        // 2. SDK
        var onFinish = Method(vk, "onFinish");
        var setChannel = Method(vk, "set_Channel");
        var setForum = Method(vk, "set_ForumUrl");

        // Initialize: drop everything up to and including the set_ForumUrl call, then set the values directly.
        var init = Method(vk, "Initialize");
        var body = init.Body.Instructions;
        int cut = body.ToList().FindIndex(i => i.OpCode == OpCodes.Call && ((MethodReference)i.Operand).Name == "set_ForumUrl");
        if (cut < 0) throw new Exception("Initialize layout changed");
        for (int i = 0; i <= cut; i++) body.RemoveAt(0);
        var ilp = init.Body.GetILProcessor();
        var first = body[0];
        ilp.InsertBefore(first, ilp.Create(OpCodes.Ldc_I4_0));
        ilp.InsertBefore(first, ilp.Create(OpCodes.Call, setChannel));
        ilp.InsertBefore(first, ilp.Create(OpCodes.Ldstr, ""));
        ilp.InsertBefore(first, ilp.Create(OpCodes.Call, setForum));

        var msInit = vk.Fields.First(f => f.Name == "msCurrentInitEvent");
        var msLogout = vk.Fields.First(f => f.Name == "msCurrentLogoutEvent");
        var msPay = vk.Fields.First(f => f.Name == "msCurrentPayEvent");

        Replace(Method(vk, "InitSDK"), il =>
        {
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stsfld, msInit);
            il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stsfld, msLogout);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, InitOk); il.Emit(OpCodes.Call, onFinish);
            il.Emit(OpCodes.Ret);
        });
        Replace(Method(vk, "Login"), il =>
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, LoginOk); il.Emit(OpCodes.Call, onFinish);
            il.Emit(OpCodes.Ret);
        });
        Replace(Method(vk, "Pay"), il =>
        {
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stsfld, msPay);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, PayOk); il.Emit(OpCodes.Call, onFinish);
            il.Emit(OpCodes.Ret);
        });
        Replace(Method(vk, "Submit"), il => il.Emit(OpCodes.Ret));
        Replace(Method(vk, "TryQuit"), il => il.Emit(OpCodes.Ret));

        if (embed != null)
            Embed.Run(mod, embed, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[0])));

        asm.Write(args[1]);
        Console.WriteLine("patched -> " + args[1] + " (server " + url + ")");
        return 0;
    }

    static MethodDefinition Method(TypeDefinition t, string name)
    {
        var m = t.Methods.FirstOrDefault(x => x.Name == name);
        if (m == null) throw new Exception("method not found: " + t.Name + "::" + name);
        return m;
    }

    // Replace a method body wholesale. Parameters/return type stay untouched, locals are dropped.
    static void Replace(MethodDefinition m, Action<ILProcessor> emit)
    {
        m.Body.Instructions.Clear();
        m.Body.Variables.Clear();
        m.Body.ExceptionHandlers.Clear();
        m.Body.InitLocals = false;
        emit(m.Body.GetILProcessor());
    }
}
