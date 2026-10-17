shader_type spatial;
// Standard, Transparent mode: Blend One OneMinusSrcAlpha, ZWrite Off.
// Diffuse is premultiplied by alpha while lighting/reflections stay at full strength.
render_mode blend_premul_alpha, depth_draw_never;

#include "res://Materials/StandardCommon.gdshaderinc"

void fragment() {
	ROUGHNESS = standard_roughness();
	vec4 surface = standard_surface(UV);
	ALBEDO = surface.rgb * surface.a;
	ALPHA = surface.a;
}
