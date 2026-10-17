using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using RuntimeCalculator.Booleans;

namespace RuntimeCalculator.Parsers {
    public class BooleanParser : ExpressionParserBaseVisitor<Boolean> {
        public static Boolean ParseExpression(string expr) {
            try {
                var stream = CharStreams.fromString(expr);
                var lexer = new ExpressionLexer(stream);
                var tokens = new CommonTokenStream(lexer);
                var parser = new ExpressionParser(tokens) { BuildParseTree = true };
                var visitor = new BooleanParser();
                return visitor.VisitMainBoolExpr(parser.mainBoolExpr());
            } catch (System.Exception e) {
                throw new System.Exception(e.Message + ": " + expr + "\n" + e.StackTrace);
            }
        }

        public override Boolean VisitMainBoolExpr([NotNull] ExpressionParser.MainBoolExprContext context) {
            if (context.Eof() == null) throw new System.Exception("invalid bool expression");
            return VisitBoolExpr(context.boolExpr());
        }

        public override Boolean VisitBoolExpr([NotNull] ExpressionParser.BoolExprContext context) {
            if (context.@operator != null) {
                var a = VisitBoolExpr(context.boolExpr(0));
                var b = VisitBoolExpr(context.boolExpr(1));
                switch (context.@operator.Type) {
                    case ExpressionParser.AND:
                        return new And(a, b);
                    case ExpressionParser.OR:
                        return new Or(a, b);
                    case ExpressionParser.XOR:
                        return new Xor(a, b);
                    default:
                        return null;
                }
            } else if (context.@cmp_operator != null) {
                var parser = new FloatParser();
                var a = parser.VisitFloatExpr(context.floatExpr(0));
                var b = parser.VisitFloatExpr(context.floatExpr(1));
                switch (context.@cmp_operator.Type) {
                    case ExpressionParser.GTE:
                        return new GTE(a, b);
                    case ExpressionParser.GT:
                        return new GT(a, b);
                    case ExpressionParser.LTE:
                        return new LTE(a, b);
                    case ExpressionParser.LT:
                        return new LT(a, b);
                    case ExpressionParser.EQ:
                        return new EQ(a, b);
                    case ExpressionParser.NEQ:
                        return new Not(new EQ(a, b));
                    default:
                        return null;
                }
            } else if (context.variable_bool() != null) {
                var res = VisitVariable_bool(context.variable_bool());
                if (context.NOT() != null) {
                    return new Not(res);
                } else {
                    return res;
                }
            } else if (context.boolExpr() != null) {
                var res = VisitBoolExpr(context.boolExpr(0));
                if (context.NOT() != null) {
                    return new Not(res);
                } else {
                    return res;
                }
            } else {
                return null;
            }
        }

        public override Boolean VisitVariable_bool([NotNull] ExpressionParser.Variable_boolContext context) {
            var text = context.GetText();
            if (text == "true") {
                return new BooleanConstant(true);
            } else if (text == "false") {
                return new BooleanConstant(false);
            } else {
                return new BooleanVariable(text);
            }
        }
    }
}
