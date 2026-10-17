shader_type spatial;
// Ground overlay shader:
// Blend SrcAlpha OneMinusSrcAlpha, ZWrite Off, unlit, the texture is mapped over the terrain in world space.
// On the terrain it is replaced by MapTerrain.gdshader, which takes these parameters (see GameEngine.Terrain).
render_mode unshaded, blend_mix, depth_draw_never;

uniform sampler2D _MainTex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec4 _Size = vec4(4096.0, -2048.0, 4096.0, -2048.0);
uniform float _Spacing = 0.1;   // second grid:  lines every 1 / _Spacing  units (10)
uniform float _Spacing2 = 0.01; // third grid:   lines every 100 units
uniform float _Spacing3 = 0.001;// fourth grid:  lines every 1000 units
uniform float _Thickness = 0.75;
uniform float _MapOverlay = 0.5;
uniform float _Opacity = 1.0;
uniform float _YOffset = -0.16; // the vertex is shifted down by 0.16 in object space

varying vec3 v_world;

// Derived from code by Evan Wallace: http://madebyevan.com/shaders/grid/
// Fades out when cells shrink below a few pixels so dense lines can't moire into a checkerboard.
float grid(vec2 coord) {
	vec2 fw = max(fwidth(coord), vec2(0.00001));
	vec2 g = abs(fract(coord - 0.5) - 0.5) / fw;
	float lin = min(g.x, g.y);
	float line = 1.0 - min(lin, 1.0);
	float fade = 1.0 - smoothstep(0.15, 0.5, max(fw.x, fw.y));
	return line * fade;
}

// The map is (world.xz + _Size.yw) / _Size.xz with v pointing to program +Z (Godot -Z). Loaded textures
// are stored bottom row first (see GameEngine.Texture.CreateFromFile), so v is the same.
vec2 map_uv(vec3 world) {
	return vec2(world.x + _Size.y, _Size.w - world.z) / _Size.xz;
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

	ALBEDO = texture(_MainTex, map_uv(v_world)).rgb;
	ALPHA = clamp(max((c + c2 + c3 + c4) * _Thickness, _MapOverlay) * _Opacity, 0.0, 1.0);
}
