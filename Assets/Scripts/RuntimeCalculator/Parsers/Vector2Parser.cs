using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using RuntimeCalculator.Vector2s;
using UnityEngine;

namespace RuntimeCalculator.Parsers {
    class Vector2Parser : ExpressionParserBaseVisitor<Vector> {
        public static Vector ParseExpression(string expr) {
            try {
                var stream = CharStreams.fromString(expr);
                var lexer = new ExpressionLexer(stream);
                var tokens = new CommonTokenStream(lexer);
                var parser = new ExpressionParser(tokens) { BuildParseTree = true };
                var visitor = new Vector2Parser();
                return visitor.VisitMainVec2Expr(parser.mainVec2Expr()).GetSimplified();
            } catch (System.Exception e) {
                throw new System.Exception(e.Message + ": " + expr + "\n" + e.StackTrace);
            }
        }

        public override Vector VisitMainVec2Expr([NotNull] ExpressionParser.MainVec2ExprContext context) {
            if (context.Eof() == null) throw new System.Exception("invalid vec2 expression");
            return VisitVec2Expr(context.vec2Expr());
        }

        public override Vector VisitVec2Expr([NotNull] ExpressionParser.Vec2ExprContext context) {
            if (context.@operator != null) {
                var a = VisitVec2Expr(context.vec2Expr(0));
                Vector b = null;
                Numbers.Number f = null;
                if (context.@operator.Type == ExpressionParser.DIV || context.@operator.Type == ExpressionParser.MUL) {
                    f = new FloatParser().VisitFloatExpr(context.floatExpr());
                } else {
                    b = VisitVec2Expr(context.vec2Expr(1));
                }
                switch (context.@operator.Type) {
                    case ExpressionParser.DIV:
                        return new Division(a, f);
                    case ExpressionParser.MUL:
                        return new Multiplication(a, f);
                    case ExpressionParser.ADD:
                        return new Sum(a, b);
                    case ExpressionParser.SUB:
                        return new Subtraction(a, b);
                    default:
                        return null;
                }
            } else if (context.vec2() != null) {
                var parser = new FloatParser();
                var x = parser.VisitFloatExpr(context.vec2().floatExpr(0));
                var y = parser.VisitFloatExpr(context.vec2().floatExpr(1));
                var res = new Compound(x, y);
                if (context.SUB() != null) {
                    return new Multiplication(res, new Numbers.Constant(-1));
                } else {
                    return res;
                }
            } else if (context.variable_vec2() != null) {
                var res = VisitVariable_vec2(context.variable_vec2());
                if (context.SUB() != null) {
                    return new Multiplication(res, new Numbers.Constant(-1));
                } else {
                    return res;
                }
            } else if (context.IF() != null) {
                var a = new BooleanParser().VisitBoolExpr(context.boolExpr());
                var b = VisitVec2Expr(context.vec2Expr(0));
                var c = VisitVec2Expr(context.vec2Expr(1));
                return new IfFunction(a, new Vector[] { b, c });
            } else if (context.LERP() != null) {
                var a = VisitVec2Expr(context.vec2Expr(0));
                var b = VisitVec2Expr(context.vec2Expr(1));
                var c = new FloatParser().VisitFloatExpr(context.floatExpr());
                return new Lerp(a, b, c);
            } else if (context.ROTATE() != null) {
                var a = VisitVec2Expr(context.vec2Expr(0));
                var b = new FloatParser().VisitFloatExpr(context.floatExpr());
                return new Rotate(a, b);
            } else if (context.func2() != null) {
                var param = new Vector[context.vec2Expr().Length];
                for (int i = 0; i < param.Length; i++) {
                    param[i] = VisitVec2Expr(context.vec2Expr(i));
                }
                return new Function(context.func2().GetText(), param);
            } else if (context.vec2Expr() != null) {
                var res = VisitVec2Expr(context.vec2Expr(0));
                if (context.SUB() != null) {
                    return new Multiplication(res, new Numbers.Constant(-1));
                } else {
                    return res;
                }
            } else {
                return null;
            }
        }

        public override Vector VisitVariable_vec2([NotNull] ExpressionParser.Variable_vec2Context context) {
            return new Variable(context.GetText());
        }
    }
}
