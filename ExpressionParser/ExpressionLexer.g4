lexer grammar ExpressionLexer;
@header {#pragma warning disable 3021}

fragment DIGIT: [0-9];
fragment LETTER: [a-zA-Z_];

//arithmetics
ADD:       '+';
SUB:       '-';
MUL:       '*';
DIV:       '/';
PWR:       '^';
MOD:       '%';
VEC3FUNC:   ('dot'|'angle'|'signedAngle'|'magnitude'|'distance'|'v3x'|'v3y'|'v3z');
VEC2FUNC:   ('dot2'|'angle2'|'signedAngle2'|'distance2'|'v2x'|'v2y');

//comparisons
AND:       'and';
OR:        'or';
XOR:       'xor';
NOT:       'not';
GTE:       '>=';
GT:        '>';
LTE:       '<=';
LT:        '<';
EQ:        '=';
NEQ:       '!=';

//other
OPN_B:     '(';
CLS_B:     ')';
VEC3:      'vec3';
VEC2:      'vec2';
IF:        'if';
SEP:       ',';
LERP:      'lerp';
ROTATE:    'rotate';

NUMBER: DIGIT+ ('.' DIGIT+)?;
VARIABLE: LETTER (LETTER | DIGIT)*;
