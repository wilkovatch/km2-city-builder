-- Synthetic cubic curve for the shared evaluator/road integration tests. No game assets.
local M = {}
local function copy(p) local r={} for i,v in ipairs(p) do r[i]=v end return r end
local function blend(p,t)
    local r=copy(p)
    for n=#r-1,1,-1 do for i=1,n do r[i]=r[i]*(1-t)+r[i+1]*t end end
    return r[1]
end
local function divide(p,t)
    local r,a,b=copy(p),{p[1]},{p[#p]}
    for n=#r-1,1,-1 do
        for i=1,n do r[i]=r[i]*(1-t)+r[i+1]*t end
        a[#a+1]=r[1]; table.insert(b,1,r[n])
    end
    return a,b
end
local function pack(p)
    local r={} for _,v in ipairs(p) do r[#r+1]=tostring(v.x); r[#r+1]=tostring(v.y); r[#r+1]=tostring(v.z) end
    return table.concat(r,",")
end
local function unpack(s)
    local r,v={},{} for n in string.gmatch(s,"[^,]+") do v[#v+1]=tonumber(n) end
    for i=1,#v,3 do r[#r+1]=vec3(v[i],v[i+1],v[i+2]) end return r
end
function M.prepare(_,c)
    local p=copy(c.points)
    if #p>2 and c.startTangent then p[2]=p[1]+c.startTangent/(#p-1) end
    if #p>2 and c.endTangent then p[#p-1]=p[#p]-c.endTangent/(#p-1) end
    return p
end
function M.controlParameters(_,c)
    local r={} for i=1,#c.points do r[i]=(i-1)/math.max(1,#c.points-1) end return r
end
function M.evaluate(_,c)
    local p=c.data
    if #p==0 then return {position=vec3(0,0,0)} end
    local d={} for i=1,#p-1 do d[i]=(p[i+1]-p[i])*(#p-1) end
    return {position=blend(p,c.t),tangent=#d>0 and blend(d,c.t) or vec3(0,0,0)}
end
function M.interpolateValues(_,c) return blend(c.values,c.t) end
function M.fail() error("synthetic curve failure") end
function M.invalid() return {position=vec3(0/0,0,0)} end
function M.split(_,c)
    local p,r=c.road.points,c.road.rotations
    local a,b=divide(p,c.t); local ar,br=divide(r,c.t)
    local original=c.road.state.originalCurve
    if not original or original == "" then original=pack(p) end
    local originalRotations=c.road.state.originalRotations
    if not originalRotations or originalRotations == "" then
        local values={} for i,v in ipairs(r) do values[i]=vec3(v.x,v.y,0) end
        originalRotations=pack(values)
    end
    local function result(points,rotations,first)
        local sources={} for i=1,#points do sources[i]=false end
        if first then sources[1]={road=1,index=1} else sources[#points]={road=1,index=#p} end
        return {points=points,rotations=rotations,sources=sources,state={originalCurve=original,originalRotations=originalRotations}}
    end
    return {first=result(a,ar,true),second=result(b,br,false)}
end
function M.merge(_,c)
    if not c.first.state.originalCurve then error("only split parts can be joined exactly by this fixture") end
    local p=unpack(c.first.state.originalCurve)
    local expectedStart=c.keepAtEnd and c.first.points[1] or (c.otherAtStart and c.second.points[#c.second.points] or c.second.points[1])
    local original=unpack(c.first.state.originalRotations)
    local r={} for i,v in ipairs(original) do r[i]=vec2(v.x,v.y) end
    if p[1]:distance(expectedStart)>0.01 then
        local rev,rr={},{}
        for i=#p,1,-1 do rev[#rev+1]=p[i]; rr[#rr+1]=vec2(r[i].x,-r[i].y) end
        p,r=rev,rr
    end
    local sources={} for i=1,#p do sources[i]=false end
    sources[1]=c.keepAtEnd and {road=1,index=1} or {road=2,index=c.otherAtStart and #c.second.points or 1}
    sources[#p]=c.keepAtEnd and {road=2,index=c.otherAtStart and #c.second.points or 1} or {road=1,index=#c.first.points}
    return {points=p,rotations=r,sources=sources}
end
return M
