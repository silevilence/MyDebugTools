using System.Buffers.Binary;

namespace MyDebugTools.DllDebugger;

// MSF 7 directory maps each logical stream onto non-contiguous file blocks.
internal sealed class PdbStreams
{
    readonly byte[] bytes;
    readonly int blockSize;
    readonly (int Size, int[] Blocks)[] streams;
    internal static int I32(byte[] data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
    internal static int U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

    public PdbStreams(byte[] bytes)
    {
        this.bytes = bytes;
        if (!bytes.AsSpan().StartsWith("Microsoft C/C++ MSF 7.00\r\n"u8)) throw new InvalidDataException("需要 MSF 7 原生 PDB。");
        blockSize = I32(bytes, 32);
        if (blockSize < 512 || blockSize > 65536 || (blockSize & (blockSize - 1)) != 0) throw new InvalidDataException("PDB 块大小无效。");
        int directorySize = I32(bytes, 44);
        int map = checked(I32(bytes, 52) * blockSize);
        var dir = Copy(directorySize, Blocks(bytes, ref map, directorySize));
        int count = I32(dir, 0);
        int pos = checked(4 + count * 4);
        if (pos > dir.Length) throw new InvalidDataException("PDB 流目录不完整。");
        streams = new (int, int[])[count];
        for (int i = 0; i < count; i++)
        {
            int size = I32(dir, 4 + i * 4);
            streams[i] = (size, size == -1 ? [] : Blocks(dir, ref pos, size));
        }
    }

    int[] Blocks(byte[] data, ref int pos, int size)
    {
        if (size < 0 || size > bytes.Length) throw new InvalidDataException("PDB 流长度无效。");
        int count = (int)(((long)size + blockSize - 1) / blockSize);
        if ((long)pos + 4L * count > data.Length) throw new InvalidDataException("PDB 块索引不完整。");
        var blocks = new int[count];
        for (int i = 0; i < count; i++, pos += 4) blocks[i] = I32(data, pos);
        return blocks;
    }

    byte[] Copy(int size, int[] blocks)
    {
        byte[] output = new byte[size];
        for (int i = 0; i < blocks.Length; i++)
            Array.Copy(bytes, checked(blocks[i] * blockSize), output, i * blockSize, Math.Min(blockSize, size - i * blockSize));
        return output;
    }

    public byte[] Stream(int index) => index < streams.Length && streams[index].Size > 0
        ? Copy(streams[index].Size, streams[index].Blocks) : [];
}
