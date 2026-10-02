using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Utility;

namespace Luft.Lsp;

public sealed record LocalVar(string Name, string Kind, AeroType Type, ExpressionNode? Init, SourceSpan Span);

public static class AstNav
{
    public static IEnumerable<AstNode> Children(AstNode node)
    {
        switch (node)
        {
            case FileNode fi: foreach (var m in fi.Modules) yield return m; break;
            case ModuleDeclarationNode mo: foreach (var d in mo.Declarations) yield return d; break;
            case FunctionDeclarationNode fu:
                foreach (var p in fu.Parameters) yield return p;
                if (fu.Body is not null) yield return fu.Body;
                break;
            case OperatorDeclarationNode op:
                foreach (var p in op.Parameters) yield return p;
                if (op.Body is not null) yield return op.Body;
                break;
            case ExtensionDeclarationNode ex: yield return ex.Extension; break;
            case ExtensionBlockDeclarationNode eb: foreach (var e in eb.Extensions) yield return e; break;
            case StructDeclarationNode st: foreach (var d in st.Declarations) yield return d; break;
            case RecordDeclarationNode re: foreach (var d in re.Declarations) yield return d; break;
            case ClassDeclarationNode cl: foreach (var d in cl.Declarations) yield return d; break;
            case TraitDeclarationNode tr: foreach (var d in tr.Declarations) yield return d; break;
            case AnnotationDeclarationNode an: foreach (var d in an.Declarations) yield return d; break;
            case EnumDeclarationNode en: foreach (var m in en.Members) yield return m; break;
            case EnumMemberNode em: if (em.Value is not null) yield return em.Value; break;
            case PropertyDeclarationNode pr:
                if (pr.Getter is not null) yield return pr.Getter;
                if (pr.Setter is not null) yield return pr.Setter;
                if (pr.Initializer is not null) yield return pr.Initializer;
                break;
            case PropertyAccessorNode pa: if (pa.Body is not null) yield return pa.Body; break;
            case FieldDeclarationNode fd: if (fd.Initializer is not null) yield return fd.Initializer; break;
            case PrimaryConstructorDeclarationNode pc: foreach (var v in pc.Variables) yield return v; break;
            case ConstructorDeclarationNode co:
                foreach (var p in co.Parameters) yield return p;
                if (co.Body is not null) yield return co.Body;
                break;
            case DestructorDeclarationNode de: if (de.Body is not null) yield return de.Body; break;
            case ParamNode pn: if (pn.Initializer is not null) yield return pn.Initializer; break;

            case VariableStatementNode vs: if (vs.Initializer is not null) yield return vs.Initializer; break;
            case ReturnStatementNode rs: if (rs.Value is not null) yield return rs.Value; break;
            case WhileStatementNode ws: yield return ws.Condition; yield return ws.Body; break;
            case ExpressionStatementNode es: yield return es.Expression; break;
            case AssignmentStatementNode asg: yield return asg.Target; yield return asg.Value; break;

            case BlockExpressionNode bl: foreach (var s in bl.Statements) yield return s; break;
            case IfExpressionNode ie:
                yield return ie.Condition;
                yield return ie.ThenBody;
                foreach (var (c, b) in ie.ElseIfs) { yield return c; yield return b; }
                if (ie.ElseBody is not null) yield return ie.ElseBody;
                break;
            case ForExpressionNode fe: yield return fe.Item; yield return fe.Collection; yield return fe.Body; break;
            case MatchExpressionNode ma: yield return ma.Target; foreach (var c in ma.Cases) yield return c; break;
            case CaseExpressionNode ca:
                yield return ca.Pattern;
                if (ca.Guard is not null) yield return ca.Guard;
                yield return ca.Body;
                break;
            case ConstantPattern cp: yield return cp.Value; break;
            case OrPattern orp: yield return orp.Left; yield return orp.Right; break;
            case RangePattern rp: yield return rp.Range; break;
            case ArrayLiteralExpressionNode al: foreach (var e in al.Elements) yield return e; break;
            case MemberAccessExpressionNode me: yield return me.Target; yield return me.Member; break;
            case CallExpressionNode ce: yield return ce.Target; foreach (var a in ce.Arguments) yield return a; break;
            case IndexExpressionNode ix: yield return ix.Target; yield return ix.Index; break;
            case BinaryExpressionNode bi: yield return bi.Left; yield return bi.Right; break;
            case RangeExpressionNode rg:
                if (rg.Left is not null) yield return rg.Left;
                if (rg.Right is not null) yield return rg.Right;
                break;
            case UnaryExpressionNode un: yield return un.Operand; break;
            case LambdaExpressionNode la:
                foreach (var p in la.Parameters) yield return p;
                yield return la.Body;
                break;
            case StringInterpolationExpressionNode si: foreach (var p in si.Parts) yield return p; break;
            case ConcurrentExpressionNode cc: yield return cc.Body; break;
            case SpawnExpressionNode sp: yield return sp.Body; break;
            case ScopedExpressionNode sc: yield return sc.Scoped; break;
            case PatternTestExpressionNode pt: yield return pt.Target; yield return pt.Pattern; break;
        }
    }

    // Path of nodes from the file down to the innermost node containing pos
    public static List<AstNode> ChainAt(FileNode file, TextLocation pos)
    {
        var chain = new List<AstNode>();
        Descend(file, pos, chain);
        return chain;
    }

    static bool Descend(AstNode node, TextLocation pos, List<AstNode> chain)
    {
        // Module spans aren't reliable, so they are always entered and dropped again if nothing inside matches
        bool structural = node is FileNode or ModuleDeclarationNode;
        if (!structural && !node.Span.Contains(pos)) return false;

        int mark = chain.Count;
        chain.Add(node);
        foreach (var child in Children(node))
            if (Descend(child, pos, chain)) return true;

        if (!structural) return true;
        chain.RemoveRange(mark, chain.Count - mark);
        return false;
    }

    public static List<string> TypeNamesOf(List<AstNode> chain) => chain.Select(n => n switch
    {
        ClassDeclarationNode c => c.Name,
        StructDeclarationNode s => s.Name,
        RecordDeclarationNode r => r.Name,
        TraitDeclarationNode t => t.Name,
        EnumDeclarationNode e => e.Name,
        _ => null
    }).OfType<string>().ToList();

    // Parameters plus every variable declared before pos in the enclosing function-like member
    public static List<LocalVar> LocalsAt(List<AstNode> chain, TextLocation pos)
    {
        var result = new List<LocalVar>();
        var owner = chain.LastOrDefault(n => n is FunctionDeclarationNode or OperatorDeclarationNode
            or ConstructorDeclarationNode or DestructorDeclarationNode or PropertyDeclarationNode);
        if (owner is null) return result;

        switch (owner)
        {
            case FunctionDeclarationNode f: AddParams(f.Parameters, result); break;
            case OperatorDeclarationNode o: AddParams(o.Parameters, result); break;
            case ConstructorDeclarationNode c: AddParams(c.Parameters, result); break;
            case PropertyDeclarationNode p: result.Add(new LocalVar("value", "parameter", p.Type, null, p.Span)); break;
        }

        Collect(owner, pos, result);
        return result;
    }

    static void AddParams(IEnumerable<ParamNode> parameters, List<LocalVar> acc)
    {
        foreach (var p in parameters) acc.Add(new LocalVar(p.Name, "parameter", p.Type, p.Initializer, p.Span));
    }

    static void Collect(AstNode node, TextLocation pos, List<LocalVar> acc)
    {
        switch (node)
        {
            case VariableStatementNode v when v.Span.Start < pos:
                acc.Add(new LocalVar(v.Name, v.VarKind.AsString(), v.Type, v.Initializer, v.Span));
                break;
            case ForExpressionNode f when f.Span.Start < pos:
                acc.Add(new LocalVar(f.Item.Name, "val", f.Item.Type, null, f.Item.Span));
                break;
            case LambdaExpressionNode l when l.Span.Start < pos:
                AddParams(l.Parameters, acc);
                break;
            case TypePattern { Binding: not null } t:
                acc.Add(new LocalVar(t.Binding, "val", t.Type, null, t.Span));
                break;
        }

        foreach (var child in Children(node)) Collect(child, pos, acc);
    }
}