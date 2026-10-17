rem Regenerates the C# lexer, parser and visitors of the expression language.
rem Requires Java 11+ and antlr-4.11.1-complete.jar (https://www.antlr.org/download/) in this folder.
java -jar antlr-4.11.1-complete.jar -visitor -no-listener -Dlanguage=CSharp ExpressionLexer.g4 ExpressionParser.g4
copy /Y *.cs ..\CustomPackages\expressions-antlr4\
del *.cs *.interp *.tokens
