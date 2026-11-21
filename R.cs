using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ModChecker
{
    /// <summary>
    /// Tiny reflection toolkit. Everything that touches Gorilla Tag's own code goes through this,
    /// so a renamed field after a game update degrades one feature instead of breaking the build.
    /// </summary>
    internal static class R
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<string, Type> typeCache = new Dictionary<string, Type>();
        private static readonly Dictionary<string, MemberInfo> memberCache = new Dictionary<string, MemberInfo>();
        private static readonly Dictionary<string, float> missCache = new Dictionary<string, float>();

        private static readonly string[] skipPrefix =
        {
            "System", "mscorlib", "Mono.", "Microsoft", "UnityEngine", "Unity.", "netstandard",
            "Newtonsoft", "BepInEx", "MonoMod", "0Harmony", "Harmony"
        };

        // ---------------------------------------------------------------- types

        public static Type FindType(string name)
        {
            Type t;
            if (typeCache.TryGetValue(name, out t))
            {
                if (t != null) return t;
                float when;
                if (missCache.TryGetValue(name, out when) && Time.realtimeSinceStartup < when) return null;   // recently missed
            }
            t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string an = asm.GetName().Name;
                bool skip = false;
                for (int i = 0; i < skipPrefix.Length; i++)
                    if (an.StartsWith(skipPrefix[i], StringComparison.Ordinal)) { skip = true; break; }
                if (skip) continue;

                try
                {
                    t = asm.GetType(name, false);
                    if (t == null)
                    {
                        foreach (var ty in asm.GetTypes())
                            if (ty.Name == name || ty.FullName == name) { t = ty; break; }
                    }
                }
                catch (ReflectionTypeLoadException e)
                {
                    foreach (var ty in e.Types)
                        if (ty != null && (ty.Name == name || ty.FullName == name)) { t = ty; break; }
                }
                catch { t = null; }

                if (t != null) break;
            }
            typeCache[name] = t;
            if (t == null) missCache[name] = Time.realtimeSinceStartup + 10f;
            return t;
        }

        // ---------------------------------------------------------------- liveness / conversion

        /// <summary>True for null and for destroyed UnityEngine.Objects.</summary>
        public static bool Dead(object o)
        {
            if (ReferenceEquals(o, null)) return true;
            var u = o as UnityEngine.Object;
            return !ReferenceEquals(u, null) && u == null;
        }

        public static string Str(object o)
        {
            if (o == null) return null;
            var s = o as string;
            return s ?? o.ToString();
        }

        public static int Int(object o, int fallback)
        {
            try { if (o is int) return (int)o; if (o is long) return (int)(long)o; if (o is float) return (int)(float)o; if (o is double) return (int)(double)o; }
            catch { }
            return fallback;
        }

        /// <summary>Unwraps Watchable&lt;T&gt;-style wrappers that expose a Value member.</summary>
        public static object Unwrap(object o)
        {
            if (o == null || o is string || o.GetType().IsPrimitive || o.GetType().IsEnum) return o;
            var v = Get(o, "Value", "value");
            return v ?? o;
        }

        public static List<object> List(object o)
        {
            var res = new List<object>();
            var e = o as IEnumerable;
            if (e == null || o is string) return res;
            try { foreach (var x in e) res.Add(x); } catch { }
            return res;
        }

        // ---------------------------------------------------------------- members

        private static MemberInfo FindMember(Type t, string name)
        {
            string key = t.FullName + "." + name;
            MemberInfo m;
            if (memberCache.TryGetValue(key, out m)) return m;
            m = null;
            for (var ty = t; ty != null && m == null; ty = ty.BaseType)
            {
                try
                {
                    var f = ty.GetField(name, Flags);
                    if (f != null) { m = f; break; }
                    var p = ty.GetProperty(name, Flags);
                    if (p != null && p.GetIndexParameters().Length == 0) m = p;
                }
                catch { }
            }
            memberCache[key] = m;
            return m;
        }

        /// <summary>Reads the first member that exists. Pass a System.Type as target to read statics.</summary>
        public static object Get(object target, params string[] names)
        {
            if (target == null) return null;
            Type t = target as Type;
            bool st = t != null;
            if (!st) t = target.GetType();
            for (int i = 0; i < names.Length; i++)
            {
                var m = FindMember(t, names[i]);
                if (m == null) continue;
                try
                {
                    var f = m as FieldInfo;
                    if (f != null) return f.GetValue(f.IsStatic ? null : (st ? null : target));
                    var p = (PropertyInfo)m;
                    var g = p.GetGetMethod(true);
                    if (g == null) continue;
                    return p.GetValue(g.IsStatic ? null : (st ? null : target), null);
                }
                catch { }
            }
            return null;
        }

        public static T Get<T>(object target, params string[] names)
        {
            var v = Get(target, names);
            if (v is T) return (T)v;
            return default(T);
        }

        /// <summary>Walks a dotted path; each segment may list alternatives separated by '|'.</summary>
        public static object Path(object target, string path)
        {
            object cur = target;
            foreach (var seg in path.Split('.'))
            {
                if (cur == null) return null;
                cur = Get(cur, seg.Split('|'));
            }
            return cur;
        }

        public static bool Set(object target, string name, object value)
        {
            if (target == null) return false;
            Type t = target as Type;
            bool st = t != null;
            if (!st) t = target.GetType();
            var m = FindMember(t, name);
            if (m == null) return false;
            try
            {
                var f = m as FieldInfo;
                if (f != null) { f.SetValue(f.IsStatic ? null : (st ? null : target), Coerce(value, f.FieldType)); return true; }
                var p = (PropertyInfo)m;
                var s = p.GetSetMethod(true);
                if (s == null) return false;
                p.SetValue(s.IsStatic ? null : (st ? null : target), Coerce(value, p.PropertyType), null);
                return true;
            }
            catch { return false; }
        }

        private static object Coerce(object v, Type t)
        {
            if (v == null || t.IsInstanceOfType(v)) return v;
            if (t.IsEnum) return v is string ? Enum.Parse(t, (string)v, true) : Enum.ToObject(t, v);
            return Convert.ChangeType(v, t);
        }

        /// <summary>All instance fields and readable properties of an object (name, value). Used to discover data classes.</summary>
        public static List<KeyValuePair<string, object>> Members(object o)
        {
            var res = new List<KeyValuePair<string, object>>();
            if (o == null) return res;
            const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            for (var ty = o.GetType(); ty != null && ty != typeof(object) && ty != typeof(MonoBehaviour) && ty != typeof(UnityEngine.Object); ty = ty.BaseType)
            {
                try
                {
                    foreach (var fi in ty.GetFields(f))
                    {
                        try { res.Add(new KeyValuePair<string, object>(fi.Name, fi.GetValue(o))); } catch { }
                    }
                    foreach (var pi in ty.GetProperties(f))
                    {
                        if (!pi.CanRead || pi.GetIndexParameters().Length != 0) continue;
                        try { res.Add(new KeyValuePair<string, object>(pi.Name, pi.GetValue(o, null))); } catch { }
                    }
                }
                catch { }
            }
            return res;
        }

        // ---------------------------------------------------------------- calls

        private static object[] ConvertArgs(ParameterInfo[] ps, object[] args)
        {
            if (args.Length > ps.Length) return null;
            var res = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                Type pt = ps[i].ParameterType;
                if (pt.IsByRef) return null;
                if (i >= args.Length)
                {
                    if (!ps[i].IsOptional) return null;
                    object dv = ps[i].DefaultValue;
                    res[i] = (dv == null || dv is DBNull || dv == Type.Missing) ? DefaultFor(pt) : dv;
                    continue;
                }
                object a = args[i];
                if (a == null)
                {
                    if (pt.IsValueType && Nullable.GetUnderlyingType(pt) == null) return null;
                    res[i] = null;
                    continue;
                }
                if (pt.IsInstanceOfType(a)) { res[i] = a; continue; }
                try { res[i] = Coerce(a, pt); }
                catch { return null; }
            }
            return res;
        }

        public static object DefaultFor(Type t)
        {
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }

        /// <summary>Calls the first overload that accepts the arguments. Pass a Type to call statics.</summary>
        public static bool TryCall(object target, string method, out object result, params object[] args)
        {
            result = null;
            if (target == null) return false;
            Type t = target as Type;
            bool st = t != null;
            if (!st) t = target.GetType();
            for (var ty = t; ty != null; ty = ty.BaseType)
            {
                MethodInfo[] ms;
                try { ms = ty.GetMethods(Flags); } catch { continue; }
                foreach (var mi in ms)
                {
                    if (mi.Name != method) continue;
                    if (st && !mi.IsStatic) continue;
                    var conv = ConvertArgs(mi.GetParameters(), args);
                    if (conv == null) continue;
                    try { result = mi.Invoke(mi.IsStatic ? null : target, conv); return true; }
                    catch (Exception e)
                    {
                        if (Plugin.Log != null) Plugin.Log.LogDebug(method + " failed: " + (e.InnerException ?? e).Message);
                    }
                }
            }
            return false;
        }

        public static object Call(object target, string method, params object[] args)
        {
            object r;
            TryCall(target, method, out r, args);
            return r;
        }

        /// <summary>
        /// Calls a method whatever its signature is, asking <paramref name="argFor"/> to build every parameter.
        /// Used for game methods whose overloads change between versions (join room, join public room...).
        /// </summary>
        public static bool CallLoose(object target, string method, Func<ParameterInfo, object> argFor)
        {
            if (target == null) return false;
            Type t = target as Type;
            bool st = t != null;
            if (!st) t = target.GetType();
            for (var ty = t; ty != null; ty = ty.BaseType)
            {
                MethodInfo[] ms;
                try { ms = ty.GetMethods(Flags); } catch { continue; }
                foreach (var mi in ms)
                {
                    if (mi.Name != method) continue;
                    if (st && !mi.IsStatic) continue;
                    var ps = mi.GetParameters();
                    var args = new object[ps.Length];
                    for (int i = 0; i < ps.Length; i++)
                    {
                        object a = argFor(ps[i]);
                        if (a == null || !ps[i].ParameterType.IsInstanceOfType(a)) a = a == null ? DefaultFor(ps[i].ParameterType) : a;
                        args[i] = a;
                    }
                    try { mi.Invoke(mi.IsStatic ? null : target, args); return true; }
                    catch (Exception e)
                    {
                        if (Plugin.Log != null) Plugin.Log.LogDebug(method + " (loose) failed: " + (e.InnerException ?? e).Message);
                    }
                }
            }
            return false;
        }
    }
}