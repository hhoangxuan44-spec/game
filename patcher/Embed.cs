// Embeds the compiled offline server (MonsuOffline.dll) into Assembly-CSharp.dll and reroutes the game's
// network layer to it:
//
//   * every type of MonsuOffline.dll is cloned into Assembly-CSharp.dll (namespace MonsuOffline)
//   * HttpRequestDispatcher.DispatchRequest no longer creates a WWW; it calls MonsuOffline.Embedded.Dispatch,
//     which answers one frame later through a small callback class (MonsuOffline.Callback) that parses the
//     text with the game's own JSONObject.Parse and hands it to the original response handler
//   * NetworkHealthSensor.DoHealthCheck reports "connected" immediately instead of requesting "<server>/up"
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class Embed
{
    public static bool AlreadyEmbedded(ModuleDefinition target)
    {
        return target.GetType("MonsuOffline.Game") != null;
    }

    public static void Run(ModuleDefinition target, string libPath, string managedDir)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);
        var lib = AssemblyDefinition.ReadAssembly(libPath, new ReaderParameters { AssemblyResolver = resolver }).MainModule;
        var cloner = new Cloner(lib, target);
        cloner.CloneAll();

        var embedded = cloner.Map(lib.GetType("MonsuOffline.Embedded"));
        var dispatch = embedded.Methods.First(m => m.Name == "Dispatch");
        var actionString = dispatch.Parameters[1].ParameterType;              // Action<string> (target-imported)

        PatchDispatcher(target, dispatch, actionString);
        PatchHealthSensor(target);
    }

    static void PatchDispatcher(ModuleDefinition target, MethodDefinition dispatch, TypeReference actionString)
    {
        var disp = target.GetType("HttpRequestDispatcher");
        var method = disp.Methods.First(m => m.Name == "DispatchRequest");
        var jsonObject = target.GetType("Boomlagoon.JSON.JSONObject");
        var parse = jsonObject.Methods.First(m => m.Name == "Parse" && m.IsStatic && m.Parameters.Count == 1);
        var request = target.GetType("AbstractServiceRequest");
        var getUrl = request.Methods.First(m => m.Name == "get_Url");
        var getPost = request.Methods.First(m => m.Name == "get_PostData");

        // Action<JSONObject> is already the type of DispatchRequest's first parameter
        var actionJson = method.Parameters[0].ParameterType;

        // class MonsuOffline.Callback { Action<JSONObject> handler; void Invoke(string text) {...} }
        var cb = new TypeDefinition("MonsuOffline", "Callback",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit, target.TypeSystem.Object);
        target.Types.Add(cb);
        var handler = new FieldDefinition("handler", FieldAttributes.Public, actionJson);
        cb.Fields.Add(handler);
        var ctor = new MethodDefinition(".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            target.TypeSystem.Void);
        var objCtor = new MethodReference(".ctor", target.TypeSystem.Void, target.TypeSystem.Object) { HasThis = true };
        var il = ctor.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, objCtor); il.Emit(OpCodes.Ret);
        cb.Methods.Add(ctor);

        var invoke = new MethodDefinition("Invoke", MethodAttributes.Public | MethodAttributes.HideBySig, target.TypeSystem.Void);
        invoke.Parameters.Add(new ParameterDefinition("text", ParameterAttributes.None, target.TypeSystem.String));
        invoke.Body.Variables.Add(new VariableDefinition(jsonObject));
        // Action<JSONObject>::Invoke: reuse the reference the original code already contains
        var actionInvoke = target.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<MethodReference>()
            .First(r => r.Name == "Invoke" && r.DeclaringType is GenericInstanceType
                        && r.DeclaringType.FullName == actionJson.FullName);
        il = invoke.Body.GetILProcessor();
        var end = il.Create(OpCodes.Ret);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, parse); il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, handler); il.Emit(OpCodes.Brfalse, end);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, handler); il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Callvirt, actionInvoke);
        il.Append(end);
        cb.Methods.Add(invoke);

        var actionStringCtor = new MethodReference(".ctor", target.TypeSystem.Void, actionString) { HasThis = true };
        actionStringCtor.Parameters.Add(new ParameterDefinition(target.TypeSystem.Object));
        actionStringCtor.Parameters.Add(new ParameterDefinition(target.TypeSystem.IntPtr));

        method.Body.Instructions.Clear();
        method.Body.Variables.Clear();
        method.Body.ExceptionHandlers.Clear();
        il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);                         // host MonoBehaviour
        il.Emit(OpCodes.Newobj, ctor);                    // new Callback { handler = responseHandler }
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, handler);
        il.Emit(OpCodes.Ldftn, invoke);
        il.Emit(OpCodes.Newobj, actionStringCtor);        // new Action<string>(callback.Invoke)
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Callvirt, getUrl);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Callvirt, getPost);
        il.Emit(OpCodes.Call, dispatch);
        il.Emit(OpCodes.Ret);
    }

    static void PatchHealthSensor(ModuleDefinition target)
    {
        var t = target.GetType("NetworkHealthSensor");
        var check = t.Methods.First(m => m.Name == "DoHealthCheck");
        var received = t.Methods.First(m => m.Name == "ServerResponseReceived");
        check.Body.Instructions.Clear();
        check.Body.Variables.Clear();
        var il = check.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, received); il.Emit(OpCodes.Ret);
    }
}

/// <summary>Copies simple (non-generic) types from one module into another, remapping every reference.</summary>
class Cloner
{
    readonly ModuleDefinition src, dst;
    readonly Dictionary<TypeDefinition, TypeDefinition> types = new Dictionary<TypeDefinition, TypeDefinition>();
    readonly Dictionary<MethodDefinition, MethodDefinition> methods = new Dictionary<MethodDefinition, MethodDefinition>();
    readonly Dictionary<FieldDefinition, FieldDefinition> fields = new Dictionary<FieldDefinition, FieldDefinition>();

    public Cloner(ModuleDefinition src, ModuleDefinition dst) { this.src = src; this.dst = dst; }

    public TypeDefinition Map(TypeDefinition t) { return types[t]; }

    public void CloneAll()
    {
        foreach (var t in src.Types.Where(t => t.Name != "<Module>")) Declare(t, null);
        foreach (var kv in types.ToList()) Fill(kv.Key, kv.Value);
        foreach (var kv in methods.ToList()) Body(kv.Key, kv.Value);
    }

    void Declare(TypeDefinition s, TypeDefinition parent)
    {
        if (s.HasGenericParameters) throw new Exception("generic type not supported: " + s.FullName);
        var d = new TypeDefinition(parent == null ? s.Namespace : "", s.Name, s.Attributes);
        types[s] = d;
        if (parent == null) dst.Types.Add(d); else parent.NestedTypes.Add(d);
        foreach (var n in s.NestedTypes) Declare(n, d);
    }

    void Fill(TypeDefinition s, TypeDefinition d)
    {
        d.BaseType = s.BaseType == null ? null : Imp(s.BaseType);
        foreach (var i in s.Interfaces) d.Interfaces.Add(new InterfaceImplementation(Imp(i.InterfaceType)));
        d.ClassSize = s.ClassSize; d.PackingSize = s.PackingSize;
        foreach (var f in s.Fields)
        {
            if (f.InitialValue != null && f.InitialValue.Length > 0) throw new Exception("RVA field data not supported: " + f.FullName);
            var nf = new FieldDefinition(f.Name, f.Attributes, Imp(f.FieldType));
            if (f.HasConstant) nf.Constant = f.Constant;
            d.Fields.Add(nf);
            fields[f] = nf;
        }
        foreach (var m in s.Methods)
        {
            if (m.HasGenericParameters) throw new Exception("generic method not supported: " + m.FullName);
            var nm = new MethodDefinition(m.Name, m.Attributes, Imp(m.ReturnType)) { ImplAttributes = m.ImplAttributes, SemanticsAttributes = m.SemanticsAttributes };
            foreach (var p in m.Parameters) nm.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, Imp(p.ParameterType)));
            nm.HasThis = m.HasThis; nm.ExplicitThis = m.ExplicitThis;
            d.Methods.Add(nm);
            methods[m] = nm;
        }
        foreach (var p in s.Properties)
        {
            var np = new PropertyDefinition(p.Name, p.Attributes, Imp(p.PropertyType));
            if (p.GetMethod != null) np.GetMethod = methods[p.GetMethod];
            if (p.SetMethod != null) np.SetMethod = methods[p.SetMethod];
            d.Properties.Add(np);
        }
    }

    void Body(MethodDefinition s, MethodDefinition d)
    {
        if (!s.HasBody) return;
        var sb = s.Body;
        var db = d.Body;
        db.InitLocals = sb.InitLocals;
        db.MaxStackSize = sb.MaxStackSize;
        foreach (var v in sb.Variables) db.Variables.Add(new VariableDefinition(Imp(v.VariableType)));
        var map = new Dictionary<Instruction, Instruction>();
        foreach (var i in sb.Instructions)
        {
            var ni = Instruction.Create(OpCodes.Nop);
            ni.OpCode = i.OpCode;
            ni.Operand = i.Operand;                       // fixed below
            map[i] = ni;
            db.Instructions.Add(ni);
        }
        foreach (var i in sb.Instructions)
        {
            var ni = map[i];
            object o = i.Operand;
            if (o is Instruction) ni.Operand = map[(Instruction)o];
            else if (o is Instruction[]) ni.Operand = ((Instruction[])o).Select(x => map[x]).ToArray();
            else if (o is TypeReference) ni.Operand = Imp((TypeReference)o);
            else if (o is MethodReference) ni.Operand = ImpMethod((MethodReference)o);
            else if (o is FieldReference) ni.Operand = ImpField((FieldReference)o);
            else if (o is VariableDefinition) ni.Operand = db.Variables[sb.Variables.IndexOf((VariableDefinition)o)];
            else if (o is ParameterDefinition) ni.Operand = d.Parameters[((ParameterDefinition)o).Index];
            else if (o is CallSite) throw new Exception("call sites not supported in " + s.FullName);
        }
        foreach (var h in sb.ExceptionHandlers)
        {
            var nh = new ExceptionHandler(h.HandlerType)
            {
                TryStart = map[h.TryStart],
                TryEnd = h.TryEnd == null ? null : map[h.TryEnd],
                HandlerStart = map[h.HandlerStart],
                HandlerEnd = h.HandlerEnd == null ? null : map[h.HandlerEnd],
                FilterStart = h.FilterStart == null ? null : map[h.FilterStart],
                CatchType = h.CatchType == null ? null : Imp(h.CatchType),
            };
            db.ExceptionHandlers.Add(nh);
        }
    }

    TypeReference Imp(TypeReference t)
    {
        if (t is ArrayType) { var a = (ArrayType)t; return new ArrayType(Imp(a.ElementType), a.Rank); }
        if (t is ByReferenceType) return new ByReferenceType(Imp(((ByReferenceType)t).ElementType));
        if (t is GenericInstanceType)
        {
            var g = (GenericInstanceType)t;
            var ng = new GenericInstanceType(Imp(g.ElementType));
            foreach (var a in g.GenericArguments) ng.GenericArguments.Add(Imp(a));
            return ng;
        }
        if (t is GenericParameter) return t;                 // only appears inside references to generic framework members
        if (t.Scope == src || t.Module == src && t is TypeDefinition)
        {
            var def = t.Resolve();
            if (def != null && types.ContainsKey(def)) return types[def];
        }
        return dst.ImportReference(t);
    }

    MethodReference ImpMethod(MethodReference m)
    {
        var def = m.DeclaringType.Scope == src || m.DeclaringType.Module == src ? m.Resolve() : null;
        if (def != null && methods.ContainsKey(def)) return methods[def];
        if (m is GenericInstanceMethod)
        {
            var gm = (GenericInstanceMethod)m;
            var ng = new GenericInstanceMethod(ImpMethod(gm.ElementMethod));
            foreach (var a in gm.GenericArguments) ng.GenericArguments.Add(Imp(a));
            return ng;
        }
        // Member of a framework type.  Rebuilt by hand because ImportReference would not remap type arguments
        // that come from this library (e.g. List<MonsuOffline.Price>::Add).
        var r = new MethodReference(m.Name, Imp(m.ReturnType), Imp(m.DeclaringType))
        {
            HasThis = m.HasThis,
            ExplicitThis = m.ExplicitThis,
            CallingConvention = m.CallingConvention,
        };
        foreach (var p in m.Parameters) r.Parameters.Add(new ParameterDefinition(Imp(p.ParameterType)));
        foreach (var gp in m.GenericParameters) r.GenericParameters.Add(new GenericParameter(gp.Name, r));
        return r;
    }

    FieldReference ImpField(FieldReference f)
    {
        var def = f.DeclaringType.Scope == src || f.DeclaringType.Module == src ? f.Resolve() : null;
        if (def != null && fields.ContainsKey(def)) return fields[def];
        return new FieldReference(f.Name, Imp(f.FieldType), Imp(f.DeclaringType));
    }
}
