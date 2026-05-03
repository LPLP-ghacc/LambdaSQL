namespace LambdaSQL.Core.Lexer;

public sealed class Lexer
{
    private readonly string _input;
    private int _pos;

    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["select"]   = TokenType.Select,
        ["from"]     = TokenType.From,
        ["where"]    = TokenType.Where,
        ["insert"]   = TokenType.Insert,
        ["into"]     = TokenType.Into,
        ["values"]   = TokenType.Values,
        ["update"]   = TokenType.Update,
        ["set"]      = TokenType.Set,
        ["delete"]   = TokenType.Delete,
        ["create"]   = TokenType.Create,
        ["drop"]     = TokenType.Drop,
        ["table"]    = TokenType.Table,
        ["order"]    = TokenType.Order,
        ["by"]       = TokenType.By,
        ["asc"]      = TokenType.Asc,
        ["desc"]     = TokenType.Desc,
        ["limit"]    = TokenType.Limit,
        ["group"]    = TokenType.Group,
        ["join"]     = TokenType.Join,
        ["inner"]    = TokenType.Inner,
        ["left"]     = TokenType.Left,
        ["on"]       = TokenType.On,
        ["and"]      = TokenType.And,
        ["or"]       = TokenType.Or,
        ["not"]      = TokenType.Not,
        ["in"]       = TokenType.In,
        ["like"]     = TokenType.Like,
        ["is"]       = TokenType.Is,
        ["as"]       = TokenType.As,
        ["distinct"] = TokenType.Distinct,
        ["having"]   = TokenType.Having,
        ["true"]     = TokenType.Bool,
        ["false"]    = TokenType.Bool,
        ["null"]     = TokenType.Null,
        ["int"]      = TokenType.TypeInt,
        ["bigint"]   = TokenType.TypeBigInt,
        ["float"]    = TokenType.TypeFloat,
        ["text"]     = TokenType.TypeText,
        ["bool"]     = TokenType.TypeBool,
    };

    public Lexer(string input)
    {
        _input = input;
        _pos = 0;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (_pos < _input.Length)
        {
            SkipWhitespaceAndComments();
            if (_pos >= _input.Length) break;

            var start = _pos;
            var ch = _input[_pos];

            if (char.IsLetter(ch) || ch == '_')
            {
                tokens.Add(ReadIdentifierOrKeyword(start));
            }
            else if (char.IsDigit(ch))
            {
                tokens.Add(ReadNumber(start));
            }
            else if (ch == '\'' || ch == '"')
            {
                tokens.Add(ReadString(start));
            }
            else
            {
                var tok = ReadSymbol(start);
                if (tok != null) tokens.Add(tok);
            }
        }

        tokens.Add(new Token(TokenType.Eof, "", _pos));
        return tokens;
    }

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _input.Length)
        {
            if (char.IsWhiteSpace(_input[_pos]))
            {
                _pos++;
            }
            else if (_pos + 1 < _input.Length && _input[_pos] == '-' && _input[_pos + 1] == '-')
            {
                // single-line comment
                while (_pos < _input.Length && _input[_pos] != '\n') _pos++;
            }
            else
            {
                break;
            }
        }
    }

    private Token ReadIdentifierOrKeyword(int start)
    {
        while (_pos < _input.Length && (char.IsLetterOrDigit(_input[_pos]) || _input[_pos] == '_'))
            _pos++;

        var word = _input[start.._pos];

        if (Keywords.TryGetValue(word, out var kwType))
            return new Token(kwType, word.ToLowerInvariant(), start);

        return new Token(TokenType.Identifier, word, start);
    }

    private Token ReadNumber(int start)
    {
        while (_pos < _input.Length && char.IsDigit(_input[_pos])) _pos++;

        if (_pos < _input.Length && _input[_pos] == '.' &&
            _pos + 1 < _input.Length && char.IsDigit(_input[_pos + 1]))
        {
            _pos++; // consume dot
            while (_pos < _input.Length && char.IsDigit(_input[_pos])) _pos++;
            return new Token(TokenType.Float, _input[start.._pos], start);
        }

        return new Token(TokenType.Integer, _input[start.._pos], start);
    }

    private Token ReadString(int start)
    {
        var quote = _input[_pos++];
        var sb = new System.Text.StringBuilder();

        while (_pos < _input.Length && _input[_pos] != quote)
        {
            if (_input[_pos] == '\\' && _pos + 1 < _input.Length)
            {
                _pos++;
                sb.Append(_input[_pos] switch
                {
                    'n'  => '\n',
                    't'  => '\t',
                    '\'' => '\'',
                    '"'  => '"',
                    '\\' => '\\',
                    var c => c
                });
            }
            else
            {
                sb.Append(_input[_pos]);
            }
            _pos++;
        }

        if (_pos < _input.Length) _pos++; // closing quote
        return new Token(TokenType.String, sb.ToString(), start);
    }

    private Token? ReadSymbol(int start)
    {
        var ch = _input[_pos++];

        return ch switch
        {
            '=' => new Token(TokenType.Equals, "=", start),
            '+' => new Token(TokenType.Plus, "+", start),
            '-' => new Token(TokenType.Minus, "-", start),
            '*' => new Token(TokenType.Star, "*", start),
            '/' => new Token(TokenType.Slash, "/", start),
            '%' => new Token(TokenType.Percent, "%", start),
            '(' => new Token(TokenType.LeftParen, "(", start),
            ')' => new Token(TokenType.RightParen, ")", start),
            ',' => new Token(TokenType.Comma, ",", start),
            ';' => new Token(TokenType.Semicolon, ";", start),
            '.' => new Token(TokenType.Dot, ".", start),
            '!' when Peek() == '=' => Advance(new Token(TokenType.NotEquals, "!=", start)),
            '<' when Peek() == '=' => Advance(new Token(TokenType.LessOrEqual, "<=", start)),
            '<' when Peek() == '>' => Advance(new Token(TokenType.NotEquals, "<>", start)),
            '<' => new Token(TokenType.Less, "<", start),
            '>' when Peek() == '=' => Advance(new Token(TokenType.GreaterOrEqual, ">=", start)),
            '>' => new Token(TokenType.Greater, ">", start),
            _ => throw new LexerException($"Unexpected character '{ch}' at position {start}")
        };
    }

    private char Peek() => _pos < _input.Length ? _input[_pos] : '\0';

    private Token Advance(Token t) { _pos++; return t; }
}

public sealed class LexerException(string message) : Exception(message);
