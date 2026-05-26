namespace DorkNet.Server.Services;

public static class RoomCodeService
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public static string Normalize(string? code)
        => (code ?? string.Empty).Trim().Replace("-", string.Empty).ToUpperInvariant();

    public static string Generate(long instanceId)
    {
        var value = (ulong)HashCode.Combine(instanceId, 0x52_52_4d_43);
        Span<char> chars = stackalloc char[6];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[(int)(value % (uint)Alphabet.Length)];
            value /= (uint)Alphabet.Length;
        }
        return new string(chars);
    }
}
