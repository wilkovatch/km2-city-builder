using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using RuntimeCalculator.Vector3s;
using UnityEngine;

namespace RuntimeCalculator.Parsers {
    class Vector3Parser : ExpressionParserBaseVisitor<Vector> {
        public static Vector ParseExpression(string expr) {
            try {
                var stream = CharStreams.fromString(expr);
                var lexer = new ExpressionLexer(stream);
                var tokens = new CommonTokenStream(lexer);
                var parser = new ExpressionParser(tokens) { BuildParseTree = true };
                var visitor = new Vector3Parser();
                return visitor.VisitMainVec3Expr(parser.mainVec3Expr()).GetSimplified();
            } catch (System.Exception e) {
                throw new System.Exception(e.Message + ": " + expr + "\n" + e.StackTrace);
            }
        }

        public override Vector VisitMainVec3Expr([NotNull] ExpressionParser.MainVec3ExprContext context) {
            if (context.Eof() == null) throw new System.Exception("invalid vec3 expression");
            return VisitVec3Expr(context.vec3Expr());
        }

        public override Vector VisitVec3Expr([NotNull] ExpressionParser.Vec3ExprContext context) {
            if (context.@operator != null) {
                var a = VisitVec3Expr(context.vec3Expr(0));
                Vector b = null;
                Numbers.Number f = null;
                if (context.@operator.Type == ExpressionParser.DIV || context.@operator.Type == ExpressionParser.MUL) {
                    f = new FloatParser().VisitFloatExpr(context.floatExpr());
                } else {
                    b = VisitVec3Expr(context.vec3Expr(1));
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
            } else if (context.vec3() != null) {
                var parser = new FloatParser();
                var x = parser.VisitFloatExpr(context.vec3().floatExpr(0));
                var y = parser.VisitFloatExpr(context.vec3().floatExpr(1));
                var z = parser.VisitFloatExpr(context.vec3().floatExpr(2));
                var res = new Compound(x, y, z);
                if (context.SUB() != null) {
                    return new Multiplication(res, new Numbers.Constant(-1));
                } else {
                    return res;
                }
            } else if (context.variable_vec3() != null) {
                var res = VisitVariable_vec3(context.variable_vec3());
                if (context.SUB() != null) {
                    return new Multiplication(res, new Numbers.Constant(-1));
                } else {
                    return res;
                }
            } else if (context.IF() != null) {
                var a = new BooleanParser().VisitBoolExpr(context.boolExpr());
                var b = VisitVec3Expr(context.vec3Expr(0));
                var c = VisitVec3Expr(context.vec3Expr(1));
                return new IfFunction(a, new Vector[] { b, c });
            } else if (context.LERP() != null) {
                var a = VisitVec3Expr(context.vec3Expr(0));
                var b = VisitVec3Expr(context.vec3Expr(1));
                var c = new FloatParser().VisitFloatExpr(context.floatExpr());
                return new Lerp(a, b, c);
            } else if (context.ROTATE() != null) {
                var a = VisitVec3Expr(context.vec3Expr(0));
                var b = VisitVec3Expr(context.vec3Expr(1));
                var c = new FloatParser().VisitFloatExpr(context.floatExpr());
                return new Rotate(a, b, c);
            } else if (context.func3() != null) {
                var param = new Vector[context.vec3Expr().Length];
                for (int i = 0; i < param.Length; i++) {
                    param[i] = VisitVec3Expr(context.vec3Expr(i));
                }
                return new Function(context.func3().GetText(), param);
            } else if (context.vec3Expr() != null) {
                var res = VisitVec3Expr(context.vec3Expr(0));
                if (context.SUB() != null) {
                    return new Multiplication(res, new Numbers.Constant(-1));
                } else {
                    return res;
                }
            } else {
                return null;
            }
        }

        public override Vector VisitVariable_vec3([NotNull] ExpressionParser.Variable_vec3Context context) {
            return new Variable(context.GetText());
        }
    }
}
