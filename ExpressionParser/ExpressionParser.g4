parser grammar ExpressionParser;
@header {#pragma warning disable 3021}

options {
    tokenVocab = ExpressionLexer;
}

variable: VARIABLE;

variable_bool: VARIABLE;

variable_vec3: VARIABLE;

variable_vec2: VARIABLE;

func: VARIABLE;

func3: VARIABLE;

func2: VARIABLE;

vec3: VEC3 OPN_B floatExpr SEP floatExpr SEP floatExpr CLS_B;

vec2: VEC2 OPN_B floatExpr SEP floatExpr CLS_B;

floatExpr: floatExpr operator=PWR floatExpr
         | floatExpr operator=(MUL | DIV | MOD) floatExpr
         | floatExpr operator=(ADD | SUB) floatExpr
	     | SUB? (NUMBER | variable | (OPN_B floatExpr CLS_B))
         | IF OPN_B boolExpr SEP floatExpr SEP floatExpr CLS_B
         | VEC3FUNC OPN_B vec3Expr (SEP vec3Expr)* CLS_B
         | VEC2FUNC OPN_B vec2Expr (SEP vec2Expr)* CLS_B
         | func OPN_B floatExpr (SEP floatExpr)* CLS_B
;

boolExpr: boolExpr operator=XOR boolExpr
        | boolExpr operator=AND boolExpr
        | boolExpr operator=OR boolExpr
        | NOT? (variable_bool | (OPN_B boolExpr CLS_B))
        | floatExpr cmp_operator=(GTE | GT | LTE | LT | EQ | NEQ) floatExpr
;

vec3Expr: vec3Expr operator=(MUL | DIV) floatExpr
        | vec3Expr operator=(ADD | SUB) vec3Expr
	    | SUB? (vec3 | variable_vec3 | (OPN_B vec3Expr CLS_B))
        | IF OPN_B boolExpr SEP vec3Expr SEP vec3Expr CLS_B
        | (LERP | ROTATE) OPN_B vec3Expr SEP vec3Expr SEP floatExpr CLS_B
        | func3 OPN_B vec3Expr (SEP vec3Expr)* CLS_B
;

vec2Expr: vec2Expr operator=(MUL | DIV) floatExpr
        | vec2Expr operator=(ADD | SUB) vec2Expr
	    | SUB? (vec2 | variable_vec2 | (OPN_B vec2Expr CLS_B))
        | IF OPN_B boolExpr SEP vec2Expr SEP vec2Expr CLS_B
        | LERP OPN_B vec2Expr SEP vec2Expr SEP floatExpr CLS_B
		| ROTATE OPN_B vec2Expr SEP floatExpr CLS_B
        | func2 OPN_B vec2Expr (SEP vec2Expr)* CLS_B
;

mainFloatExpr: floatExpr EOF;

mainBoolExpr: boolExpr EOF;

mainVec3Expr: vec3Expr EOF;

mainVec2Expr: vec2Expr EOF;
