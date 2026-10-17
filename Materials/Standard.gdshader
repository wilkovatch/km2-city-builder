shader_type spatial;
// Standard, Opaque mode: alpha is ignored and the surface writes depth.
render_mode depth_draw_opaque;

#include "res://Materials/StandardCommon.gdshaderinc"

void fragment() {
	ROUGHNESS = standard_roughness();
	ALBEDO = standard_surface(UV).rgb;
}
