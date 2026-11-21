using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace PokeSpriteCreator.Json
{
    /// <summary>Insertion-ordered JSON object.</summary>
    public class JsonObject : IEnumerable<KeyValuePair<string, object>>
    {
        readonly List<string> _keys = new List<string>();
        readonly Dictionary<string, object> _map = new Dictionary<string, object>();

        public object this[string key]
        {
            get { object v; return _map.TryGetValue(key, out v) ? v : null; }
            set { if (!_map.ContainsKey(key)) _keys.Add(key); _map[key] = value; }
        }
        public bool ContainsKey(string key) { return _map.ContainsKey(key); }
        public void Remove(string key) { if (_map.Remove(key)) _keys.Remove(key); }
        public IList<string> Keys { get { return _keys; } }
        public int Count { get { return _keys.Count; } }

        public string GetString(string key, string def = null) { var v = this[key]; return v == null ? def : Convert.ToString(v, CultureInfo.InvariantCulture); }
        public int GetInt(string key, int def = 0) { var v = this[key]; return v == null ? def : Convert.ToInt32(v, CultureInfo.InvariantCulture); }
        public bool GetBool(string key, bool def = false) { var v = this[key]; return v is bool ? (bool)v : def; }
        public JsonObject GetObject(string key) { return this[key] as JsonObject; }
        public List<object> GetArray(string key) { return this[key] as List<object>; }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            foreach (var k in _keys) yield return new KeyValuePair<string, object>(k, _map[k]);
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    /// <summary>Marks a property that should not be written to JSON.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class JsonIgnoreAttribute : Attribute { }

    /// <summary>Writes a list property on a single line (e.g. tile rows, stat arrays).</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class JsonCompactAttribute : Attribute { }

    /// <summary>
    /// Small dependency-free JSON reader/writer with reflection-based object mapping.
    /// Public read/write properties are serialized in declaration order; nulls, empty
    /// lists and false/0 defaults are skipped to keep project files short and readable.
    /// </summary>
    public static class Json
    {
        // ------------------------------------------------------------------ parse
        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.SkipWs();
            var v = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw p.Error("Unexpected trailing characters");
            return v;
        }

        class Parser
        {
            readonly string s; int i;
            public Parser(string text) { s = text; i = 0; if (s.Length > 0 && s[0] == '﻿') i = 1; }
            public bool End { get { return i >= s.Length; } }
            public Exception Error(string msg)
            {
                int line = 1, col = 1;
                for (int k = 0; k < i && k < s.Length; k++) { if (s[k] == '\n') { line++; col = 1; } else col++; }
                return new FormatException(msg + " at line " + line + ", column " + col);
            }
            public void SkipWs()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                    break;
                }
            }
            public object ReadValue()
            {
                if (End) throw Error("Unexpected end of JSON");
                char c = s[i];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't' && Match("true")) return true;
                if (c == 'f' && Match("false")) return false;
                if (c == 'n' && Match("null")) return null;
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                throw Error("Unexpected character '" + c + "'");
            }
            bool Match(string w)
            {
                if (string.CompareOrdinal(s, i, w, 0, w.Length) == 0) { i += w.Length; return true; }
                return false;
            }
            JsonObject ReadObject()
            {
                var o = new JsonObject(); i++; SkipWs();
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    SkipWs();
                    if (End || s[i] != '"') throw Error("Expected property name");
                    string k = ReadString(); SkipWs();
                    if (End || s[i] != ':') throw Error("Expected ':'");
                    i++; SkipWs();
                    o[k] = ReadValue(); SkipWs();
                    if (End) throw Error("Unterminated object");
                    if (s[i] == ',') { i++; SkipWs(); if (i < s.Length && s[i] == '}') { i++; return o; } continue; }
                    if (s[i] == '}') { i++; return o; }
                    throw Error("Expected ',' or '}'");
                }
            }
            List<object> ReadArray()
            {
                var a = new List<object>(); i++; SkipWs();
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    SkipWs(); a.Add(ReadValue()); SkipWs();
                    if (End) throw Error("Unterminated array");
                    if (s[i] == ',') { i++; SkipWs(); if (i < s.Length && s[i] == ']') { i++; return a; } continue; }
                    if (s[i] == ']') { i++; return a; }
                    throw Error("Expected ',' or ']'");
                }
            }
            string ReadString()
            {
                i++; var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Error("Unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("Bad escape");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Error("Bad unicode escape");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                        default: throw Error("Bad escape '\\" + e + "'");
                    }
                }
            }
            object ReadNumber()
            {
                int st = i; bool isFloat = false;
                if (s[i] == '-') i++;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c >= '0' && c <= '9') { i++; continue; }
                    if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { isFloat = true; i++; continue; }
                    break;
                }
                string num = s.Substring(st, i - st);
                if (!isFloat)
                {
                    long l;
                    if (long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
                }
                return double.Parse(num, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }

        // ------------------------------------------------------------------ write
        public static string Write(object value, bool pretty = true)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, pretty ? 0 : -1, false);
            if (pretty) sb.Append('\n');
            return sb.ToString();
        }

        static void Indent(StringBuilder sb, int level) { sb.Append('\n'); sb.Append(' ', level * 2); }

        static void WriteValue(StringBuilder sb, object v, int level, bool compact)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is Enum) { WriteString(sb, v.ToString()); return; }
            if (v is double || v is float || v is decimal)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("0"); return; }
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
            }
            if (v is IConvertible && IsNumeric(v)) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            if (v is JsonObject) { WriteObject(sb, (JsonObject)v, level, compact); return; }
            if (v is IDictionary)
            {
                var jo = new JsonObject();
                foreach (DictionaryEntry de in (IDictionary)v) jo[Convert.ToString(de.Key, CultureInfo.InvariantCulture)] = de.Value;
                WriteObject(sb, jo, level, compact); return;
            }
            if (v is IEnumerable) { WriteArray(sb, (IEnumerable)v, level, compact); return; }
            WriteObject(sb, ToJsonObject(v), level, compact);
        }

        static bool IsNumeric(object v)
        {
            switch (Type.GetTypeCode(v.GetType()))
            {
                case TypeCode.Byte: case TypeCode.SByte: case TypeCode.Int16: case TypeCode.UInt16:
                case TypeCode.Int32: case TypeCode.UInt32: case TypeCode.Int64: case TypeCode.UInt64:
                    return true;
            }
            return false;
        }

        static void WriteObject(StringBuilder sb, JsonObject o, int level, bool compact)
        {
            if (o.Count == 0) { sb.Append("{}"); return; }
            bool pretty = level >= 0 && !compact;
            sb.Append('{');
            bool first = true;
            foreach (var kv in o)
            {
                if (!first) sb.Append(compact ? ", " : ",");
                first = false;
                if (pretty) Indent(sb, level + 1);
                WriteString(sb, kv.Key);
                sb.Append(level >= 0 ? ": " : ":");
                bool childCompact = compact || IsCompactValue(kv.Value);
                WriteValue(sb, kv.Value, level < 0 ? -1 : level + 1, childCompact);
            }
            if (pretty) Indent(sb, level);
            sb.Append('}');
        }

        // Arrays of plain numbers/short strings are written on one line.
        static bool IsCompactValue(object v)
        {
            var list = v as IEnumerable;
            if (list == null || v is string || v is JsonObject || v is IDictionary) return false;
            int n = 0;
            foreach (var item in list)
            {
                if (item == null || item is bool || IsNumeric(item) || item is double || item is float) { n++; continue; }
                var str = item as string;
                if (str != null && str.Length < 40) { n++; continue; }
                return false;
            }
            return true;
        }

        static void WriteArray(StringBuilder sb, IEnumerable a, int level, bool compact)
        {
            bool pretty = level >= 0 && !compact;
            sb.Append('[');
            bool first = true, any = false;
            foreach (var item in a)
            {
                any = true;
                if (!first) sb.Append(compact ? ", " : ",");
                first = false;
                if (pretty) Indent(sb, level + 1);
                WriteValue(sb, item, level < 0 ? -1 : level + 1, compact || (level >= 0 && IsCompactValue(item)));
            }
            if (pretty && any) Indent(sb, level);
            sb.Append(']');
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ------------------------------------------------------------------ object mapping
        static readonly Dictionary<Type, PropertyInfo[]> _propCache = new Dictionary<Type, PropertyInfo[]>();

        public static PropertyInfo[] GetSerializableProperties(Type t)
        {
            lock (_propCache)
            {
                PropertyInfo[] props;
                if (_propCache.TryGetValue(t, out props)) return props;
                var list = new List<PropertyInfo>();
                // Base-class properties first, then derived (declaration order within each).
                var chain = new List<Type>();
                for (var c = t; c != null && c != typeof(object); c = c.BaseType) chain.Insert(0, c);
                foreach (var c in chain)
                    foreach (var p in c.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0) continue;
                        if (p.IsDefined(typeof(JsonIgnoreAttribute), true)) continue;
                        list.Add(p);
                    }
                props = list.ToArray();
                _propCache[t] = props;
                return props;
            }
        }

        static readonly Dictionary<Type, object> _defaults = new Dictionary<Type, object>();

        static object DefaultInstance(Type t)
        {
            lock (_defaults)
            {
                object d;
                if (_defaults.TryGetValue(t, out d)) return d;
                try { d = t.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(t) : null; } catch { d = null; }
                _defaults[t] = d;
                return d;
            }
        }

        public static JsonObject ToJsonObject(object obj)
        {
            var o = new JsonObject();
            var def = DefaultInstance(obj.GetType());
            foreach (var p in GetSerializableProperties(obj.GetType()))
            {
                object v = p.GetValue(obj, null);
                object dv = def == null ? null : p.GetValue(def, null);
                if (IsSkippable(v, dv)) continue;
                o[p.Name] = v;
            }
            return o;
        }

        // A value is left out when it is empty or equal to the class default, so the
        // reader (which starts from a default instance) reproduces it exactly.
        static bool IsSkippable(object v, object defaultValue)
        {
            if (v == null) return defaultValue == null || (defaultValue is ICollection && ((ICollection)defaultValue).Count == 0);
            if (v is ICollection) return ((ICollection)v).Count == 0 && (defaultValue == null || (defaultValue is ICollection && ((ICollection)defaultValue).Count == 0));
            if (v is string && ((string)v).Length == 0) return defaultValue == null || "".Equals(defaultValue);
            if (v is string || v is bool || v.GetType().IsPrimitive || v is Enum) return v.Equals(defaultValue);
            return false;
        }

        public static T Deserialize<T>(string text) { return (T)ConvertTo(Parse(text), typeof(T)); }
        public static T FromFile<T>(string path) { return Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
        public static void ToFile(string path, object value)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Write(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static object ConvertTo(object v, Type t)
        {
            if (t == typeof(object)) return v;
            var nullable = Nullable.GetUnderlyingType(t);
            if (nullable != null) return v == null ? null : ConvertTo(v, nullable);
            if (v == null) return t.IsValueType ? Activator.CreateInstance(t) : null;
            if (t == typeof(string)) return v is string ? v : Convert.ToString(v, CultureInfo.InvariantCulture);
            if (t == typeof(bool)) return v is bool ? v : (object)(Convert.ToString(v).ToLowerInvariant() == "true");
            if (t.IsEnum) return Enum.Parse(t, Convert.ToString(v, CultureInfo.InvariantCulture), true);
            if (t.IsPrimitive || t == typeof(decimal)) return Convert.ChangeType(v, t, CultureInfo.InvariantCulture);
            if (t == typeof(JsonObject)) return v as JsonObject;
            if (t.IsArray)
            {
                var src = v as List<object> ?? new List<object>();
                var et = t.GetElementType();
                var arr = Array.CreateInstance(et, src.Count);
                for (int k = 0; k < src.Count; k++) arr.SetValue(ConvertTo(src[k], et), k);
                return arr;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var et = t.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(t);
                var src = v as List<object>;
                if (src != null) foreach (var item in src) list.Add(ConvertTo(item, et));
                return list;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var args = t.GetGenericArguments();
                var dict = (IDictionary)Activator.CreateInstance(t);
                var src = v as JsonObject;
                if (src != null) foreach (var kv in src) dict[ConvertTo(kv.Key, args[0])] = ConvertTo(kv.Value, args[1]);
                return dict;
            }
            var jo = v as JsonObject;
            if (jo == null) throw new FormatException("Expected object for " + t.Name);
            var obj = Activator.CreateInstance(t);
            foreach (var p in GetSerializableProperties(t))
            {
                if (!jo.ContainsKey(p.Name)) continue;
                p.SetValue(obj, ConvertTo(jo[p.Name], p.PropertyType), null);
            }
            return obj;
        }
    }
}
