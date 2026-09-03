using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using fNbt;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CoreDawn.Save
{
    /// <summary>
    /// 세이브 DTO ↔ NBT(fNbt) 변환 — 리플렉션으로 public 필드를 읽고 쓴다. 키는 <c>[JsonProperty]</c> 이름(없으면 필드 이름),
    /// <c>[JsonIgnore]</c>·const·static은 뺀다. JSON 시절의 규칙을 그대로 옮겼다: null 은 쓰지 않고(태그 없음 = 기본값),
    /// 좌표는 짧은 목록(Vector3 → Float×3, Quaternion → Float×4, Vector2Int → IntArray[2]), enum 은 이름 문자열.
    ///
    /// 태그 대응: bool→Byte(0/1) · int→Int · uint/long→Long · float→Float · double→Double · string→String ·
    /// List/배열→List(같은 종류만; null 항목은 건너뛴다) · Dictionary&lt;string,T&gt;→Compound · 그 외 클래스→Compound ·
    /// byte[]/int[]/long[]→ByteArray/IntArray/LongArray · 이미 NbtTag 인 값은 복제해 그대로.
    ///
    /// 읽기는 너그럽다: 숫자 태그는 어느 폭이든 필드 타입으로 맞추고, 없는 태그는 필드 초기값을 둔다. 모르는 태그는 무시.
    /// Sim 의 ISaveableModule 은 JToken 을 받으므로 <see cref="ToJson"/> 다리로 넘긴다(Sim 은 fNbt 를 모른다).
    /// </summary>
    public static class SaveNbt
    {
        // ── 쓰기 ────────────────────────────────────────────────

        /// <summary>DTO → 이름 있는 compound. null 이면 null.</summary>
        public static NbtCompound ToTag(object dto, string name = "")
        {
            if (dto == null) return null;
            var tag = Encode(name, dto);
            if (tag is NbtCompound c) return c;
            throw new InvalidOperationException($"[SaveNbt] {dto.GetType().Name} 은 compound 가 아니라 {tag?.TagType} 로 변환됩니다 — 모듈 상태는 클래스여야 합니다.");
        }

        static NbtTag Encode(string name, object v)
        {
            switch (v)
            {
                case null: return null;
                case NbtTag t: { var c = (NbtTag)t.Clone(); c.Name = name; return c; }
                case bool b: return new NbtByte(name, (byte)(b ? 1 : 0));
                case byte x: return new NbtByte(name, x);
                case sbyte x: return new NbtByte(name, unchecked((byte)x));
                case short x: return new NbtShort(name, x);
                case ushort x: return new NbtShort(name, unchecked((short)x));
                case int x: return new NbtInt(name, x);
                case uint x: return new NbtLong(name, x);   // 부호 없이 보존 — Int 로 넣으면 JSON 다리에서 음수가 된다
                case long x: return new NbtLong(name, x);
                case ulong x: return new NbtLong(name, unchecked((long)x));
                case float x: return new NbtFloat(name, x);
                case double x: return new NbtDouble(name, x);
                case decimal x: return new NbtDouble(name, (double)x);
                case string s: return new NbtString(name, s);
                case Enum e: return new NbtString(name, e.ToString());
                case Vector2 p: return Floats(name, p.x, p.y);
                case Vector3 p: return Floats(name, p.x, p.y, p.z);
                case Vector4 p: return Floats(name, p.x, p.y, p.z, p.w);
                case Quaternion q: return Floats(name, q.x, q.y, q.z, q.w);
                case Color c: return Floats(name, c.r, c.g, c.b, c.a);
                case Vector2Int p: return new NbtIntArray(name, new[] { p.x, p.y });
                case Vector3Int p: return new NbtIntArray(name, new[] { p.x, p.y, p.z });
                case byte[] a: return new NbtByteArray(name, (byte[])a.Clone());
                case int[] a: return new NbtIntArray(name, (int[])a.Clone());
                case long[] a: return new NbtLongArray(name, (long[])a.Clone());
                case IDictionary d:
                {
                    var c = new NbtCompound(name);
                    foreach (DictionaryEntry e in d)
                    {
                        if (e.Key is not string key) throw new InvalidOperationException($"[SaveNbt] 사전 키는 문자열이어야 합니다: {e.Key?.GetType().Name}");
                        var child = Encode(key, e.Value);
                        if (child != null) c.Add(child);
                    }
                    return c;
                }
                case IEnumerable seq:
                {
                    // fNbt 는 원소 없는 목록에 타입이 없으면 쓰기를 거부한다 — 컬렉션의 원소 타입에서 미리 정한다
                    var list = new NbtList(name, ListTypeOf(v.GetType()));
                    foreach (var item in seq)
                    {
                        var child = Encode(null, item);
                        if (child != null) list.Add(child);   // null 항목은 NBT 에 자리가 없다
                    }
                    return list;
                }
            }
            var comp = new NbtCompound(name);
            foreach (var (field, key) in FieldsOf(v.GetType()))
            {
                var child = Encode(key, field.GetValue(v));
                if (child != null) comp.Add(child);
            }
            return comp;
        }

        /// <summary>컬렉션 타입의 원소가 될 태그 종류 — 빈 목록도 타입을 갖게. 모르면 Compound.</summary>
        static NbtTagType ListTypeOf(Type collection)
        {
            Type et = collection.IsArray ? collection.GetElementType() : null;
            if (et == null)
                foreach (var i in collection.GetInterfaces())
                    if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)) { et = i.GetGenericArguments()[0]; break; }
            if (et == null) return NbtTagType.Compound;
            et = Nullable.GetUnderlyingType(et) ?? et;
            if (typeof(NbtTag).IsAssignableFrom(et)) return et == typeof(NbtCompound) ? NbtTagType.Compound : et == typeof(NbtList) ? NbtTagType.List : NbtTagType.Compound;
            if (et == typeof(bool) || et == typeof(byte) || et == typeof(sbyte)) return NbtTagType.Byte;
            if (et == typeof(short) || et == typeof(ushort)) return NbtTagType.Short;
            if (et == typeof(int)) return NbtTagType.Int;
            if (et == typeof(uint) || et == typeof(long) || et == typeof(ulong)) return NbtTagType.Long;
            if (et == typeof(float)) return NbtTagType.Float;
            if (et == typeof(double) || et == typeof(decimal)) return NbtTagType.Double;
            if (et == typeof(string) || et.IsEnum) return NbtTagType.String;
            if (et == typeof(Vector2) || et == typeof(Vector3) || et == typeof(Vector4) || et == typeof(Quaternion) || et == typeof(Color)) return NbtTagType.List;
            if (et == typeof(Vector2Int) || et == typeof(Vector3Int) || et == typeof(int[])) return NbtTagType.IntArray;
            if (et == typeof(byte[])) return NbtTagType.ByteArray;
            if (et == typeof(long[])) return NbtTagType.LongArray;
            if (typeof(IDictionary).IsAssignableFrom(et)) return NbtTagType.Compound;
            if (typeof(IEnumerable).IsAssignableFrom(et)) return NbtTagType.List;
            return NbtTagType.Compound;
        }

        static NbtList Floats(string name, params float[] xs)
        {
            var l = new NbtList(name, NbtTagType.Float);
            foreach (var x in xs) l.Add(new NbtFloat(x));
            return l;
        }

        // ── 읽기 ────────────────────────────────────────────────

        /// <summary>compound → DTO. 태그가 없으면 default. 형태가 틀리면 오류 로그 + default(모듈 하나 때문에 전부 잃지 않는다).</summary>
        public static T FromTag<T>(NbtTag tag)
        {
            if (tag == null) return default;
            try { return (T)Decode(typeof(T), tag); }
            catch (Exception e)
            {
                Debug.LogError($"[Save] '{typeof(T).Name}' NBT 역직렬화 실패 — 이 부분은 건너뜁니다: {e.Message}");
                return default;
            }
        }

        public static object Decode(Type t, NbtTag tag)
        {
            if (tag == null) return t.IsValueType ? Activator.CreateInstance(t) : null;
            var nullable = Nullable.GetUnderlyingType(t);
            if (nullable != null) t = nullable;

            if (typeof(NbtTag).IsAssignableFrom(t)) return tag.Clone();
            if (t == typeof(bool)) return Num(tag) != 0;
            if (t == typeof(byte)) return unchecked((byte)Int(tag));
            if (t == typeof(sbyte)) return unchecked((sbyte)Int(tag));
            if (t == typeof(short)) return unchecked((short)Int(tag));
            if (t == typeof(ushort)) return unchecked((ushort)Int(tag));
            if (t == typeof(int)) return unchecked((int)Int(tag));
            if (t == typeof(uint)) return unchecked((uint)Int(tag));
            if (t == typeof(long)) return Int(tag);
            if (t == typeof(ulong)) return unchecked((ulong)Int(tag));
            if (t == typeof(float)) return (float)Num(tag);
            if (t == typeof(double)) return Num(tag);
            if (t == typeof(decimal)) return (decimal)Num(tag);
            if (t == typeof(string)) return tag is NbtString s ? s.Value : tag.ToString();
            if (t.IsEnum)
            {
                if (tag is NbtString es) return Enum.Parse(t, es.Value, ignoreCase: true);
                return Enum.ToObject(t, Int(tag));
            }
            if (t == typeof(Vector2)) { var f = FloatsOf(tag, 2); return new Vector2(f[0], f[1]); }
            if (t == typeof(Vector3)) { var f = FloatsOf(tag, 3); return new Vector3(f[0], f[1], f[2]); }
            if (t == typeof(Vector4)) { var f = FloatsOf(tag, 4); return new Vector4(f[0], f[1], f[2], f[3]); }
            if (t == typeof(Quaternion)) { var f = FloatsOf(tag, 4); return new Quaternion(f[0], f[1], f[2], f[3]); }
            if (t == typeof(Color)) { var f = FloatsOf(tag, 4); return new Color(f[0], f[1], f[2], f[3]); }
            if (t == typeof(Vector2Int)) { var i = IntsOf(tag, 2); return new Vector2Int(i[0], i[1]); }
            if (t == typeof(Vector3Int)) { var i = IntsOf(tag, 3); return new Vector3Int(i[0], i[1], i[2]); }
            if (t == typeof(byte[])) return tag is NbtByteArray ba ? (byte[])ba.Value.Clone() : Array.Empty<byte>();
            if (t == typeof(int[])) return IntsOf(tag, 0);
            if (t == typeof(long[])) return tag is NbtLongArray la ? (long[])la.Value.Clone() : Array.ConvertAll(IntsOf(tag, 0), x => (long)x);

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>) && t.GetGenericArguments()[0] == typeof(string))
            {
                var vt = t.GetGenericArguments()[1];
                var dict = (IDictionary)Activator.CreateInstance(t);
                if (tag is NbtCompound c)
                    foreach (var child in c.Tags) dict[child.Name] = Decode(vt, child);
                return dict;
            }
            if (t.IsArray)
            {
                var et = t.GetElementType();
                var items = tag is NbtList l ? l : new NbtList();
                var arr = Array.CreateInstance(et, items.Count);
                for (int i = 0; i < items.Count; i++) arr.SetValue(Decode(et, items[i]), i);
                return arr;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var et = t.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(t);
                if (tag is NbtList l) foreach (var item in l) list.Add(Decode(et, item));
                return list;
            }

            // 그 밖의 클래스/구조체 — 필드 단위. 태그가 없는 필드는 초기값 그대로.
            if (tag is not NbtCompound comp) throw new InvalidOperationException($"{t.Name} 자리에 {tag.TagType} 태그가 왔습니다");
            var obj = Activator.CreateInstance(t);
            foreach (var (field, key) in FieldsOf(t))
            {
                var child = comp[key];
                if (child != null) field.SetValue(obj, Decode(field.FieldType, child));
            }
            return obj;
        }

        static double Num(NbtTag t) => t.TagType switch
        {
            NbtTagType.Byte => ((NbtByte)t).Value,
            NbtTagType.Short => ((NbtShort)t).Value,
            NbtTagType.Int => ((NbtInt)t).Value,
            NbtTagType.Long => ((NbtLong)t).Value,
            NbtTagType.Float => ((NbtFloat)t).Value,
            NbtTagType.Double => ((NbtDouble)t).Value,
            NbtTagType.String => double.Parse(((NbtString)t).Value, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"숫자 자리에 {t.TagType} 태그"),
        };

        static long Int(NbtTag t) => t.TagType switch
        {
            NbtTagType.Byte => ((NbtByte)t).Value,
            NbtTagType.Short => ((NbtShort)t).Value,
            NbtTagType.Int => ((NbtInt)t).Value,
            NbtTagType.Long => ((NbtLong)t).Value,
            NbtTagType.Float => (long)((NbtFloat)t).Value,
            NbtTagType.Double => (long)((NbtDouble)t).Value,
            NbtTagType.String => long.Parse(((NbtString)t).Value, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"정수 자리에 {t.TagType} 태그"),
        };

        static float[] FloatsOf(NbtTag t, int n)
        {
            var r = new float[n];
            if (t is NbtList l) for (int i = 0; i < n && i < l.Count; i++) r[i] = (float)Num(l[i]);
            else if (t is NbtIntArray ia) for (int i = 0; i < n && i < ia.Value.Length; i++) r[i] = ia.Value[i];
            return r;
        }

        static int[] IntsOf(NbtTag t, int n)
        {
            if (t is NbtIntArray ia) { var a = (int[])ia.Value.Clone(); if (n == 0 || a.Length == n) return a; var r = new int[n]; Array.Copy(a, r, Math.Min(n, a.Length)); return r; }
            if (t is NbtList l)
            {
                var r = new int[n == 0 ? l.Count : n];
                for (int i = 0; i < r.Length && i < l.Count; i++) r[i] = unchecked((int)Int(l[i]));
                return r;
            }
            return new int[n];
        }

        // ── 필드 표 ─────────────────────────────────────────────

        static readonly Dictionary<Type, List<(FieldInfo field, string key)>> _fields = new();

        static List<(FieldInfo field, string key)> FieldsOf(Type t)
        {
            if (_fields.TryGetValue(t, out var cached)) return cached;
            var list = new List<(FieldInfo, string)>();
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.IsInitOnly || f.IsLiteral) continue;
                if (f.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                var jp = f.GetCustomAttribute<JsonPropertyAttribute>();
                list.Add((f, string.IsNullOrEmpty(jp?.PropertyName) ? f.Name : jp.PropertyName));
            }
            _fields[t] = list;
            return list;
        }

        // ── JToken 다리 — Sim 의 ISaveableModule.RestoreState(JToken) 용 ──

        /// <summary>NBT → JToken. Byte 는 정수(0/1)로 가고, Newtonsoft 가 bool 필드로 바꿔 읽는다.
        /// 좌표는 목록([x,y,z])이라 기본 Newtonsoft 로는 Vector3 필드에 안 들어간다 — Sim 모듈 상태는 스칼라·문자열·목록·중첩만 쓴다(지금 그렇다).</summary>
        public static JToken ToJson(NbtTag tag)
        {
            switch (tag)
            {
                case null: return null;
                case NbtCompound c: { var o = new JObject(); foreach (var child in c.Tags) o[child.Name] = ToJson(child); return o; }
                case NbtList l: { var a = new JArray(); foreach (var child in l) a.Add(ToJson(child)); return a; }
                case NbtByte b: return new JValue((int)b.Value);
                case NbtShort s: return new JValue((int)s.Value);
                case NbtInt i: return new JValue(i.Value);
                case NbtLong lg: return new JValue(lg.Value);
                case NbtFloat f: return new JValue(f.Value);
                case NbtDouble d: return new JValue(d.Value);
                case NbtString str: return new JValue(str.Value);
                case NbtByteArray ba: return new JArray(ba.Value);
                case NbtIntArray ia: return new JArray(ia.Value);
                case NbtLongArray la: return new JArray(la.Value);
            }
            throw new InvalidOperationException($"[SaveNbt] JSON 으로 옮길 수 없는 태그: {tag.TagType}");
        }

        /// <summary>두 태그가 값까지 같은가(이름은 무시, compound 는 키 순서 무시). 디버그 비교용.</summary>
        public static bool DeepEquals(NbtTag a, NbtTag b)
        {
            if (a == null || b == null) return a == b;
            if (a.TagType != b.TagType) return false;
            switch (a)
            {
                case NbtCompound ca:
                {
                    var cb = (NbtCompound)b;
                    if (ca.Count != cb.Count) return false;
                    foreach (var child in ca.Tags) if (!DeepEquals(child, cb[child.Name])) return false;
                    return true;
                }
                case NbtList la:
                {
                    var lb = (NbtList)b;
                    if (la.Count != lb.Count) return false;
                    for (int i = 0; i < la.Count; i++) if (!DeepEquals(la[i], lb[i])) return false;
                    return true;
                }
                case NbtByteArray x: return StructuralComparisons.StructuralEqualityComparer.Equals(x.Value, ((NbtByteArray)b).Value);
                case NbtIntArray x: return StructuralComparisons.StructuralEqualityComparer.Equals(x.Value, ((NbtIntArray)b).Value);
                case NbtLongArray x: return StructuralComparisons.StructuralEqualityComparer.Equals(x.Value, ((NbtLongArray)b).Value);
                case NbtString x: return x.Value == ((NbtString)b).Value;
                default: return Num(a).Equals(Num(b));
            }
        }
    }
}
