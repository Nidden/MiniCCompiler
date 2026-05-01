using System;
using System.Collections.Generic;
using System.Text;

namespace CompMacro11
{
    public enum TokenType
    {
        // Литералы и идентификаторы
        IntLiteral, Identifier, StringLiteral,
        // Типы
        KwInt, KwVoid, KwBool,
        // Управление
        KwIf, KwElse, KwWhile, KwFor, KwReturn, KwBreak, KwContinue,
        KwSwitch, KwCase, KwDefault, KwDo,
        // Булевы литералы
        KwTrue, KwFalse,
        // Операторы
        Plus, Minus, Star, Slash, Percent,
        Eq, NEq, Lt, Gt, LEq, GEq,
        And, Or, Not,
        Shl, Shr,                          // << >>
        BitAnd, BitOr, BitXor, Tilde,      // & | ^ ~
        Assign, PlusAssign, MinusAssign, StarAssign, SlashAssign, PercentAssign,
        ShlAssign, ShrAssign,              // <<= >>=
        BitAndAssign, BitOrAssign, BitXorAssign, // &= |= ^=
        PlusPlus, MinusMinus,
        // Разделители
        LParen, RParen, LBrace, RBrace, LBracket, RBracket,
        Semicolon, Comma, Colon,
        // Специальные
        EOF
    }

    public class Token
    {
        public TokenType Type;
        public string Value;
        public int Line;
        public Token(TokenType t, string v, int line) { Type = t; Value = v; Line = line; }
        public override string ToString() => $"[{Type} '{Value}' L{Line}]";
    }

    public class Lexer
    {
        private string _src;
        private int _pos;
        private int _line;

        private static readonly Dictionary<string, TokenType> Keywords = new Dictionary<string, TokenType>
        {
            {"int",      TokenType.KwInt},   {"void",     TokenType.KwVoid},
            {"bool",     TokenType.KwBool},
            {"true",     TokenType.KwTrue},  {"false",    TokenType.KwFalse},
            {"if",       TokenType.KwIf},    {"else",     TokenType.KwElse},
            {"while",    TokenType.KwWhile}, {"for",      TokenType.KwFor},
            {"do",       TokenType.KwDo},
            {"switch",   TokenType.KwSwitch},{"case",     TokenType.KwCase},
            {"default",  TokenType.KwDefault},
            {"return",   TokenType.KwReturn},{"break",    TokenType.KwBreak},
            {"continue", TokenType.KwContinue}
        };

        public Lexer(string src) { _src = src; _pos = 0; _line = 1; }

        private char Peek(int offset = 0) => (_pos + offset < _src.Length) ? _src[_pos + offset] : '\0';

        private char Advance() { var c = _src[_pos++]; if (c == '\n') _line++; return c; }

        public List<Token> Tokenize()
        {
            var tokens = new List<Token>();
            while (_pos < _src.Length)
            {
                SkipWhitespaceAndComments();
                if (_pos >= _src.Length) break;

                int line = _line;
                char c = Peek();

                if (char.IsLetter(c) || c == '_')
                {
                    var sb = new StringBuilder();
                    while (_pos < _src.Length && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
                        sb.Append(Advance());
                    var word = sb.ToString();
                    tokens.Add(Keywords.TryGetValue(word, out var kw)
                        ? new Token(kw, word, line)
                        : new Token(TokenType.Identifier, word, line));
                }
                else if (char.IsDigit(c))
                {
                    var sb = new StringBuilder();
                    while (_pos < _src.Length && char.IsDigit(Peek()))
                        sb.Append(Advance());
                    tokens.Add(new Token(TokenType.IntLiteral, sb.ToString(), line));
                }
                else
                {
                    Token tok = null;
                    switch (c)
                    {
                        case '+':
                            Advance();
                            if (Peek() == '+') { Advance(); tok = new Token(TokenType.PlusPlus, "++", line); }
                            else if (Peek() == '=') { Advance(); tok = new Token(TokenType.PlusAssign, "+=", line); }
                            else tok = new Token(TokenType.Plus, "+", line);
                            break;
                        case '-':
                            Advance();
                            if (Peek() == '-') { Advance(); tok = new Token(TokenType.MinusMinus, "--", line); }
                            else if (Peek() == '=') { Advance(); tok = new Token(TokenType.MinusAssign, "-=", line); }
                            else tok = new Token(TokenType.Minus, "-", line);
                            break;
                        case '*':
                            Advance();
                            if (Peek() == '=') { Advance(); tok = new Token(TokenType.StarAssign, "*=", line); }
                            else tok = new Token(TokenType.Star, "*", line);
                            break;
                        case '/':
                            Advance();
                            if (Peek() == '=') { Advance(); tok = new Token(TokenType.SlashAssign, "/=", line); }
                            else tok = new Token(TokenType.Slash, "/", line);
                            break;
                        case '%':
                            Advance();
                            if (Peek() == '=') { Advance(); tok = new Token(TokenType.PercentAssign, "%=", line); }
                            else tok = new Token(TokenType.Percent, "%", line);
                            break;
                        case '=':
                            Advance();
                            if (Peek() == '=') { Advance(); tok = new Token(TokenType.Eq, "==", line); }
                            else tok = new Token(TokenType.Assign, "=", line);
                            break;
                        case '!':
                            Advance();
                            if (Peek() == '=') { Advance(); tok = new Token(TokenType.NEq, "!=", line); }
                            else tok = new Token(TokenType.Not, "!", line);
                            break;

                        case '&':
                            Advance();
                            if (Peek() == '&') { Advance(); tok = new Token(TokenType.And, "&&", line); }
                            else if (Peek() == '=') { Advance(); tok = new Token(TokenType.BitAndAssign, "&=", line); }
                            else tok = new Token(TokenType.BitAnd, "&", line);
                            break;
                        case '|':
                            Advance();
                            if (Peek() == '|') { Advance(); tok = new Token(TokenType.Or, "||", line); }
                            else if (Peek() == '=') { Advance(); tok = new Token(TokenType.BitOrAssign, "|=", line); }
                            else tok = new Token(TokenType.BitOr, "|", line);
                            break;
                        case '^':
                            Advance();
                            if (Peek() == '=') { Advance(); tok = new Token(TokenType.BitXorAssign, "^=", line); }
                            else tok = new Token(TokenType.BitXor, "^", line);
                            break;
                        case '~':
                            Advance();
                            tok = new Token(TokenType.Tilde, "~", line);
                            break;
                        case '<':
                            Advance();
                            if (Peek() == '<')
                            {
                                Advance();
                                if (Peek() == '=') { Advance(); tok = new Token(TokenType.ShlAssign, "<<=", line); }
                                else tok = new Token(TokenType.Shl, "<<", line);
                            }
                            else if (Peek() == '=') { Advance(); tok = new Token(TokenType.LEq, "<=", line); }
                            else tok = new Token(TokenType.Lt, "<", line);
                            break;
                        case '>':
                            Advance();
                            if (Peek() == '>')
                            {
                                Advance();
                                if (Peek() == '=') { Advance(); tok = new Token(TokenType.ShrAssign, ">>=", line); }
                                else tok = new Token(TokenType.Shr, ">>", line);
                            }
                            else if (Peek() == '=') { Advance(); tok = new Token(TokenType.GEq, ">=", line); }
                            else tok = new Token(TokenType.Gt, ">", line);
                            break;
                        case '(': Advance(); tok = new Token(TokenType.LParen, "(", line); break;
                        case ')': Advance(); tok = new Token(TokenType.RParen, ")", line); break;
                        case '{': Advance(); tok = new Token(TokenType.LBrace, "{", line); break;
                        case '}': Advance(); tok = new Token(TokenType.RBrace, "}", line); break;
                        case '[': Advance(); tok = new Token(TokenType.LBracket, "[", line); break;
                        case ']': Advance(); tok = new Token(TokenType.RBracket, "]", line); break;
                        case ':': Advance(); tok = new Token(TokenType.Colon, ":", line); break;
                        case ';': Advance(); tok = new Token(TokenType.Semicolon, ";", line); break;
                        case ',': Advance(); tok = new Token(TokenType.Comma, ",", line); break;
                        case '"':
                            {
                                Advance(); // пропустить открывающую "
                                var sb2 = new System.Text.StringBuilder();
                                while (_pos < _src.Length && Peek() != '"' && Peek() != '\n')
                                {
                                    if (Peek() == '\\') { Advance(); sb2.Append(EscapeChar(Peek())); }
                                    else sb2.Append(Peek());
                                    Advance();
                                }
                                if (Peek() == '"') Advance(); // закрывающая "
                                tok = new Token(TokenType.StringLiteral, sb2.ToString(), line);
                                break;
                            }
                        default:
                            throw new Exception($"Строка {line}: неожиданный символ '{c}'");
                    }
                    if (tok != null) tokens.Add(tok);
                }
            }
            tokens.Add(new Token(TokenType.EOF, "", _line));
            return tokens;
        }

        private char EscapeChar(char c)
        {
            switch (c)
            {
                case 'n': return '\n';
                case 'r': return '\r';
                case 't': return '\t';
                case '0': return '\0';
                case '"': return '"';
                case '\\': return '\\';
                default: return c;
            }
        }

        private void SkipWhitespaceAndComments()
        {
            while (_pos < _src.Length)
            {
                if (char.IsWhiteSpace(Peek())) { Advance(); continue; }
                if (Peek() == '/' && Peek(1) == '/')
                {
                    while (_pos < _src.Length && Peek() != '\n') Advance();
                    continue;
                }
                if (Peek() == '/' && Peek(1) == '*')
                {
                    Advance(); Advance();
                    while (_pos < _src.Length - 1 && !(Peek() == '*' && Peek(1) == '/'))
                        Advance();
                    if (_pos < _src.Length - 1) { Advance(); Advance(); }
                    continue;
                }
                break;
            }
        }
    }
}
