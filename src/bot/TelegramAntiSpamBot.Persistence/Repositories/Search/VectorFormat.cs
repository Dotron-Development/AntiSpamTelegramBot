namespace TelegramAntiSpamBot.Persistence.Repositories;

public static class VectorFormat
{
    public const string Model = "text-embedding-3-large";
    public const string Version = "1";
    public const int Dimensions = 3072;

    internal static byte[] Encode(ReadOnlySpan<float> values)
    {
        if (!IsValid(values)) throw new ArgumentException($"Expected a finite, nonzero {Dimensions}-dimension vector.");
        var bytes = new byte[Dimensions * sizeof(float)];
        for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), values[i]);
        return bytes;
    }

    internal static float[] Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Dimensions * sizeof(float)) throw new InvalidDataException("Unexpected vector byte count.");
        var values = new float[Dimensions];
        for (var i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(i * sizeof(float))..]);
        if (!IsValid(values)) throw new InvalidDataException("Invalid vector coordinates.");
        return values;
    }

    public static bool IsValid(ReadOnlySpan<float> values)
    {
        if (values.Length != Dimensions) return false;
        var nonzero = false;
        foreach (var value in values)
        {
            if (!float.IsFinite(value)) return false;
            nonzero |= value != 0;
        }
        return nonzero;
    }
}
