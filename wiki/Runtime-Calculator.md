# Runtime Calculator

The runtime calculator (`Scripts/RuntimeCalculator`, namespace `RuntimeCalculator`) parses the expressions written in the core definitions into trees of small calculator objects, and evaluates them quickly many times (once per road section, per prop, per vertex...). The language itself is documented for core authors in [Expression Language](Expression-Language).

## Parsing

The grammar is written for ANTLR 4 (`ExpressionLexer.g4` and `ExpressionParser.g4` in the `ExpressionParser` folder at the repository root). The generated C# lexer, parser and visitor base classes go in `CustomPackages/expressions-antlr4` and use the `Antlr4.Runtime.Standard` NuGet package.

The generated files are not tracked by git, so they must be generated after cloning the repository, and again after every grammar change: put `antlr-4.13.2-complete.jar` (from the [ANTLR download page](https://www.antlr.org/download/)) in the `ExpressionParser` folder and run `compile.bat` there (Java 11 or later required). The script generates the four files and copies them to `CustomPackages/expressions-antlr4`. On other systems, run the `java` command from the script and copy the generated `.cs` files by hand.

There are four entry rules (`mainFloatExpr`, `mainBoolExpr`, `mainVec3Expr`, `mainVec2Expr`), one per result type, and one visitor per type in `Scripts/RuntimeCalculator/Parsers`:

| Class | Entry point | Returns |
|---|---|---|
| `FloatParser` | `ParseExpression(string)` | `Numbers.Number` |
| `BooleanParser` | `ParseExpression(string)` | `Booleans.Boolean` |
| `Vector3Parser` | `ParseExpression(string)` | `Vector3s.Vector` |
| `Vector2Parser` | `ParseExpression(string)` | `Vector2s.Vector` |

The visitors call each other for mixed expressions (a comparison inside an `if`, a vector function returning a number, ...). Parse errors are rethrown with the expression text appended. Constant sub-trees are folded at parse time (`GetSimplified`).

## Calculator objects

All calculators implement `GenericCalculator<T>` (`GetValue(VariableContainer)` and `SetIndices(VariableContainer)`).

| Namespace / file | Classes |
|---|---|
| `Numbers` (`Numbers.cs`) | `Constant` (literals and `pi`, `deg2rad`, `rad2deg`), `Variable`, `Function` (`sin`, `cos`, `tan`, `min`, `max`, `sign`, `abs`, `clamp`, `ceil`, `floor`, `round`, `sqrt`, `lerp`, `rnd`), `Vec3Function` (`dot`, `angle`, `signedAngle`, `magnitude`, `distance`, `v3x`, `v3y`, `v3z`), `Vec2Function` (`dot2`, `angle2`, `signedAngle2`, `distance2`, `v2x`, `v2y`), `IfFunction`, and the operations `Sum`, `Subtraction`, `Multiplication`, `Division`, `Modulo`, `Power`. |
| `Booleans` (`Booleans.cs`) | `BooleanConstant` (`true`, `false`), `BooleanVariable` (a float variable read as `> 0`), `Not`, `And`, `Or`, `Xor`, and the comparisons `GT`, `GTE`, `LT`, `LTE`, `EQ` (`!=` is `Not(EQ)`). |
| `Vector3s` (`Vector3s.cs`) | `Constant`, `Compound` (`vec3(x, y, z)`), `Variable`, `Function` (`cross`, `scale`, `normalize`, `min`, `max`, `project`, `reflect`), `IfFunction`, `Lerp`, `Rotate`, and `Sum`, `Subtraction`, `Multiplication` and `Division` (by a number). |
| `Vector2s` (`Vector2s.cs`) | Same as `Vector3s` for 2D vectors: `Function` (`scale`, `min`, `max`, `reflect`), `Rotate` (by an angle in radians). |

`Operation` (numbers), `BooleanOperation`, `Comparison`, `VecOperation` and `FloatOperation` (vectors) are the abstract bases of the binary operators.

Every math function goes through `GameEngine.Math`/`Vector2`/`Vector3` (see [Engine Abstraction](Engine-Abstraction)).

## Variables

### `VariableContainer`

Holds the values of all variables of one runtime type in three arrays (`floats`, `vector3s`, `vector2s`) plus three name → index dictionaries (`floatIndex`, `vec3Index`, `vec2Index`). Booleans, integers and enums are stored as floats (true = 1, false = 0).

* The constructor takes the list of `(name, type)` pairs and assigns the indices.
* `GetClone()` creates a container with the same layout and fresh values; generators use clones so that elements of the same type do not share values.
* `SetFloat/SetVector3/SetVector2` set a value by name or by index.
* `boolNames` (shared by the clones) lists the float variables that are booleans, `owner` is the element using the container and `scriptView` the cached view of the Lua scripts (see below); these two are not cloned.

### Index resolution

`Variable` calculators store a name when parsed. `SetIndices(container)` looks the name up once and stores the index, so evaluation is just an array access. Runtime types call `SetIndices` on every expression right after creating their container: an unknown variable name raises `Invalid float parameter: <name>` at that point, which makes the core fail to load instead of failing later during generation.

Because indices are resolved against a specific layout, an expression can only be evaluated with containers that share that layout (the type's container or one of its clones). The junction section vertices, for example, use their own small container (`JunctionType.sectionVerticesVS`).

## Lua scripts

`Scripts/RuntimeCalculator/Scripting` (namespace `RuntimeCalculator.Scripting`) runs the Lua scripts of the core (its `lua` folder, see [Core Lua Scripts](Core-Lua-Scripts)) with the `LuaCSharp` package.

| Class | Role |
|---|---|
| `LuaScriptHost` | One Lua state per core (`Get()`), created when the first type that uses a script is read, and replaced when a city is loaded (`Reset()`, the types are read again then). Opens a sandboxed set of libraries; `require` only loads the modules of the `lua` folder (`CoreModuleLoader`). `Resolve("<module>.<function>")` loads the module and returns the function, throwing when the types are read if it is missing or does not compile. `Call` runs a function synchronously (no asynchronous function is available to the scripts), stops it after about 20 million instructions (count hook), and logs each error once per definition, returning nil. |
| `ScriptNumber`, `ScriptBoolean`, `ScriptVector3`, `ScriptVector2` | The calculators of the script definitions (subclasses of `Numbers.Number`, `Booleans.Boolean`, `Vector3s.Vector`, `Vector2s.Vector`, never constant). `GetValue(vc)` calls the function with the variables and the context and checks the type of the result (the default value if it is wrong). |
| `LuaVariableView` | The read-only `v` table of a container: its `__index` reads the variables by name (booleans for the names in `boolNames`), cached in the container (`scriptView`). |
| `LuaVec3`, `LuaVec2` | The vectors of the scripts (`[LuaObject]` classes generated by the package), with the same functions as the expressions. `LuaVectors.Register` adds the `vec3`, `vec2` and `rnd` globals. |
| `IScriptContextProvider` | Implemented by the elements that give their scripts a context (`ctx`): the `owner` of a container (set by the element on the container of its generator). Roads give a `RoadScriptContext`. |

Definitions, constraints and derived points run on the main thread. Large custom element geometry rebuilds use isolated worker hosts (see [Geometry Generation](Geometry-Generation#custom-elements)); a Lua state is never accessed by two threads at once. Worker hosts buffer output and errors for the main thread and are disposed after the batch pass. `LuaVectorBindings` gives each execution thread its own vector metatables: Lua's metamethod lookups can populate table entries, so sharing a generated static metatable would be unsafe even for vectors whose values are read-only.

## Known limitations

* A unary minus applies only to a number, a variable or a parenthesized expression: `-sin(x)` is a syntax error, write `-(sin(x))` or `0 - sin(x)`.
* `lerp` is a lexer token of its own (`LERP`), so the grammar has explicit `lerp` rules for floats, `vec3` and `vec2` instead of handling it through `func`. A new function that has to work on more than one type needs the same treatment.

### Parser notes

* The lexer skips whitespace (`WS` rule). Spaces are never reported as token errors.
* `FloatParser.VisitVariable` matches the constant names exactly. Identifiers that merely *contain* `pi`, `deg2rad` or `rad2deg` (such as `spin`) are variables.
* `lerp` works on floats. It is listed in `Numbers.Function` and reachable from the grammar.
