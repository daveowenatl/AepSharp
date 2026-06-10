using System.Globalization;
using System.Text;

namespace AepSharp.EngineModel;

/// <summary>
/// Parses Adobe EngineData (PostScript/PDF "COS" dictionary syntax) into an
/// <see cref="EngineValue"/> tree. Grammar:
///   value   := dict | array | string | number | name | bool
///   dict    := '&lt;&lt;' (name value)* '&gt;&gt;'
///   array   := '[' value* ']'
///   string  := '(' bytes ')'   (UTF-16BE with FE FF BOM, '\' byte-escapes)
///   name    := '/' nameChars
///   number  := [+-]?digits('.'digits)?  |  [+-]?'.'digits
///   bool    := 'true' | 'false'
/// </summary>
internal static class EngineDataParser
{
    public static EngineValue Parse(ReadOnlySpan<byte> data)
    {
        var parser = new Parser(data.ToArray());
        return parser.ParseDocument();
    }

    private sealed class Parser(byte[] data)
    {
        private int _pos;

        /// <summary>
        /// A whole EngineData blob. If it opens with a dict it is one; otherwise it's a
        /// bare key/value body (the real on-disk shape) read as an implicit top dict.
        /// </summary>
        public EngineValue ParseDocument()
        {
            SkipWhitespace();
            if (Peek() == '<' && Peek(1) == '<')
                return ParseValue();

            var dict = new EngineDict();
            while (true)
            {
                SkipWhitespace();
                if (_pos >= data.Length)
                    return dict;
                var key = ParseNameToken();
                var value = ParseValue();
                dict.Entries.Add(new KeyValuePair<string, EngineValue>(key, value));
            }
        }

        public EngineValue ParseValue()
        {
            SkipWhitespace();
            if (_pos >= data.Length)
                throw new InvalidDataException("Unexpected end of EngineData");

            var c = data[_pos];
            return c switch
            {
                (byte)'<' => ParseDict(),
                (byte)'[' => ParseArray(),
                (byte)'(' => ParseString(),
                (byte)'/' => ParseName(),
                (byte)'t' or (byte)'f' => ParseBool(),
                _ => ParseNumber(),
            };
        }

        private EngineDict ParseDict()
        {
            Expect((byte)'<');
            Expect((byte)'<');
            var dict = new EngineDict();
            while (true)
            {
                SkipWhitespace();
                if (Peek() == '>' && Peek(1) == '>')
                {
                    _pos += 2;
                    return dict;
                }
                var key = ParseNameToken();          // dictionary key
                var value = ParseValue();
                dict.Entries.Add(new KeyValuePair<string, EngineValue>(key, value));
            }
        }

        private EngineArray ParseArray()
        {
            Expect((byte)'[');
            var arr = new EngineArray();
            while (true)
            {
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _pos++;
                    return arr;
                }
                arr.Items.Add(ParseValue());
            }
        }

        private EngineString ParseString()
        {
            Expect((byte)'(');
            var raw = new List<byte>();
            while (_pos < data.Length)
            {
                var c = data[_pos];
                if (c == (byte)'\\')
                {
                    if (_pos + 1 < data.Length)
                        raw.Add(data[_pos + 1]);
                    _pos += 2;
                    continue;
                }
                if (c == (byte)')')
                {
                    _pos++;
                    break;
                }
                raw.Add(c);
                _pos++;
            }

            var bytes = raw.ToArray();
            string value;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                value = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            else
                value = Encoding.Latin1.GetString(bytes);
            return new EngineString { Value = value };
        }

        private EngineName ParseName() => new() { Value = ParseNameToken() };

        private string ParseNameToken()
        {
            Expect((byte)'/');
            var start = _pos;
            while (_pos < data.Length && !IsDelimiter(data[_pos]))
                _pos++;
            return Encoding.ASCII.GetString(data, start, _pos - start);
        }

        private EngineBoolean ParseBool()
        {
            if (Match("true")) return new EngineBoolean { Value = true };
            if (Match("false")) return new EngineBoolean { Value = false };
            throw new InvalidDataException($"Unexpected token at offset {_pos}");
        }

        private EngineNumber ParseNumber()
        {
            var start = _pos;
            while (_pos < data.Length)
            {
                var c = data[_pos];
                if (c is (byte)'+' or (byte)'-' or (byte)'.' or (>= (byte)'0' and <= (byte)'9'))
                    _pos++;
                else
                    break;
            }
            if (_pos == start)
                throw new InvalidDataException($"Unexpected token at offset {_pos}");
            var text = Encoding.ASCII.GetString(data, start, _pos - start);
            return new EngineNumber { Value = double.Parse(text, CultureInfo.InvariantCulture) };
        }

        private bool Match(string literal)
        {
            if (_pos + literal.Length > data.Length)
                return false;
            for (var i = 0; i < literal.Length; i++)
                if (data[_pos + i] != (byte)literal[i])
                    return false;
            _pos += literal.Length;
            return true;
        }

        private void Expect(byte b)
        {
            if (_pos >= data.Length || data[_pos] != b)
                throw new InvalidDataException($"Expected '{(char)b}' at offset {_pos}");
            _pos++;
        }

        private int Peek(int ahead = 0) => _pos + ahead < data.Length ? data[_pos + ahead] : -1;

        private void SkipWhitespace()
        {
            while (_pos < data.Length && data[_pos] <= 0x20)
                _pos++;
        }

        private static bool IsDelimiter(byte b) =>
            b <= 0x20 || b is (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
                or (byte)'(' or (byte)')' or (byte)'/';
    }
}
