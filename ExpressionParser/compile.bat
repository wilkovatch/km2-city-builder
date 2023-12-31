java -jar antlr-4.11.1-complete.jar -visitor -no-listener -Dlanguage=CSharp ExpressionLexer.g4 ExpressionParser.g4
cp *.cs ""../Assets/CustomPackages/expressions-antlr4/"
