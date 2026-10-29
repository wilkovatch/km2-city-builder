# Core Lua Scripts

A definition can be computed by a function of a Lua script instead of an [expression](Expression-Language). Use scripts for logic that is too involved for an expression: loops, local variables, helper functions shared by several types, or values that depend on other elements (for example the MM2 traffic lights, which can stand on a corner of another road of the intersection).

The scripts run inside the program (Lua 5.2, through the [Lua-CSharp](https://github.com/nuskey8/Lua-CSharp) interpreter), so a call takes about a microsecond. They are **pure functions**: they read the variables of an element and return a value. They can't change anything, nor access files, the operating system or the scene.

## Files

The scripts are in the `lua` folder of the core. Each file is a module that returns a table of functions:

```
<CoreName>/lua/
├── trafficLight.lua      module "trafficLight"
└── lib/
    └── geometry.lua      module "lib.geometry"
```

```lua
local geometry = require("lib.geometry")   -- shared helpers: other modules of the lua folder

local M = {}

function M.startPosition(v, ctx)
    return geometry.corner(v.startTrafficLightPosition, v.startDir)
end

return M
```

`require` only finds the modules of the `lua` folder of the core (`a.b` is the file `lua/a/b.lua`). Each module is loaded once per Lua state and shared by the types using that state. Large low-level geometry rebuilds use isolated worker states; keep geometry functions pure and do not retain data from individual elements in module variables. The scripts are read again when a city is loaded.

## Script definitions

Any definition (static, dynamic, late, or local to a component, in any element type) can use `script` instead of `value`. Its value is the function `<module>.<function>`:

```json
{ "name": "startTrafficLightBasePosition", "type": "vec3", "script": "trafficLight.startPosition" }
```

The function is called with two arguments, `v` (the variables) and `ctx` (the context of the element, see below), and returns the value of the definition:

| Definition type | Lua result |
|---|---|
| `float`, `int` | number (not rounded for `int`, as with expressions) |
| `bool` | boolean (`nil` or a number are errors: in Lua `0` is true) |
| `vec3` | `vec3` |
| `vec2` | `vec2` |

A script definition is evaluated at the same moments and in the same order as an expression would be (see [Expression Language](Expression-Language#definitions)): its result becomes a variable that the following definitions and expressions can use.

## Variables (`v`)

`v.<name>` reads any variable that an expression could use at that moment: parameters, built-in variables of the element and definitions evaluated before.

| Variable | Lua value |
|---|---|
| `float`, `int`, `enum` parameters and definitions, numeric built-ins | number |
| `bool` parameters and definitions, boolean built-ins (`hasStartIntersection`, `throughIntersection`, ...) | boolean |
| `vec3`, `vec2` | `vec3`, `vec2` |

`v` is read-only: assigning a field is an error, and so is reading an unknown name, so typos don't go unnoticed.

## Context (`ctx`)

`ctx` gives read-only access to the surroundings of the element. It is `nil` for the elements that don't provide one.

**Roads**:

| Member | Description |
|---|---|
| `ctx.typeName` | The road type. |
| `ctx:intersection(end)` | The intersection at the `"start"` or at the `"end"` of the road, `nil` if there is none. |

**Intersection** (as seen from the road):

| Member | Description |
|---|---|
| `position` | The center (`vec3`). |
| `roadCount` | Number of roads. |
| `selfIndex` | Position of the road itself among the roads sorted clockwise (1-based). |
| `road(i)` | The road at position `i` in the clockwise order. The index wraps around: `road(selfIndex + 1)` is the next road clockwise, `road(selfIndex - 1)` the previous one. |

**Road of an intersection**:

| Member | Description |
|---|---|
| `vars` | Its variables, like `v`. |
| `atStart` | `true` if its start is at the intersection, `false` if its end is. |
| `isSelf` | `true` for the road that called the script. |
| `typeName` | Its road type. |

The variables of another road are the values left by its last rebuild. All the roads are rebuilt before the parametric objects are placed, so the definitions read by the hooks of an `objectInstance` parameter see up-to-date neighbours. Static, dynamic and `common` late definitions, evaluated while the road itself is being rebuilt, may still see the previous values of a neighbour that is rebuilt later. Put the definitions that read other roads in a late category other than `common`: those are only evaluated when a hook reads them (see [Core Props and Objects](Core-Props-and-Objects#parametric-objects)).

## Vectors

`vec3(x, y, z)` and `vec2(x, y)` create vectors. Their components (`a.x`, `a.y`, `a.z`) are read-only.

Operators: `a + b`, `a - b`, `-a`, `a * n`, `n * a`, `a / n` (`n` a number), `a == b`, `tostring(a)`.

Methods, with the same results as the expression functions:

| `vec3` method | Expression | `vec2` method | Expression |
|---|---|---|---|
| `a:dot(b)` | `dot(a, b)` | `a:dot(b)` | `dot2(a, b)` |
| `a:cross(b)` | `cross(a, b)` | | |
| `a:magnitude()` | `magnitude(a)` | `a:magnitude()` | |
| `a:normalize()` | `normalize(a)` | `a:normalize()` | |
| `a:distance(b)` | `distance(a, b)` | `a:distance(b)` | `distance2(a, b)` |
| `a:angle(b)` | `angle(a, b)` | `a:angle(b)` | `angle2(a, b)` |
| `a:signedAngle(b, axis)` | `signedAngle(a, b, axis)` | `a:signedAngle(b)` | `signedAngle2(a, b)` |
| `a:scale(b)` | `scale(a, b)` | `a:scale(b)` | `scale(a, b)` |
| `a:min(b)`, `a:max(b)` | `min(a, b)`, `max(a, b)` | `a:min(b)`, `a:max(b)` | `min(a, b)`, `max(a, b)` |
| `a:project(b)` | `project(a, b)` | | |
| `a:reflect(b)` | `reflect(a, b)` | `a:reflect(b)` | `reflect(a, b)` |
| `a:lerp(b, t)` | `lerp(a, b, t)` | `a:lerp(b, t)` | `lerp(a, b, t)` |
| `a:rotate(axis, angle)` | `rotate(a, axis, angle)` | | |

Angles are in degrees. `rnd(min, max, seed)` is the deterministic random function of the expressions (without a seed it is not deterministic).

## Available libraries

The basic functions (without `dofile`, `loadfile` and `collectgarbage`), `math` (without `math.random` and `math.randomseed`: a definition must give the same result every time it is evaluated, use `rnd` instead), `string`, `table`, `bit32` and `require`. There is no `io`, `os`, `debug` or `coroutine`. `print` writes to the log of the program, prefixed by `[lua]`.

## Errors

* A missing module or function, or a syntax error, is reported when the types are read (when the city is loaded), like an invalid expression.
* An error while a script runs, or a result of the wrong type, is logged once per definition (with the line of the script). The definition then takes the default value: 0, `false` or a zero vector.
* A call that runs for too long (about 20 million instructions, for example an endless loop) is stopped with an error.

## Example: the MM2 traffic lights

Each road type describes the corners where its lights can stand, with expressions in its `common` late definitions:

```json
{ "name": "startTrafficLightPosition", "type": "vec3", "value": "if(swapTrafficLights, v0_0, v6_0)" },
{ "name": "invertedStartTrafficLightPosition", "type": "vec3", "value": "if(swapTrafficLights, v6_0, v0_0)" }
```

The logic shared by all the road types is in `lua/trafficLight.lua`, referenced by script definitions. The condition and the model index are static definitions; the position and the rotation are in the late category `trafficLights`, evaluated only when the light is placed:

```json
"staticDefinitions": [
    { "name": "startTrafficLightCondition", "type": "bool", "script": "trafficLight.startCondition" },
    { "name": "startTrafficLight_meshIndex", "type": "int", "script": "trafficLight.startMeshIndex" }
],
"lateDefinitions": [
    { "category": "trafficLights", "definitions": [
        { "name": "startTrafficLightBasePosition", "type": "vec3", "script": "trafficLight.startPosition" },
        { "name": "startTrafficLightBaseRotation", "type": "vec3", "script": "trafficLight.startRotation" }
    ] }
]
```

The `startTrafficLight` parameter points its hooks (`condition`, `defaultModel.index`, `basePosition`, `baseRotation`) at these definitions. With "traffic lights on the opposite road", the script looks for the road of the intersection with the corner nearest to the point across the intersection:

```lua
local function corners(vars, side)
    return vars[side .. "TrafficLightPosition"], vars["inverted" .. capitalized(side) .. "TrafficLightPosition"]
end

-- ...
for i = 1, intersection.roadCount do
    local road = intersection:road(i)
    if not road.isSelf then
        local a, b = corners(road.vars, road.atStart and "start" or "end")
        local score = 1 / math.min(target:distance(a), target:distance(b))
        if score > bestScore then
            bestScore = score
            chosen = i
        end
    end
end
```
