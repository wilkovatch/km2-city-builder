shader_type spatial;
// Grid shader:
// Blend SrcAlpha OneMinusSrcAlpha, ZWrite Off, world-space XZ grid.
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;

uniform vec4 _Size = vec4(4096.0, -2048.0, 4096.0, -2048.0); // kept so existing scripts can still set it
uniform vec4 _Colour : source_color = vec4(0.5, 0.5, 0.5, 0.5);
uniform vec4 _Colour2 : source_color = vec4(1.0, 0.0, 0.0, 0.8);
uniform vec3 _Center = vec3(0.0);
uniform float _Radius = 0.1;
uniform float _Spacing = 0.1;   // second grid:  lines every 1 / _Spacing  units (10)
uniform float _Spacing2 = 0.01; // third grid:   lines every 100 units
uniform float _Spacing3 = 0.001;// fourth grid:  lines every 1000 units
uniform float _Thickness = 0.5;
uniform float _YOffset = -0.16; // the vertex is shifted down by 0.16 in object space

varying vec3 v_world;

// Derived from code by Evan Wallace: http://madebyevan.com/shaders/grid/
// No distance fade: dense grids must saturate into an even haze, fading them
// leaves darker moire bands that look like lighting on the terrain.
float grid(vec2 coord) {
	vec2 fw = max(fwidth(coord), vec2(0.00001));
	vec2 g = abs(fract(coord - 0.5) - 0.5) / fw;
	float lin = min(g.x, g.y);
	return 1.0 - min(lin, 1.0);
}

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX.y += _YOffset;
}

void fragment() {
	vec2 p = v_world.xz;
	float c  = grid(p);
	float c2 = grid(p * _Spacing);
	float c3 = grid(p * _Spacing2);
	float c4 = grid(p * _Spacing3);

	vec4 grid_col = _Colour;
	grid_col.a = min((c + c2 + c3 + c4) * _Thickness, 1.0);

	vec4 circle = _Colour2;
	vec2 d = v_world.xz - _Center.xz;
	if (dot(d, d) > _Radius * _Radius) {
		circle = vec4(0.0);
	}

	vec4 col = clamp(grid_col + circle, 0.0, 1.0);
	ALBEDO = col.rgb;
	ALPHA = col.a;
}
