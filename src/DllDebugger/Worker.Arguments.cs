using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MyDebugTools.DllDebugger;

internal partial class Worker
{
    readonly Dictionary<string, Type> structures = [];

    Type ValueType(Signature s, string name)
    {
        var layout = s.Layouts.FirstOrDefault(l => l.Name == name);
        if (layout == null) return Scalar(name);
        string key = SignatureText.FormatLayout(layout);
        if (structures.TryGetValue(key, out var type)) return type;
        var builder = module.DefineType("Record" + structures.Count,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
            typeof(ValueType), (PackingSize)layout.Pack);
        foreach (var field in layout.Fields)
            builder.DefineField(field.Name, Scalar(field.Type), FieldAttributes.Public);
        return structures[key] = builder.CreateType()!;
    }

    object ReadValue(Signature s, string type, JsonElement value)
    {
        var layout = s.Layouts.FirstOrDefault(l => l.Name == type);
        if (layout == null) return Input(type, value);
        var record = Activator.CreateInstance(ValueType(s, type))!;
        foreach (var f in layout.Fields)
        {
            if (!value.TryGetProperty(f.Name, out var field)) throw new InvalidDataException(type + " 缺少字段 " + f.Name);
            record.GetType().GetField(f.Name)!.SetValue(record, Input(f.Type, field));
        }
        return record;
    }

    JsonElement WriteValue(Signature s, string type, object? value)
    {
        var layout = s.Layouts.FirstOrDefault(l => l.Name == type);
        if (layout == null) return Output(type, value);
        return JsonSerializer.SerializeToElement(layout.Fields.ToDictionary(f => f.Name,
            f => Output(f.Type, value!.GetType().GetField(f.Name)!.GetValue(value))));
    }

    static byte[] Bytes(Parameter p, JsonElement value)
    {
        if (p.Type == "string")
        {
            var encoding = p.Encoding switch { "utf16" => Encoding.Unicode, "utf8" => Encoding.UTF8, _ => Encoding.ASCII };
            return encoding.GetBytes((value.GetString() ?? throw new InvalidDataException(p.Name + " 需要字符串。")) + "\0");
        }
        var buffer = new byte[p.Length!.Value];
        if (p.Direction != "out")
        {
            var bytes = value.EnumerateArray().Select(e => e.GetByte()).ToArray();
            if (bytes.Length > buffer.Length) throw new InvalidDataException(p.Name + "：输入超过缓冲区长度。");
            bytes.CopyTo(buffer, 0);
        }
        return buffer;
    }

    CallResult Call(CallRequest request)
    {
        var s = request.Signature;
        s.Validate();
        var parameters = s.Parameters!;
        var types = new Type[parameters.Length];
        var args = new object?[parameters.Length];
        var pins = new List<GCHandle>();
        var buffers = new Dictionary<string, byte[]>();
        try
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (!request.Arguments.TryGetValue(p.Name, out var input) && p.Direction != "out")
                    throw new InvalidDataException("缺少参数：" + p.Name);
                try
                {
                    if (p.Type is "string" or "buffer")
                    {
                        var bytes = Bytes(p, input);
                        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                        pins.Add(pin);
                        buffers[p.Name] = bytes;
                        types[i] = typeof(nint);
                        args[i] = pin.AddrOfPinnedObject();
                    }
                    else
                    {
                        string name = p.Type.TrimEnd('*');
                        var type = ValueType(s, name);
                        types[i] = p.Type.EndsWith('*') ? type.MakeByRefType() : type;
                        args[i] = p.Direction == "out" ? Activator.CreateInstance(type) : ReadValue(s, name, input);
                    }
                }
                catch (Exception e) { throw new InvalidDataException(p.Name + "：" + e.Message); }
            }
            var method = Method(s, ValueType(s, s.Result), types);
            var returned = method.Invoke(null, args);
            var outputs = new Dictionary<string, JsonElement>();
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.Direction == "in") continue;
                outputs[p.Name] = p.Type == "buffer"
                    ? JsonSerializer.SerializeToElement(buffers[p.Name].Select(b => (int)b))
                    : WriteValue(s, p.Type.TrimEnd('*'), args[i]);
            }
            return new(WriteValue(s, s.Result, returned), outputs);
        }
        finally { foreach (var pin in pins) pin.Free(); }
    }
}
