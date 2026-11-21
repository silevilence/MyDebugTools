using System.Globalization;
using System.IO.Pipes;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyDebugTools.DllDebugger;

internal class Worker(string path)
{
    readonly Dictionary<string, nint> handles = [];
    readonly Dictionary<string, MethodInfo> methods = [];
    readonly ModuleBuilder module = AssemblyBuilder.DefineDynamicAssembly(new("DllCalls"), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("DllCalls");

    internal static Type Scalar(string type) => type switch
    {
        "i8" => typeof(sbyte), "u8" => typeof(byte), "i16" => typeof(short), "u16" => typeof(ushort),
        "i32" => typeof(int), "u32" => typeof(uint), "i64" => typeof(long), "u64" => typeof(ulong),
        "f32" => typeof(float), "f64" => typeof(double), "void" => typeof(void),
        "isize" or "handle" => typeof(nint), "usize" => typeof(nuint),
        _ => throw new InvalidDataException("不支持的标量类型：" + type)
    };

    object Input(string type, JsonElement value)
    {
        if (type == "handle")
        {
            if (value.ValueKind == JsonValueKind.Null) return (nint)0;
            if (value.ValueKind != JsonValueKind.String || !handles.TryGetValue(value.GetString()!, out var handle))
                throw new InvalidDataException("句柄不属于当前会话。");
            return handle;
        }
        if (type == "isize") return checked((nint)value.GetInt64());
        if (type == "usize") return checked((nuint)value.GetUInt64());
        return value.Deserialize(Scalar(type)) ?? throw new InvalidDataException("缺少输入值。");
    }

    JsonElement Output(string type, object? value)
    {
        if (type == "handle" && (nint)value! != 0)
        {
            var key = "handle-" + Guid.NewGuid().ToString("N");
            handles[key] = (nint)value!;
            return JsonSerializer.SerializeToElement(key);
        }
        object? result = type switch { "handle" or "void" => null, "isize" => (long)(nint)value!,
            "usize" => (ulong)(nuint)value!, _ => value };
        return JsonSerializer.SerializeToElement(result);
    }

    MethodInfo Method(Signature signature, Type result, Type[] arguments)
    {
        string key = JsonSerializer.Serialize(new { signature.Entry, signature.Convention, Result = result.Name, Args = arguments.Select(t => t.Name) });
        if (methods.TryGetValue(key, out var cached)) return cached;
        var holder = module.DefineType("DllCall" + methods.Count, TypeAttributes.Public);
        var method = holder.DefinePInvokeMethod("Invoke", path, signature.Entry, MethodAttributes.Static | MethodAttributes.Public
            | MethodAttributes.PinvokeImpl, CallingConventions.Standard, result, arguments,
            signature.Convention == "cdecl" ? CallingConvention.Cdecl : CallingConvention.StdCall, CharSet.None);
        method.SetImplementationFlags(method.GetMethodImplementationFlags() | MethodImplAttributes.PreserveSig);
        return methods[key] = holder.CreateType()!.GetMethod("Invoke")!;
    }

    CallResult Call(CallRequest request)
    {
        var s = request.Signature;
        s.Validate();
        var parameters = s.Parameters!;
        var method = Method(s, Scalar(s.Result), parameters.Select(p => Scalar(p.Type)).ToArray());
        var args = parameters.Select(p => request.Arguments.TryGetValue(p.Name, out var v) ? Input(p.Type, v) :
            throw new InvalidDataException("缺少参数：" + p.Name)).ToArray();
        return new(Output(s.Result, method.Invoke(null, args)), []);
    }

    static int Main(string[] args)
    {
        if (args.Length != 3) return 2;
        using var pipe = new NamedPipeClientStream(".", args[0], PipeDirection.InOut);
        pipe.Connect(5000);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        // Keep this exact file locked against replacement for the lifetime of the loaded module.
        using var file = File.OpenRead(args[1]);
        var worker = new Worker(args[1]);
        bool matches = Convert.ToHexString(SHA256.HashData(file)) == args[2];
        nint library = 0;
        while (reader.ReadLine() is {} line)
        {
            Reply reply;
            try
            {
                if (!matches) throw new InvalidDataException("DLL 已换版，请重新打开并确认签名。");
                if (library == 0) library = NativeLibrary.Load(args[1]);
                reply = new(worker.Call(JsonSerializer.Deserialize<CallRequest>(line)!), null);
            }
            catch (Exception error) { reply = new(null, error.GetBaseException().Message); }
            writer.WriteLine(JsonSerializer.Serialize(reply));
        }
        return 0;
    }
}
