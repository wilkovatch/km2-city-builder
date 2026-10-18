"""Vector and polygon helpers for the scripts, the same as the ones the editor uses (points are [x, y, z] lists, the
polygons are on the ground plane x, z)."""

import math


def add(a, b):
    return [a[0] + b[0], a[1] + b[1], a[2] + b[2]]


def sub(a, b):
    return [a[0] - b[0], a[1] - b[1], a[2] - b[2]]


def mul(a, f):
    return [a[0] * f, a[1] * f, a[2] * f]


def length(a):
    return math.sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2])


def distance(a, b):
    return length(sub(a, b))


def normalize(a):
    n = length(a)
    return [0.0, 0.0, 0.0] if n < 1e-5 else mul(a, 1.0 / n)


def lerp(a, b, t):
    return add(a, mul(sub(b, a), t))


def _ray_intersects_segment(point, p1, p2):
    #ray from point towards +x, on the x, z plane
    v1 = (point[0] - p1[0], point[1] - p1[1])
    v2 = (p2[0] - p1[0], p2[1] - p1[1])
    v3 = (0.0, 1.0)
    dot = v2[0] * v3[0] + v2[1] * v3[1]
    if abs(dot) < 0.0001:
        return False
    t1 = (v2[0] * v1[1] - v2[1] * v1[0]) / dot
    t2 = (v1[0] * v3[0] + v1[1] * v3[1]) / dot
    return t1 >= 0 and 0 <= t2 < 1


def is_point_inside_polygon(point, polygon):
    """Whether the point is inside the polygon (on the x, z plane)."""
    count = 0
    p = (point[0], point[2])
    for i in range(len(polygon)):
        a = polygon[i]
        b = polygon[(i + 1) % len(polygon)]
        if _ray_intersects_segment(p, (a[0], a[2]), (b[0], b[2])):
            count += 1
    return count % 2 != 0


def is_polygon_clockwise(polygon):
    total = 0.0
    for i in range(len(polygon)):
        a = polygon[i]
        b = polygon[(i + 1) % len(polygon)]
        total += (b[0] - a[0]) * (b[2] + a[2])
    return total > 0


def outer_polygon(polygon, dist):
    """The polygon moved outwards by the distance (each point along the mean of the normals of its two sides)."""
    mult = 1.0 if is_polygon_clockwise(polygon) else -1.0
    res = []
    n = len(polygon)
    for i in range(n):
        p0 = polygon[i - 1]
        p1 = polygon[i]
        p2 = polygon[(i + 1) % n]
        d1 = sub(p2, p1)
        d2 = sub(p1, p0)
        #cross product with the up vector
        n1 = [-d1[2], 0.0, d1[0]]
        n2 = [-d2[2], 0.0, d2[0]]
        normal = mul(normalize(mul(add(n1, n2), 0.5)), mult)
        res.append(add(p1, mul(normal, dist)))
    return res
