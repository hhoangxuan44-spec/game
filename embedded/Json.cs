// Minimal JSON reader/writer for the embedded offline server.
// Model: object -> Dictionary<string, object>, array -> List<object>, string, double, bool, null.
// Only mscorlib 2.0 types are used: this code is compiled against Unity 4.6's Mono and merged into Assembly-CSharp.dll.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MonsuOffline
{
    public static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            object v = ReadValue(s, ref i);
            return v;
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object ReadValue(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{')
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                i++;
                Skip(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Skip(s, ref i);
                    string k = ReadString(s, ref i);
                    Skip(s, ref i);
                    if (s[i] != ':') throw new FormatException("expected :");
                    i++;
                    d[k] = ReadValue(s, ref i);
                    Skip(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("expected , or }");
                }
            }
            if (c == '[')
            {
                List<object> l = new List<object>();
                i++;
                Skip(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ReadValue(s, ref i));
                    Skip(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("expected , or ]");
                }
            }
            if (c == '"') return ReadString(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (st == i) throw new FormatException("bad json at " + i);
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }

        static int Hex4(string s, int at)
        {
            int v = 0;
            for (int k = 0; k < 4; k++)
            {
                char c = s[at + k];
                int d = c >= '0' && c <= '9' ? c - '0' : (c >= 'a' && c <= 'f' ? c - 'a' + 10 : c - 'A' + 10);
                v = v * 16 + d;
            }
            return v;
        }

        static string ReadString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("expected string");
            i++;
            StringBuilder sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                if (e == 'n') sb.Append('\n');
                else if (e == 't') sb.Append('\t');
                else if (e == 'r') sb.Append('\r');
                else if (e == 'b') sb.Append('\b');
                else if (e == 'f') sb.Append('\f');
                else if (e == 'u') { sb.Append((char)Hex4(s, i)); i += 4; }
                else sb.Append(e);
            }
            i++;
            return sb.ToString();
        }

        public static string Write(object v)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, v);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is int) { sb.Append(((int)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is long) { sb.Append(((long)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is double || v is float)
            {
                double d = J.ToD(v);
                if (d == Math.Floor(d) && Math.Abs(d) < 1e15) sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            Dictionary<string, object> m = v as Dictionary<string, object>;
            if (m != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in m)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    WriteValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            System.Collections.IEnumerable list = v as System.Collections.IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object o in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, o);
                }
                sb.Append(']');
                return;
            }
            throw new ArgumentException("cannot serialize " + v.GetType());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ' || c > '~') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }

    /// <summary>Small helpers so handler code reads like the Python server.</summary>
    public static class J
    {
        public static double ToD(object o)
        {
            if (o is double) return (double)o;
            if (o is int) return (int)o;
            if (o is long) return (long)o;
            if (o is float) return (float)o;
            return 0;
        }

        public static Dictionary<string, object> Map() { return new Dictionary<string, object>(); }
        public static List<object> List() { return new List<object>(); }

        public static Dictionary<string, object> M(object o) { return o as Dictionary<string, object>; }
        public static List<object> L(object o) { return o as List<object>; }

        public static object Get(Dictionary<string, object> d, string k)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v)) return v;
            return null;
        }

        public static int Int(Dictionary<string, object> d, string k, int def)
        {
            object v = Get(d, k);
            if (v == null) return def;
            if (v is string) { int r; return int.TryParse((string)v, out r) ? r : def; }
            if (v is bool) return (bool)v ? 1 : 0;
            return (int)Math.Round(J.ToD(v));
        }

        public static double Dbl(Dictionary<string, object> d, string k, double def)
        {
            object v = Get(d, k);
            if (v == null || v is string || v is bool) return def;
            return J.ToD(v);
        }

        public static string Str(Dictionary<string, object> d, string k, string def)
        {
            string v = Get(d, k) as string;
            return v == null ? def : v;
        }

        public static bool Bool(Dictionary<string, object> d, string k, bool def)
        {
            object v = Get(d, k);
            return v is bool ? (bool)v : def;
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string k)
        {
            Dictionary<string, object> v = Get(d, k) as Dictionary<string, object>;
            return v == null ? new Dictionary<string, object>() : v;
        }

        public static List<object> Arr(Dictionary<string, object> d, string k)
        {
            List<object> v = Get(d, k) as List<object>;
            return v == null ? new List<object>() : v;
        }
    }
}
