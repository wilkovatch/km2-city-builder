# Expression Language

Almost every value in a core definition that is not a fixed setting is an **expression**: a small formula evaluated by the program with the current parameters and geometry. Expressions describe definitions, mesh vertices and UVs, conditions, lane positions, prop placement rules and more.

The implementation is described in [Runtime Calculator](Runtime-Calculator).

## Types

Every expression has one of four result types, given by where it is used (or by the `type` of a definition):

| Type | Used for | Examples |
|---|---|---|
| float | Numbers (also `int` and `enum` values) | `0.5*totalWidth`, `if(divider=0, 0, dividerWidth)` |
| bool | Conditions | `isDouble and divider > 0`, `not hasRail` |
| vec3 | Positions and directions | `v1-h+localRight*realFwParkW`, `vec3(0, -ground, 0)` |
| vec2 | UVs | `vec2(zRd, 0)`, `vec2(absX1, absZ1) * (-0.25)` |

Booleans and integers are stored as floats (true = 1, false = 0), so a boolean variable can be used in a float expression (`applyHeightAnyway * 0.15`) and a float variable in a boolean expression (true when greater than 0).

All positions and directions are in the program's coordinate system: Y up, +Z forward, left-handed.

## Syntax

### Literals and constants

* Numbers: `3`, `0.25` (a digit is required before the dot; no exponent notation).
* `pi`, `deg2rad`, `rad2deg`.
* `true`, `false` (in boolean expressions only).
* `vec3(x, y, z)`, `vec2(x, y)` where each component is a float expression.

### Variables

Identifiers made of letters, digits and underscores, starting with a letter or underscore. A variable can be a parameter, a definition, a loop variable or a built-in variable of the element type (listed on each element page). Using an unknown name makes the core fail to load (for float and boolean variables the error is `Invalid float parameter: <name>`).

Spaces, tabs and line breaks between tokens are ignored.

### Float operators and functions

| Syntax | Meaning |
|---|---|
| `a ^ b` | Power. Highest precedence, left associative (`2^3^2` is `(2^3)^2`). |
| `a * b`, `a / b`, `a % b` | Multiplication, division, remainder (sign of `a`). |
| `a + b`, `a - b` | Addition, subtraction. |
| `-x` | Negation of a number, variable or parenthesized expression. It binds tighter than `^`: `-x^2` is `(-x)^2`; write `-(x^2)`. It cannot be applied directly to a function call: write `-(sin(x))` or `0 - sin(x)`. |
| `if(condition, a, b)` | `a` if the boolean `condition` is true, `b` otherwise. |
| `sin(x)`, `cos(x)`, `tan(x)` | Trigonometry, in radians. |
| `min(a, b)`, `max(a, b)`, `clamp(x, min, max)` | |
| `abs(x)`, `sign(x)`, `sqrt(x)` | `sign(0)` is 1. |
| `floor(x)`, `ceil(x)`, `round(x)` | |
| `lerp(a, b, t)` | Linear interpolation from `a` to `b`, `t` clamped to 0..1. |
| `rnd(a, b)` | Random number between `a` and `b`, different at each evaluation. |
| `rnd(a, b, seed)` | Deterministic random sequence: each call with the same seed returns the next number of that seed's sequence. The sequences restart every time a road places its props, so placements are stable between rebuilds. |
| `dot(u, v)`, `angle(u, v)`, `signedAngle(u, v, axis)` | On vec3 values. Angles in degrees, `angle` is unsigned. |
| `magnitude(v)`, `distance(u, v)` | Length of a vec3, distance between two vec3. |
| `v3x(v)`, `v3y(v)`, `v3z(v)` | Components of a vec3. |
| `dot2(u, v)`, `angle2(u, v)`, `signedAngle2(u, v)`, `distance2(u, v)`, `v2x(v)`, `v2y(v)` | The same for vec2. |

### Boolean operators

| Syntax | Meaning |
|---|---|
| `a = b`, `a != b`, `a > b`, `a >= b`, `a < b`, `a <= b` | Comparisons of float expressions (`=` is an exact comparison). |
| `not x`, `not (expression)` | Negation of a variable or a parenthesized expression. `not a > b` is not valid: write `not (a > b)`. |
| `a xor b`, `a and b`, `a or b` | In order of decreasing precedence. |

### Vector operators and functions

| Syntax | Meaning |
|---|---|
| `u + v`, `u - v` | Sum and difference of two vectors of the same type. |
| `v * x`, `v / x` | Multiplication and division by a float. **The vector must be on the left**: `localUp * h`, not `h * localUp`. |
| `-v` | Negation of a vector variable, constructor or parenthesized expression. |
| `if(condition, u, v)` | Conditional vector. |
| `lerp(u, v, t)` | Linear interpolation, `t` clamped to 0..1. |
| `rotate(v, axis, angle)` (vec3) | Rotation of `v` around `axis` by `angle` **degrees**. |
| `rotate(v, angle)` (vec2) | Rotation of `v` by `angle` **radians**. |
| `cross(u, v)`, `normalize(v)`, `project(u, v)`, `reflect(u, n)` (vec3) | `normalize` of a zero vector is zero. |
| `scale(u, v)`, `min(u, v)`, `max(u, v)` | Component-wise product, minimum and maximum (vec3 and vec2); `reflect` is also available for vec2. |

### Reserved words

`and`, `or`, `xor`, `not`, `if`, `vec3`, `vec2`, `lerp`, `rotate` and the vector function names (`dot`, `angle`, `signedAngle`, `magnitude`, `distance`, `v3x`, `v3y`, `v3z`, `dot2`, `angle2`, `signedAngle2`, `distance2`, `v2x`, `v2y`) cannot be used as variable names. `pi`, `deg2rad` and `rad2deg` are always read as constants; longer names that contain them (such as `spin`) are ordinary variables.

### Pitfalls

* Comparisons with `=` are exact; for computed values prefer `abs(a - b) < 0.001`.

## Definitions

Definitions are named, typed expressions declared in a type's `parameters.json` (or in a component's `localDefinitions`). Each one becomes a variable that later expressions can use.

```json
{ "name": "trueDivWidth", "type": "float", "value": "if(divider=0, 0, dividerWidth)" }
```

`type` is `float`, `int` (stored as a float, not rounded), `bool`, `vec3` or `vec2`.

Instead of `value`, a definition can have `script`: the name of a function of a Lua script of the core that computes it, for logic that would be too involved for an expression (see [Core Lua Scripts](Core-Lua-Scripts)):

```json
{ "name": "startTrafficLightBasePosition", "type": "vec3", "script": "trafficLight.startPosition" }
```

| Kind | Where | When it is evaluated |
|---|---|---|
| `staticDefinitions` | `parameters.json` | Once per element (re)build, after the parameters are read. Can use parameters and the element's global built-in variables. |
| `dynamicDefinitions` | `parameters.json` | For each section/segment/iteration, after the per-section built-in variables are set. |
| `lateDefinitions` | `parameters.json`, grouped by `category` | On demand: `common` after a road's line is computed, other categories when a parametric object needs them (see [Core Props and Objects](Core-Props-and-Objects#parametric-objects)). |
| `localDefinitions` | A component file | For each section, before the component's mesh is evaluated, if the component's condition is true. |

Definitions are evaluated **in the order they are listed**, so a definition can only rely on definitions listed before it (in the same list or in an earlier stage). A definition can redefine a built-in variable: for example the MM2 junction redefines `v0` in its dynamic definitions to straighten the sidewalk.

Some variables are read by the program itself and must be defined by certain types, typically `uMult` and `vMult` (the texture scale of generated terrain-like surfaces). They are listed on each element page.

### Loop variables

`loopVariables` (in `parameters.json`) declares extra float variables to be used as loop indices in expression arrays:

```json
"loopVariables": ["i"]
```

## Expression arrays

Lists of values (mesh vertices, UVs, faces, face textures, section vertices, anchors, lane positions) can be written in three forms.

**Plain list**: one expression (or number, for float lists) per element.

```json
"vertices": ["v0-h", "v6-h"]
```

**Conditional blocks**: a list of objects with an `if` condition and the values to `append` when it is true. Each `append` can be a plain list or another list of conditional blocks (a loop inside a conditional block is not expanded: use loops only at the top level).

```json
"faces": [
    { "if": "hasSidewalk", "append": [0, 2] },
    { "if": "true",        "append": [4] }
]
```

**Loop**: an object with `for` (`"<variable>; <start>; <end>"` or `"<variable>; <start>; <end>; <step>"`), a `value` expression evaluated for each iteration and an optional `if` condition evaluated for each iteration. The loop runs from `start` (included) to `end` (excluded); `start`, `end` and `step` are expressions, truncated to integers. When `start > end` the loop counts down and **the step must be negative** (the default step is 1, which would never end). The loop variable must be a float variable of the type, normally declared in `loopVariables`.

```json
"lanes": {
    "for": "i; 0; forwardLanes",
    "value": "(i+0.5)/totLanes"
}
```

A list mixing plain values and objects is not allowed: all elements must be values, or all must be conditional blocks.
