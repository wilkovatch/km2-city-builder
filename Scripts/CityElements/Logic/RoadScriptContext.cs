using System.Collections.Generic;
using Lua;
using RuntimeCalculator.Scripting;
using GO = GameEngine.GameObject;

//The context given to the scripts of the definitions of a road (their second argument): read-only access to its
//intersections and to the other roads connected to them
[LuaObject("road")]
public partial class RoadScriptContext {
    readonly Road road;

    public RoadScriptContext(Road road) {
        this.road = road;
    }

    [LuaMember("typeName")] public string TypeName => road.state?.Str("type", "") ?? "";

    //the intersection at the "start" or at the "end" of the road, nil if there is none
    [LuaMember("intersection")]
    public LuaValue Intersection(string end) {
        Intersection intersection;
        if (end == "start") intersection = road.startIntersection;
        else if (end == "end") intersection = road.endIntersection;
        else throw new System.ArgumentException("intersection(end): end must be \"start\" or \"end\", not \"" + end + "\"");
        if (intersection == null) return LuaValue.Nil;
        return new IntersectionScriptView(road, intersection);
    }

    public static LuaValue GetVariables(Road road) {
        return LuaVariableView.Get(road?.generator?.subGen?.variableContainer);
    }
}

//An intersection seen from one of its roads: its roads are sorted clockwise
[LuaObject("intersection")]
public partial class IntersectionScriptView {
    readonly Road self;
    readonly Intersection intersection;
    readonly List<Road> roads;

    public IntersectionScriptView(Road self, Intersection intersection) {
        this.self = self;
        this.intersection = intersection;
        roads = intersection.GetRoadsSortedClockwise();
    }

    [LuaMember("position")] public LuaVec3 Position => new LuaVec3(GO.GetPosition(intersection.point));
    [LuaMember("roadCount")] public int RoadCount => roads.Count;
    //1-based position of the road itself in the clockwise order
    [LuaMember("selfIndex")] public int SelfIndex => roads.IndexOf(self) + 1;

    //the road at the given 1-based position in the clockwise order (wrapped around: 0 is the last one, roadCount + 1 the first one)
    [LuaMember("road")]
    public RoadScriptView Road(int index) {
        var n = roads.Count;
        var i = ((index - 1) % n + n) % n;
        return new RoadScriptView(roads[i], intersection, roads[i] == self);
    }
}

//A road of an intersection: its variables (as left by its last rebuild) and which of its ends is at the intersection
[LuaObject("roadView")]
public partial class RoadScriptView {
    readonly Road road;
    readonly bool atStart;
    readonly bool isSelf;

    public RoadScriptView(Road road, Intersection intersection, bool isSelf) {
        this.road = road;
        atStart = road.startIntersection == intersection;
        this.isSelf = isSelf;
    }

    [LuaMember("vars")] public LuaValue Vars => RoadScriptContext.GetVariables(road);
    [LuaMember("atStart")] public bool AtStart => atStart;
    [LuaMember("isSelf")] public bool IsSelf => isSelf;
    [LuaMember("typeName")] public string TypeName => road.state?.Str("type", "") ?? "";
}
