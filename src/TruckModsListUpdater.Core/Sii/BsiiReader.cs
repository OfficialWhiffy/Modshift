using System.Globalization;
using System.Text;

namespace TruckModsListUpdater.Core.Sii;

/// <summary>
/// Minimal reader for SCS binary SII ("BSII" v1-3), adapted from ets2-mirror's save reader.
/// Values it does not need (vectors, quaternions) are skipped; strings, numbers, booleans,
/// ids and ordinals are kept as text.
/// </summary>
sealed class BsiiReader(byte[] data)
{
    int _pos;
    readonly Dictionary<uint, StructDef> _structs = new();

    sealed record FieldDef(string Name, uint Type, Dictionary<uint, string>? Ordinals);
    sealed record StructDef(string Name, List<FieldDef> Fields);

    const string EncChars = "0123456789abcdefghijklmnopqrstuvwxyz_";

    public List<SiiUnit> ReadAll()
    {
        try
        {
            return Read();
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or KeyNotFoundException or InvalidDataException)
        {
            throw new SiiFormatException($"Could not read binary SII at offset {_pos}: {e.Message}", e);
        }
    }

    List<SiiUnit> Read()
    {
        _pos = 4;
        uint version = U32();
        if (version is < 1 or > 3) throw new InvalidDataException($"Unsupported BSII version {version}");

        var units = new List<SiiUnit>();
        while (_pos < data.Length)
        {
            uint blockType = U32();
            if (blockType == 0)
            {
                if (data[_pos++] == 0) break; // end marker
                uint structId = U32();
                string name = Str();
                var fields = new List<FieldDef>();
                while (true)
                {
                    uint t = U32();
                    if (t == 0) break;
                    string fname = Str();
                    Dictionary<uint, string>? ordinals = null;
                    if (t == 0x37)
                    {
                        ordinals = new();
                        uint n = U32();
                        for (int i = 0; i < n; i++) ordinals[U32()] = Str();
                    }
                    fields.Add(new FieldDef(fname, t, ordinals));
                }
                _structs[structId] = new StructDef(name, fields);
            }
            else
            {
                var def = _structs[blockType];
                var unit = new SiiUnit(def.Name, Id());
                foreach (var f in def.Fields)
                {
                    switch (Value(f))
                    {
                        case null: break;
                        case string[] a: unit.Arrays[f.Name] = [.. a]; break;
                        case string s: unit.Values[f.Name] = s; break;
                    }
                }
                units.Add(unit);
            }
        }
        return units;
    }

    /// <summary>Returns a string, a string[] for arrays, or null for skipped types.</summary>
    object? Value(FieldDef f)
    {
        var inv = CultureInfo.InvariantCulture;
        switch (f.Type)
        {
            case 0x01: return Str();
            case 0x02: return Arr(Str);
            case 0x03: return EncStr();
            case 0x04: return Arr(EncStr);
            case 0x05: return F32().ToString(inv);
            case 0x06: return Arr(() => F32().ToString(inv));
            case 0x07: Skip(8); return null;
            case 0x08: SkipArr(8); return null;
            case 0x09: Skip(12); return null;
            case 0x0A: SkipArr(12); return null;
            case 0x11: Skip(12); return null;
            case 0x12: SkipArr(12); return null;
            case 0x17: Skip(16); return null;
            case 0x18: SkipArr(16); return null;
            case 0x19: Skip(32); return null;
            case 0x1A: SkipArr(32); return null;
            case 0x25: return I32().ToString(inv);
            case 0x26: return Arr(() => I32().ToString(inv));
            case 0x27: case 0x2F: return U32().ToString(inv);
            case 0x28: return Arr(() => U32().ToString(inv));
            case 0x29: case 0x2B: Skip(2); return null;
            case 0x2A: case 0x2C: SkipArr(2); return null;
            case 0x31: return ((long)U64()).ToString(inv);
            case 0x32: return Arr(() => ((long)U64()).ToString(inv));
            case 0x33: return U64().ToString(inv);
            case 0x34: return Arr(() => U64().ToString(inv));
            case 0x35: return data[_pos++] != 0 ? "true" : "false";
            case 0x36: return Arr(() => data[_pos++] != 0 ? "true" : "false");
            case 0x37: { uint k = U32(); return f.Ordinals!.GetValueOrDefault(k, k.ToString(inv)); }
            case 0x39: case 0x3B: case 0x3D: return Id();
            case 0x3A: case 0x3C: case 0x3E: return Arr(Id);
            default: throw new InvalidDataException($"Unknown BSII value type 0x{f.Type:X2} ({f.Name})");
        }
    }

    string Id()
    {
        byte parts = data[_pos++];
        if (parts == 0xFF) return NamelessId(U64());
        var sb = new StringBuilder();
        for (int i = 0; i < parts; i++)
        {
            if (i > 0) sb.Append('.');
            sb.Append(EncStr());
        }
        return sb.ToString();
    }

    /// <summary>Formats like the text SII: _nameless.1a2b.3c4d.5e6f.7081 (leading zero groups dropped).</summary>
    static string NamelessId(ulong v)
    {
        var groups = new List<string>();
        for (int shift = 48; shift >= 0; shift -= 16)
        {
            ushort g = (ushort)(v >> shift);
            if (groups.Count == 0 && g == 0 && shift > 0) continue;
            groups.Add(groups.Count == 0 ? g.ToString("x") : g.ToString("x4"));
        }
        return "_nameless." + string.Join('.', groups);
    }

    string EncStr()
    {
        ulong v = U64();
        var sb = new StringBuilder();
        while (v != 0)
        {
            int idx = (int)(v % 38);
            v /= 38;
            if (idx > 0) sb.Append(EncChars[idx - 1]);
        }
        return sb.ToString();
    }

    string[] Arr(Func<string> read)
    {
        uint n = U32();
        var a = new string[n];
        for (int i = 0; i < n; i++) a[i] = read();
        return a;
    }

    void SkipArr(int elemSize) => Skip((int)U32() * elemSize);
    void Skip(int n) => _pos += n;
    uint U32() { var v = BitConverter.ToUInt32(data, _pos); _pos += 4; return v; }
    int I32() { var v = BitConverter.ToInt32(data, _pos); _pos += 4; return v; }
    ulong U64() { var v = BitConverter.ToUInt64(data, _pos); _pos += 8; return v; }
    float F32() { var v = BitConverter.ToSingle(data, _pos); _pos += 4; return v; }
    string Str() { int n = (int)U32(); var s = Encoding.UTF8.GetString(data, _pos, n); _pos += n; return s; }
}
