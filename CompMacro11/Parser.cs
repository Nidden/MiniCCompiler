using System;
using System.Collections.Generic;
using System.Linq;

namespace CompMacro11
{
    public class Parser
    {
        private List<Token> _tokens;
        private int _pos;

        private Token Cur => _tokens[_pos];
        private Token Peek(int offset = 0) => _tokens[Math.Min(_pos + offset, _tokens.Count - 1)];

        private Token Consume()
        {
            var t = _tokens[_pos];
            if (t.Type != TokenType.EOF) _pos++;
            return t;
        }

        private Token Expect(TokenType type)
        {
            if (Cur.Type != type)
                throw new Exception($"Строка {Cur.Line}: ожидалось {type}, найдено '{Cur.Value}'");
            return Consume();
        }

        private bool Check(TokenType type) => Cur.Type == type;
        private bool Match(TokenType type) { if (Check(type)) { Consume(); return true; } return false; }

        public Parser(List<Token> tokens) { _tokens = tokens; _pos = 0; }

        // ── Программа ──────────────────────────────────────────
        public ProgramNode ParseProgram()
        {
            var prog = new ProgramNode();
            while (!Check(TokenType.EOF))
            {
                // тип ident '[' → глобальный массив
                // тип ident '=' или ';' → глобальная переменная
                // тип ident '(' → функция
                if ((Check(TokenType.KwInt) || Check(TokenType.KwBool)) && PeekIsGlobal())
                    prog.Globals.Add(ParseGlobalVar());
                else
                    prog.Functions.Add(ParseFuncDecl());
            }
            return prog;
        }

        // Проверить lookahead: тип ident затем не '(' → глобальная переменная/массив
        private bool PeekIsGlobal()
        {
            int i = _pos;
            if (i < _tokens.Count &&
                (_tokens[i].Type == TokenType.KwInt || _tokens[i].Type == TokenType.KwBool))
                i++;
            if (i < _tokens.Count && _tokens[i].Type == TokenType.Identifier)
                i++;
            if (i >= _tokens.Count) return false;
            var t = _tokens[i].Type;
            // '[' = массив, '=' = переменная с инициализатором, ';' = переменная без, ',' = несколько
            return t == TokenType.LBracket || t == TokenType.Assign ||
                   t == TokenType.Semicolon || t == TokenType.Comma;
        }

        private VarDeclStmtNode ParseGlobalVar()
        {
            int line = Cur.Line;
            var t = ParseBaseType();
            var name = Expect(TokenType.Identifier).Value;
            ParseArrayDims(t, firstCanBeEmpty: false);
            ExprNode init = null;
            ArrayInitNode arrInit = null;
            if (Match(TokenType.Assign))
            {
                if (t.IsArray) arrInit = ParseArrayInit(t);
                else init = ParseExpr();
            }
            Expect(TokenType.Semicolon);
            return new VarDeclStmtNode { Name = name, Type = t, Init = init, ArrayInit = arrInit, Line = line };
        }

        // ── Тип (int, bool или void) с размерами массива ──────────
        private MiniCType ParseBaseType()
        {
            var t = new MiniCType();
            if (Check(TokenType.KwVoid)) { Consume(); t.IsVoid = true; }
            else if (Check(TokenType.KwBool)) { Consume(); t.IsBool = true; }
            else Expect(TokenType.KwInt);
            return t;
        }

        private void ParseArrayDims(MiniCType t, bool firstCanBeEmpty)
        {
            while (Check(TokenType.LBracket))
            {
                Consume(); // '['
                if (Check(TokenType.RBracket))
                {
                    if (!firstCanBeEmpty)
                        throw new Exception($"Строка {Cur.Line}: пустой размер массива допустим только в параметре");
                    t.Dims.Add(-1);
                    firstCanBeEmpty = false;
                }
                else
                {
                    var sz = ParseConstExpr();
                    t.Dims.Add(sz);
                    firstCanBeEmpty = false;
                }
                Expect(TokenType.RBracket);
                t.IsArray = true;
            }
        }

        private int ParseConstExpr()
        {
            bool neg = false;
            if (Check(TokenType.Minus)) { Consume(); neg = true; }
            var tok = Expect(TokenType.IntLiteral);
            int v = int.Parse(tok.Value);
            return neg ? -v : v;
        }

        // ── Функция ─────────────────────────────────────────────
        private FuncDeclNode ParseFuncDecl()
        {
            int line = Cur.Line;
            var retType = ParseBaseType();
            var name = Expect(TokenType.Identifier).Value;
            Expect(TokenType.LParen);

            var parms = new List<ParamNode>();
            if (!Check(TokenType.RParen))
            {
                // void-параметр: int main(void)
                if (Check(TokenType.KwVoid)) { Consume(); }
                else
                {
                    do { parms.Add(ParseParam()); }
                    while (Match(TokenType.Comma));
                }
            }
            Expect(TokenType.RParen);

            var body = ParseBlock();
            return new FuncDeclNode { Name = name, ReturnType = retType, Params = parms, Body = body, Line = line };
        }

        private ParamNode ParseParam()
        {
            var t = ParseBaseType();
            var name = Expect(TokenType.Identifier).Value;
            ParseArrayDims(t, firstCanBeEmpty: true);
            return new ParamNode { Name = name, Type = t };
        }

        // ── Тело оператора: блок {} или одиночный оператор ──────
        private StmtNode ParseBody()
        {
            if (Check(TokenType.LBrace)) return ParseBlock();
            // Одиночный оператор — оборачиваем в блок
            int line = Cur.Line;
            var block = new BlockStmtNode { Line = line };
            block.Stmts.Add(ParseStmt());
            return block;
        }

        // ── Блок ────────────────────────────────────────────────
        private BlockStmtNode ParseBlock()
        {
            int line = Cur.Line;
            Expect(TokenType.LBrace);
            var block = new BlockStmtNode { Line = line };
            while (!Check(TokenType.RBrace) && !Check(TokenType.EOF))
                block.Stmts.Add(ParseStmt());
            Expect(TokenType.RBrace);
            return block;
        }

        // ── Оператор ────────────────────────────────────────────
        private StmtNode ParseStmt()
        {
            int line = Cur.Line;
            if (Check(TokenType.LBrace)) return ParseBlock();
            if (Check(TokenType.KwIf)) return ParseIf();
            if (Check(TokenType.KwWhile)) return ParseWhile();
            if (Check(TokenType.KwFor)) return ParseFor();
            if (Check(TokenType.KwReturn)) return ParseReturn();
            if (Check(TokenType.KwBreak)) { Consume(); Expect(TokenType.Semicolon); return new BreakStmtNode { Line = line }; }
            if (Check(TokenType.KwContinue)) { Consume(); Expect(TokenType.Semicolon); return new ContinueStmtNode { Line = line }; }
            if (Check(TokenType.KwSwitch)) return ParseSwitch();
            if (Check(TokenType.KwDo)) return ParseDoWhile();
            if (Check(TokenType.KwInt) || Check(TokenType.KwVoid) || Check(TokenType.KwBool))
                return ParseVarDecl();

            var expr = ParseExpr();
            Expect(TokenType.Semicolon);
            return new ExprStmtNode { Expr = expr, Line = line };
        }

        private IfStmtNode ParseIf()
        {
            int line = Cur.Line;
            Expect(TokenType.KwIf);
            Expect(TokenType.LParen);
            var cond = ParseExpr();
            Expect(TokenType.RParen);
            var then = ParseBody();
            StmtNode els = null;
            if (Match(TokenType.KwElse))
            {
                if (Check(TokenType.KwIf)) els = ParseIf();
                else els = ParseBody();
            }
            return new IfStmtNode { Cond = cond, Then = then, Else = els, Line = line };
        }

        private WhileStmtNode ParseWhile()
        {
            int line = Cur.Line;
            Expect(TokenType.KwWhile);
            Expect(TokenType.LParen);
            var cond = ParseExpr();
            Expect(TokenType.RParen);
            var body = ParseBody();
            return new WhileStmtNode { Cond = cond, Body = body, Line = line };
        }

        private ForStmtNode ParseFor()
        {
            int line = Cur.Line;
            Expect(TokenType.KwFor);
            Expect(TokenType.LParen);
            StmtNode init = null;
            if (!Check(TokenType.Semicolon))
            {
                if (Check(TokenType.KwInt)) init = ParseVarDecl();
                else { var e = ParseExpr(); Expect(TokenType.Semicolon); init = new ExprStmtNode { Expr = e }; }
            }
            else Expect(TokenType.Semicolon);
            ExprNode cond = null;
            if (!Check(TokenType.Semicolon)) cond = ParseExpr();
            Expect(TokenType.Semicolon);
            ExprNode post = null;
            if (!Check(TokenType.RParen)) post = ParseExpr();
            Expect(TokenType.RParen);
            var body = ParseBody();
            return new ForStmtNode { Init = init, Cond = cond, Post = post, Body = body, Line = line };
        }

        private ReturnStmtNode ParseReturn()
        {
            int line = Cur.Line;
            Expect(TokenType.KwReturn);
            ExprNode val = null;
            if (!Check(TokenType.Semicolon)) val = ParseExpr();
            Expect(TokenType.Semicolon);
            return new ReturnStmtNode { Value = val, Line = line };
        }

        private StmtNode ParseVarDecl()
        {
            int line = Cur.Line;
            var t = ParseBaseType();
            var decls = new List<VarDeclStmtNode>();

            do
            {
                // Для каждой переменной в списке — копируем базовый тип
                var vt = new MiniCType { IsVoid = t.IsVoid, IsBool = t.IsBool };
                var name = Expect(TokenType.Identifier).Value;
                ParseArrayDims(vt, firstCanBeEmpty: false);
                ExprNode init = null;
                ArrayInitNode arrInit = null;
                if (Match(TokenType.Assign))
                {
                    if (vt.IsArray)
                        arrInit = ParseArrayInit(vt);
                    else
                        init = ParseExpr();
                }
                decls.Add(new VarDeclStmtNode { Name = name, Type = vt, Init = init, ArrayInit = arrInit, Line = line });
            }
            while (Match(TokenType.Comma));

            Expect(TokenType.Semicolon);

            if (decls.Count == 1) return decls[0];
            return new BlockStmtNode { Stmts = decls.Cast<StmtNode>().ToList(), Line = line };
        }

        private ArrayInitNode ParseArrayInit(MiniCType t)
        {
            var node = new ArrayInitNode();
            node.Flat = ParseFlatInits();
            // Дополнить нулями до полного размера
            int total = t.TotalElements();
            while (node.Flat.Count < total) node.Flat.Add(0);
            return node;
        }

        private List<int> ParseFlatInits()
        {
            var result = new List<int>();
            if (Check(TokenType.LBrace))
            {
                Expect(TokenType.LBrace);
                if (!Check(TokenType.RBrace))
                {
                    do
                    {
                        if (Check(TokenType.LBrace))
                            result.AddRange(ParseFlatInits());
                        else
                        {
                            bool neg = Match(TokenType.Minus);
                            var tok = Expect(TokenType.IntLiteral);
                            int v = int.Parse(tok.Value);
                            result.Add(neg ? -v : v);
                        }
                    } while (Match(TokenType.Comma));
                }
                Expect(TokenType.RBrace);
            }
            return result;
        }

        // ── Выражения (рекурсивный спуск) ───────────────────────
        private SwitchStmtNode ParseSwitch()
        {
            int line = Cur.Line;
            Expect(TokenType.KwSwitch);
            Expect(TokenType.LParen);
            var expr = ParseExpr();
            Expect(TokenType.RParen);
            Expect(TokenType.LBrace);
            var node = new SwitchStmtNode { Expr = expr, Line = line };
            while (!Check(TokenType.RBrace) && !Check(TokenType.EOF))
            {
                int? val = null;
                if (Check(TokenType.KwCase))
                {
                    Consume();
                    var tok = Expect(TokenType.IntLiteral);
                    val = int.Parse(tok.Value);
                    Expect(TokenType.Colon);
                }
                else if (Check(TokenType.KwDefault))
                {
                    Consume();
                    Expect(TokenType.Colon);
                    // val остаётся null
                }
                else break;
                var sc = new SwitchCase { Value = val };
                while (!Check(TokenType.KwCase) && !Check(TokenType.KwDefault)
                       && !Check(TokenType.RBrace) && !Check(TokenType.EOF))
                    sc.Body.Add(ParseStmt());
                node.Cases.Add(sc);
            }
            Expect(TokenType.RBrace);
            return node;
        }

        private DoWhileStmtNode ParseDoWhile()
        {
            int line = Cur.Line;
            Expect(TokenType.KwDo);
            var body = ParseBody();
            Expect(TokenType.KwWhile);
            Expect(TokenType.LParen);
            var cond = ParseExpr();
            Expect(TokenType.RParen);
            Expect(TokenType.Semicolon);
            return new DoWhileStmtNode { Body = body, Cond = cond, Line = line };
        }

        private ExprNode ParseExpr() => ParseAssign();

        private ExprNode ParseAssign()
        {
            var left = ParseOr();
            int line = Cur.Line;
            string op = null;
            if (Check(TokenType.Assign)) op = "=";
            else if (Check(TokenType.PlusAssign)) op = "+=";
            else if (Check(TokenType.MinusAssign)) op = "-=";
            else if (Check(TokenType.StarAssign)) op = "*=";
            else if (Check(TokenType.SlashAssign)) op = "/=";
            else if (Check(TokenType.PercentAssign)) op = "%=";
            else if (Check(TokenType.ShlAssign)) op = "<<=";
            else if (Check(TokenType.ShrAssign)) op = ">>=";
            else if (Check(TokenType.BitAndAssign)) op = "&=";
            else if (Check(TokenType.BitOrAssign)) op = "|=";
            else if (Check(TokenType.BitXorAssign)) op = "^=";
            if (op != null)
            {
                Consume();
                var right = ParseAssign();
                return new AssignExpr { Op = op, Target = left, Value = right, Line = line };
            }
            return left;
        }

        private ExprNode ParseOr()
        {
            var left = ParseAnd();
            while (Check(TokenType.Or))
            {
                int line = Cur.Line; Consume();
                left = new BinaryExpr { Op = "||", Left = left, Right = ParseAnd(), Line = line };
            }
            return left;
        }

        private ExprNode ParseAnd()
        {
            var left = ParseBitOr();
            while (Check(TokenType.And))
            {
                int line = Cur.Line; Consume();
                left = new BinaryExpr { Op = "&&", Left = left, Right = ParseBitOr(), Line = line };
            }
            return left;
        }

        private ExprNode ParseBitOr()
        {
            var left = ParseBitXor();
            while (Check(TokenType.BitOr))
            {
                int line = Cur.Line; Consume();
                left = new BinaryExpr { Op = "|", Left = left, Right = ParseBitXor(), Line = line };
            }
            return left;
        }

        private ExprNode ParseBitXor()
        {
            var left = ParseBitAnd();
            while (Check(TokenType.BitXor))
            {
                int line = Cur.Line; Consume();
                left = new BinaryExpr { Op = "^", Left = left, Right = ParseBitAnd(), Line = line };
            }
            return left;
        }

        private ExprNode ParseBitAnd()
        {
            var left = ParseEq();
            while (Check(TokenType.BitAnd))
            {
                int line = Cur.Line; Consume();
                left = new BinaryExpr { Op = "&", Left = left, Right = ParseEq(), Line = line };
            }
            return left;
        }

        private ExprNode ParseEq()
        {
            var left = ParseRel();
            while (Check(TokenType.Eq) || Check(TokenType.NEq))
            {
                int line = Cur.Line;
                var op = Cur.Value; Consume();
                left = new BinaryExpr { Op = op, Left = left, Right = ParseRel(), Line = line };
            }
            return left;
        }

        private ExprNode ParseRel()
        {
            var left = ParseShift();
            while (Check(TokenType.Lt) || Check(TokenType.Gt) || Check(TokenType.LEq) || Check(TokenType.GEq))
            {
                int line = Cur.Line;
                var op = Cur.Value; Consume();
                left = new BinaryExpr { Op = op, Left = left, Right = ParseShift(), Line = line };
            }
            return left;
        }

        private ExprNode ParseShift()
        {
            var left = ParseAdd();
            while (Check(TokenType.Shl) || Check(TokenType.Shr))
            {
                int line = Cur.Line;
                var op = Cur.Value; Consume();
                left = new BinaryExpr { Op = op, Left = left, Right = ParseAdd(), Line = line };
            }
            return left;
        }

        private ExprNode ParseAdd()
        {
            var left = ParseMul();
            while (Check(TokenType.Plus) || Check(TokenType.Minus))
            {
                int line = Cur.Line;
                var op = Cur.Value; Consume();
                left = new BinaryExpr { Op = op, Left = left, Right = ParseMul(), Line = line };
            }
            return left;
        }

        private ExprNode ParseMul()
        {
            var left = ParseUnary();
            while (Check(TokenType.Star) || Check(TokenType.Slash) || Check(TokenType.Percent))
            {
                int line = Cur.Line;
                var op = Cur.Value; Consume();
                left = new BinaryExpr { Op = op, Left = left, Right = ParseUnary(), Line = line };
            }
            return left;
        }

        private ExprNode ParseUnary()
        {
            int line = Cur.Line;
            if (Check(TokenType.Minus)) { Consume(); return new UnaryExpr { Op = "-", Operand = ParseUnary(), Line = line }; }
            if (Check(TokenType.Not)) { Consume(); return new UnaryExpr { Op = "!", Operand = ParseUnary(), Line = line }; }
            if (Check(TokenType.Tilde)) { Consume(); return new UnaryExpr { Op = "~", Operand = ParseUnary(), Line = line }; }
            return ParsePostfix();
        }

        private ExprNode ParsePostfix()
        {
            var expr = ParsePrimary();
            while (true)
            {
                int line = Cur.Line;
                if (Check(TokenType.LBracket))
                {
                    Consume();
                    var idx = ParseExpr();
                    Expect(TokenType.RBracket);
                    expr = new ArrayIndexExpr { Array = expr, Index = idx, Line = line };
                }
                else if (Check(TokenType.PlusPlus))
                {
                    Consume();
                    expr = new UnaryExpr { Op = "++", Operand = expr, Line = line };
                }
                else if (Check(TokenType.MinusMinus))
                {
                    Consume();
                    expr = new UnaryExpr { Op = "--", Operand = expr, Line = line };
                }
                else break;
            }
            return expr;
        }

        private ExprNode ParsePrimary()
        {
            int line = Cur.Line;
            if (Check(TokenType.IntLiteral))
            {
                var v = int.Parse(Cur.Value); Consume();
                return new IntLiteralExpr { Value = v, Line = line };
            }
            if (Check(TokenType.StringLiteral))
            {
                var s = Cur.Value; Consume();
                return new StringLiteralExpr { Value = s, Line = line };
            }
            // Булевы литералы: true → 1, false → 0
            if (Check(TokenType.KwTrue))
            {
                Consume();
                return new BoolLiteralExpr { Value = true, Line = line };
            }
            if (Check(TokenType.KwFalse))
            {
                Consume();
                return new BoolLiteralExpr { Value = false, Line = line };
            }
            if (Check(TokenType.Identifier))
            {
                var name = Cur.Value; Consume();
                if (Check(TokenType.LParen))
                {
                    Consume();
                    var args = new List<ExprNode>();
                    if (!Check(TokenType.RParen))
                        do { args.Add(ParseExpr()); } while (Match(TokenType.Comma));
                    Expect(TokenType.RParen);
                    return new CallExpr { FuncName = name, Args = args, Line = line };
                }
                return new IdentExpr { Name = name, Line = line };
            }
            if (Check(TokenType.LParen))
            {
                Consume();
                var e = ParseExpr();
                Expect(TokenType.RParen);
                return e;
            }
            throw new Exception($"Строка {line}: неожиданный токен '{Cur.Value}'");
        }
    }
}
