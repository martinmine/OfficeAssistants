using System.Buffers.Binary;
using System.Text;

namespace AcsFormat;

/// <summary>
/// Little-endian cursor over an in-memory ACS file. All multi-byte values in the
/// format are little-endian; strings are a ULONG character count followed by UTF-16
/// characters and a null terminator (the terminator is omitted for empty strings).
/// </summary>
internal ref struct AcsBinaryReader
{
    private readonly ReadOnlySpan<byte> _data;

    public AcsBinaryReader(ReadOnlySpan<byte> data, int position = 0)
    {
        _data = data;
        Position = position;
    }

    public int Position { get; set; }

    public byte ReadByte() => _data[Position++];

    public bool ReadBool() => ReadByte() != 0;

    public ushort ReadUInt16()
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_data[Position..]);
        Position += 2;
        return value;
    }

    public short ReadInt16()
    {
        var value = BinaryPrimitives.ReadInt16LittleEndian(_data[Position..]);
        Position += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data[Position..]);
        Position += 4;
        return value;
    }

    public int ReadInt32()
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(_data[Position..]);
        Position += 4;
        return value;
    }

    public Guid ReadGuid()
    {
        var value = new Guid(_data.Slice(Position, 16));
        Position += 16;
        return value;
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        var value = _data.Slice(Position, count);
        Position += count;
        return value;
    }

    public AcsLocator ReadLocator() => new(ReadUInt32(), ReadUInt32());

    public string ReadString()
    {
        var length = checked((int)ReadUInt32());
        if (length == 0)
        {
            return string.Empty;
        }

        var value = Encoding.Unicode.GetString(_data.Slice(Position, length * 2));
        Position += (length + 1) * 2;
        return value;
    }

    /// <summary>Reads a Windows COLORREF (0x00BBGGRR) and returns it as opaque 0xAARRGGBB.</summary>
    public uint ReadColorRef()
    {
        var r = ReadByte();
        var g = ReadByte();
        var b = ReadByte();
        ReadByte();
        return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
    }
}

/// <summary>Offset/size pair pointing at a block elsewhere in the file.</summary>
internal readonly record struct AcsLocator(uint Offset, uint Size);
