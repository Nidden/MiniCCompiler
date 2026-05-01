using System.Collections.Generic;

namespace CompMacro11
{
    // ─── Типы ───────────────────────────────────────────────────
    public class MiniCType
    {
        public bool IsVoid;
        public bool IsBool;  // bool — хранится как int (0/1), но выводится как bool
        public bool IsArray;
        public List<int> Dims;
        public MiniCType() { Dims = new List<int>(); }
        public int TotalElements()
        {
            if (!IsArray || Dims.Count == 0) return 1;
            int n = 1; foreach (var d in Dims) { if (d > 0) n *= d; }
            return n;
        }
        public int Stride(int dimIndex)
        {
            int s = 1;
            for (int i = dimIndex + 1; i < Dims.Count; i++)
                s *= Dims[i];
            return s;
        }
        public override string ToString()
        {
            if (IsVoid) return "void";
            if (IsBool && !IsArray) return "bool";
            if (!IsArray) return "int";
            var sb = new System.Text.StringBuilder(IsBool ? "bool" : "int");
            foreach (var d in Dims) sb.Append(d < 0 ? "[]" : $"[{d}]");
            return sb.ToString();
        }
    }

    // ─── Программа ──────────────────────────────────────────────
    public class ProgramNode
    {
        public List<VarDeclStmtNode> Globals = new List<VarDeclStmtNode>();
        public List<FuncDeclNode> Functions = new List<FuncDeclNode>();
        public List<GlobalArrayNode> GlobalVars = new List<GlobalArrayNode>();
    }

    // ─── Глобальный массив (статическая память, .PSECT DATA) ────
    public class GlobalArrayNode
    {
        public string Name;
        public MiniCType Type;
        public ArrayInitNode ArrayInit; // null = нули
        public int Line;
    }

    // ─── Параметр функции ────────────────────────────────────────
    public class ParamNode
    {
        public string Name;
        public MiniCType Type;
    }

    // ─── Функция ────────────────────────────────────────────────
    public class FuncDeclNode
    {
        public string Name;
        public MiniCType ReturnType;
        public List<ParamNode> Params = new List<ParamNode>();
        public BlockStmtNode Body;
        public int Line;
    }

    // ─── Операторы ──────────────────────────────────────────────
    public abstract class StmtNode { public int Line; }

    public class BlockStmtNode : StmtNode
    {
        public List<StmtNode> Stmts = new List<StmtNode>();
    }

    public class VarDeclStmtNode : StmtNode
    {
        public string Name;
        public MiniCType Type;
        public ExprNode Init; // null если нет инициализатора
        public ArrayInitNode ArrayInit; // для массивов
    }

    public class ExprStmtNode : StmtNode { public ExprNode Expr; }

    public class IfStmtNode : StmtNode
    {
        public ExprNode Cond;
        public StmtNode Then;
        public StmtNode Else; // null если нет
    }

    public class WhileStmtNode : StmtNode
    {
        public ExprNode Cond;
        public StmtNode Body;
    }

    public class ForStmtNode : StmtNode
    {
        public StmtNode Init;   // VarDecl или ExprStmt или null
        public ExprNode Cond;   // null = бесконечно
        public ExprNode Post;   // null
        public StmtNode Body;
    }

    public class ReturnStmtNode : StmtNode { public ExprNode Value; }
    public class BreakStmtNode : StmtNode { }
    public class ContinueStmtNode : StmtNode { }

    // ─── Выражения ──────────────────────────────────────────────
    public abstract class ExprNode { public int Line; }

    public class IntLiteralExpr : ExprNode { public int Value; }
    public class BoolLiteralExpr : ExprNode { public bool Value; } // true=1, false=0

    public class IdentExpr : ExprNode { public string Name; }

    public class ArrayIndexExpr : ExprNode
    {
        public ExprNode Array;
        public ExprNode Index;
    }

    public class UnaryExpr : ExprNode
    {
        public string Op; // "-", "!", "++", "--"
        public ExprNode Operand;
    }

    public class BinaryExpr : ExprNode
    {
        public string Op;
        public ExprNode Left, Right;
    }

    public class AssignExpr : ExprNode
    {
        public string Op; // "=", "+=", "-=", ...
        public ExprNode Target;
        public ExprNode Value;
    }

    public class CallExpr : ExprNode
    {
        public string FuncName;
        public List<ExprNode> Args = new List<ExprNode>();
    }

    // ─── Инициализатор массива ──────────────────────────────────
    public class ArrayInitNode
    {
        public List<object> Elements; // int или ArrayInitNode (вложенные)
        public List<int> Flat = new List<int>();
    }

    // ── Одна ветка switch ────────────────────────────────────
    public class SwitchCase
    {
        public int? Value;                   // null = default
        public List<StmtNode> Body = new List<StmtNode>();
    }

    // switch(expr) { case v: stmts... default: stmts... }
    public class SwitchStmtNode : StmtNode
    {
        public ExprNode Expr;
        public List<SwitchCase> Cases = new List<SwitchCase>();
    }

    // do { body } while (cond);
    public class DoWhileStmtNode : StmtNode
    {
        public StmtNode Body;
        public ExprNode Cond;
    }

    // Строковый литерал "text"
    public class StringLiteralExpr : ExprNode
    {
        public string Value;
    }
}
