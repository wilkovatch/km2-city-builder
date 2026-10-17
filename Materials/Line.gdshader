shader_type spatial;
// Line material: Standard in Opaque mode, so alpha is ignored.
render_mode unshaded, cull_disabled;

uniform vec4 _Color : source_color = vec4(1.0);

void fragment() {
	ALBEDO = _Color.rgb;
}
