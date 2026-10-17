shader_type spatial;
// Standard, Cutout mode: pixels below _Cutoff are discarded, the rest stays opaque.
render_mode depth_draw_opaque;

#include "res://Materials/StandardCommon.gdshaderinc"

uniform float _Cutoff : hint_range(0.0, 1.0) = 0.5;

void fragment() {
	ROUGHNESS = standard_roughness();
	vec4 surface = standard_surface(UV);
	ALBEDO = surface.rgb;
	ALPHA = surface.a;
	ALPHA_SCISSOR_THRESHOLD = _Cutoff;
}
