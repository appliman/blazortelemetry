using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BlazorTelemetry.Host.Authentication;

public static class UserTotp
{
    public static byte[] Key(Guid secret, string identifier) => SHA256.HashData(Encoding.UTF8.GetBytes($"{secret:D}|{identifier}"));

    public static string Code(byte[] key, long _interval)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, _interval);
        var _hash = HMACSHA1.HashData(key, counter);
        var _offset = _hash[^1] & 15;
        var _value = BinaryPrimitives.ReadInt32BigEndian(_hash.AsSpan(_offset, 4)) & int.MaxValue;
        return (_value % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static long? Match(byte[] key, string? code, DateTimeOffset now)
    {
        if (code is null || code.Length != 6 || code.Any(character => character < '0' || character > '9'))
        {
            return null;
        }
        var _interval = now.ToUnixTimeSeconds() / 30;
        for (var _candidate = _interval + 1; _candidate >= _interval - 1; _candidate--)
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(key, _candidate)), Encoding.ASCII.GetBytes(code)))
            {
                return _candidate;
            }
        }
        return null;
    }

    public static string Base32(byte[] bytes)
    {
        const string ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var _result = new StringBuilder();
        var _buffer = 0;
        var _bits = 0;
        foreach (var _value in bytes)
        {
            _buffer = (_buffer << 8) | _value;
            _bits += 8;
            while (_bits >= 5)
            {
                _bits -= 5;
                _result.Append(ALPHABET[(_buffer >> _bits) & 31]);
            }
        }
        if (_bits > 0)
        {
            _result.Append(ALPHABET[(_buffer << (5 - _bits)) & 31]);
        }
        return _result.ToString();
    }
}
