using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using System.Text.RegularExpressions;
using RuntimeCalculator.Numbers;

namespace RuntimeCalculator.Parsers {
    public class FloatParser : ExpressionParserBaseVisitor<Number> {
        public static Number ParseExpression(string expr) {
            try {
                var stream = CharStreams.fromString(expr);
                var lexer = new ExpressionLexer(stream);
                var tokens = new CommonTokenStream(lexer);
                var parser = new ExpressionParser(tokens) { BuildParseTree = true };
                var visitor = new FloatParser();
                return visitor.VisitMainFloatExpr(parser.mainFloatExpr()).GetSimplified();
            } catch (System.Exception e) {
                throw new System.Exception(e.Message + ": " + expr + "\n" + e.StackTrace);
            }
        }

        public override Number VisitMainFloatExpr([NotNull] ExpressionParser.MainFloatExprContext context) {
            if (context.Eof() == null) throw new System.Exception("invalid float expression");
            return VisitFloatExpr(context.floatExpr());
        }

        public override Number VisitFloatExpr([NotNull] ExpressionParser.FloatExprContext context) {
            if (context.@operator != null) {
                var a = VisitFloatExpr(context.floatExpr(0));
                var b = VisitFloatExpr(context.floatExpr(1));
                switch (context.@operator.Type) {
                    case ExpressionParser.DIV:
                        return new Division(a, b);
                    case ExpressionParser.MUL:
                        return new Multiplication(a, b);
                    case ExpressionParser.ADD:
                        return new Sum(a, b);
                    case ExpressionParser.SUB:
                        return new Subtraction(a, b);
                    case ExpressionParser.MOD:
                        return new Modulo(a, b);
                    case ExpressionParser.PWR:
                        return new Power(a, b);
                    default:
                        return null;
                }
            } else if (context.NUMBER() != null) {
                var res = new Constant(float.Parse(context.NUMBER().GetText(), System.Globalization.CultureInfo.InvariantCulture));
                if (context.SUB() != null) {
                    return new Multiplication(new Constant(-1), res);
                } else {
                    return res;
                }
            } else if (context.variable() != null) {
                var res = VisitVariable(context.variable());
                if (context.SUB() != null) {
                    return new Multiplication(new Constant(-1), res);
                } else {
                    return res;
                }
            } else if (context.IF() != null) {
                var a = new BooleanParser().VisitBoolExpr(context.boolExpr());
                var b = VisitFloatExpr(context.floatExpr(0));
                var c = VisitFloatExpr(context.floatExpr(1));
                return new IfFunction(a, new Number[] { b, c });
            } else if (context.VEC3FUNC() != null) {
                var param = new Vector3s.Vector[context.vec3Expr().Length];
                var parser = new Vector3Parser();
                for (int i = 0; i < param.Length; i++) {
                    param[i] = parser.VisitVec3Expr(context.vec3Expr(i));
                }
                return new Vec3Function(context.VEC3FUNC().GetText(), param);
            } else if (context.VEC2FUNC() != null) {
                var param = new Vector2s.Vector[context.vec2Expr().Length];
                var parser = new Vector2Parser();
                for (int i = 0; i < param.Length; i++) {
                    param[i] = parser.VisitVec2Expr(context.vec2Expr(i));
                }
                return new Vec2Function(context.VEC2FUNC().GetText(), param);
            } else if (context.func() != null) {
                var param = new Number[context.floatExpr().Length];
                for (int i = 0; i < param.Length; i++) {
                    param[i] = VisitFloatExpr(context.floatExpr(i));
                }
                return new Function(context.func().GetText(), param);
            } else if (context.floatExpr() != null) {
                var res = VisitFloatExpr(context.floatExpr(0));
                if (context.SUB() != null) {
                    return new Multiplication(new Constant(-1), res);
                } else {
                    return res;
                }
            } else {
                return null;
            }
        }

        public override Number VisitVariable([NotNull] ExpressionParser.VariableContext context) {
            var t = context.GetText();
            if (new Regex(Constant.Regex()).IsMatch(t)) {
                return new Constant(context.GetText());
            } else {
                return new Variable(context.GetText());
            }
        }
    }
}
