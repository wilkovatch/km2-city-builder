shader_type spatial;
// Standard, Fade mode: Blend SrcAlpha OneMinusSrcAlpha, ZWrite Off.
render_mode blend_mix, depth_draw_never;

#include "res://Materials/StandardCommon.gdshaderinc"

void fragment() {
	ROUGHNESS = standard_roughness();
	vec4 surface = standard_surface(UV);
	ALBEDO = surface.rgb;
	ALPHA = surface.a;
}
