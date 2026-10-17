// Copyright © 2025 Cory Petkovsek, Roope Palmroos, and Contributors.
// This shader is the minimum needed to allow the terrain to function, without any texturing.
// Fragment replaced with the ground overlay shader (Materials/Default.gdshader):
// Blend SrcAlpha OneMinusSrcAlpha, ZWrite Off, unlit, so the overlay texture is drawn over the ground with the grid lines.

shader_type spatial;
render_mode unshaded,blend_mix,depth_draw_never,cull_back,skip_vertex_transform;

// Defined Constants
#define SKIP_PASS 0
#define VERTEX_PASS 1
#define FRAGMENT_PASS 2

#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
    #define fma(a, b, c) ((a) * (b) + (c))
    #define dFdxCoarse(a) dFdx(a)
    #define dFdyCoarse(a) dFdy(a)
#endif

// Private uniforms
// Commented uniforms aren't needed for this shader, but are available for your own needs.
uniform vec3 _camera_pos = vec3(0.f);
uniform float _mesh_size = 48.f;
uniform uint _background_mode = 1u; // NONE = 0, FLAT = 1, NOISE = 2
uniform uint _mouse_layer = 0x80000000u; // Layer 32
uniform float _vertex_spacing = 1.0;
uniform float _vertex_density = 1.0; // = 1/_vertex_spacing
uniform float _region_size = 1024.0;
uniform float _region_texel_size = 0.0009765625; // = 1/1024
uniform int _region_map_size = 32;
uniform int _region_map[1024];
//uniform vec2 _region_locations[1024];
//uniform float _texture_uv_scale_array[32];
//uniform float _texture_detile_array[32];
//uniform vec4 _texture_color_array[32];
uniform highp sampler2DArray _height_maps : repeat_disable;
uniform highp sampler2DArray _control_maps : repeat_disable;
//uniform highp sampler2DArray _color_maps : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
//uniform highp sampler2DArray _texture_array_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
//uniform highp sampler2DArray _texture_array_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;

// Public Uniforms
uniform bool flat_terrain_normals = false;

// Varyings & Types
// Some are required for editor functions
varying float v_vertex_xz_dist;
varying vec3 v_vertex;

// Same parameters as Materials/Default.gdshader without the leading underscore (Terrain3D treats
// "_" uniforms as private and won't set them), copied over by GameEngine.Terrain.
uniform sampler2D MainTex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec4 Size = vec4(4096.0, -2048.0, 4096.0, -2048.0);
uniform float Spacing = 0.1;   // second grid:  lines every 1 / Spacing  units (10)
uniform float Spacing2 = 0.01; // third grid:   lines every 100 units
uniform float Spacing3 = 0.001;// fourth grid:  lines every 1000 units
uniform float Thickness = 0.75;
uniform float MapOverlay = 0.5;
uniform float Opacity = 1.0;
// Heightmap extent in world XZ (min x, min z, max x, max z), set by GameEngine.Terrain. Terrain3D
// regions are a fixed size, so they overhang the heightmap and must be clipped at the terrain edge.
uniform vec4 TerrainBounds = vec4(-1e20, -1e20, 1e20, 1e20);
uniform float YOffset = -0.16; // the overlay is drawn 0.16 below the surface so road meshes stay on top

////////////////////////
// Vertex
////////////////////////

// Takes in world space XZ (UV) coordinates & search depth (only applicable for background mode none)
// Returns ivec3 with:
// XY: (0 to _region_size - 1) coordinates within a region
// Z: layer index used for texturearrays, -1 if not in a region
ivec3 get_index_coord(const vec2 uv, const int search) {
	vec2 r_uv = round(uv);
	vec2 o_uv = mod(r_uv,_region_size);
	ivec2 pos;
	int bounds, layer_index = -1;
	for (int i = -1; i < clamp(search, SKIP_PASS, FRAGMENT_PASS); i++) {
		if ((layer_index == -1 && _background_mode == 0u ) || i < 0) {
			r_uv -= i == -1 ? vec2(0.0) : vec2(float(o_uv.x <= o_uv.y), float(o_uv.y <= o_uv.x));
			pos = ivec2(floor((r_uv) * _region_texel_size)) + (_region_map_size / 2);
			bounds = int(uint(pos.x | pos.y) < uint(_region_map_size));
			layer_index = (_region_map[ pos.y * _region_map_size + pos.x ] * bounds - 1);
		}
	}
	return ivec3(ivec2(mod(r_uv,_region_size)), layer_index);
}

void vertex() {
	// Get vertex of flat plane in world coordinates and set world UV
	v_vertex = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

	// Camera distance to vertex on flat plane
	v_vertex_xz_dist = length(v_vertex.xz - _camera_pos.xz);

	// Geomorph vertex, set end and start for linear height interpolate
	float scale = MODEL_MATRIX[0][0];
	float vertex_lerp = smoothstep(0.55, 0.95, (v_vertex_xz_dist / scale - _mesh_size - 4.0) / (_mesh_size - 2.0));
	vec2 v_fract = fract(VERTEX.xz * 0.5) * 2.0;
	// For LOD0 morph from a regular grid to an alternating grid to align with LOD1+
	vec2 shift = (scale < _vertex_spacing + 1e-6) ? // LOD0 or not
		// Shift from regular to symetric
		mix(v_fract, vec2(v_fract.x, -v_fract.y),
			round(fract(round(mod(v_vertex.z * _vertex_density, 4.0)) *
			round(mod(v_vertex.x * _vertex_density, 4.0)) * 0.25))
			) :
		// Symetric shift
		v_fract * round((fract(v_vertex.xz * 0.25 / scale) - 0.5) * 4.0);
	vec2 start_pos = v_vertex.xz * _vertex_density;
	vec2 end_pos = (v_vertex.xz - shift * scale) * _vertex_density;
	v_vertex.xz -= shift * scale * vertex_lerp;

	// UV coordinates in world space. Values are 0 to _region_size within regions
	UV = v_vertex.xz * _vertex_density;

	// UV coordinates in region space + texel offset. Values are 0 to 1 within regions
	UV2 = fma(UV, vec2(_region_texel_size), vec2(0.5 * _region_texel_size));

	// Discard vertices for Holes. 1 lookup
	ivec3 v_region = get_index_coord(start_pos, VERTEX_PASS);
	uint control = floatBitsToUint(texelFetch(_control_maps, v_region, 0)).r;
	bool hole = bool(control >>2u & 0x1u);

	// Show holes to all cameras except mouse camera (on exactly 1 layer)
	if ( !(CAMERA_VISIBLE_LAYERS == _mouse_layer) &&
			(hole || (_background_mode == 0u && v_region.z < 0))) {
		v_vertex.x = 0. / 0.;
	} else {
		// Interpolate Geomorph Start & End, set height. 2 Lookups.
		ivec3 uv_a = get_index_coord(start_pos, VERTEX_PASS);
		ivec3 uv_b = get_index_coord(end_pos, VERTEX_PASS);
		float h = mix(texelFetch(_height_maps, uv_a, 0).r, texelFetch(_height_maps, uv_b, 0).r, vertex_lerp);
		v_vertex.y = h;
	}
	vec3 grid_vertex = v_vertex;
	grid_vertex.y += YOffset;

	// Convert model space to view space w/ skip_vertex_transform render mode
	VERTEX = (VIEW_MATRIX * vec4(grid_vertex, 1.0)).xyz;
	NORMAL = normalize((MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	BINORMAL = normalize((MODELVIEW_MATRIX * vec4(BINORMAL, 0.0)).xyz);
	TANGENT = normalize((MODELVIEW_MATRIX * vec4(TANGENT, 0.0)).xyz);
}

////////////////////////
// Map helpers
////////////////////////

// Evan Wallace style antialiased grid line in cell units.
// Fades out once cells get smaller than ~2-6px so dense lines never moire into a checker pattern.
float grid_level(vec2 coord) {
	vec2 fw = max(fwidth(coord), vec2(0.00001));
	vec2 d = abs(fract(coord - 0.5) - 0.5) / fw;
	float line = 1.0 - min(min(d.x, d.y), 1.0);
	float fade = 1.0 - smoothstep(0.15, 0.5, max(fw.x, fw.y));
	return line * fade;
}

// The map is (world.xz + _Size.yw) / _Size.xz with v pointing to program +Z (Godot -Z). Loaded textures
// are stored bottom row first (see GameEngine.Texture.CreateFromFile), so v is the same.
vec2 map_uv(vec3 world) {
	return vec2(world.x + Size.y, Size.w - world.z) / Size.xz;
}

////////////////////////
// Fragment
////////////////////////

void fragment() {
	// Recover UVs (Terrain3D's injected debug views expect `uv`)
	vec2 uv = UV;

	vec2 p = v_vertex.xz;
	if (any(lessThan(p, TerrainBounds.xy)) || any(greaterThan(p, TerrainBounds.zw))) discard;
	float c  = grid_level(p);
	float c2 = grid_level(p * Spacing);
	float c3 = grid_level(p * Spacing2);
	float c4 = grid_level(p * Spacing3);

	ALBEDO = texture(MainTex, map_uv(v_vertex)).rgb;
	ALPHA = clamp(max((c + c2 + c3 + c4) * Thickness, MapOverlay) * Opacity, 0.0, 1.0);
}
