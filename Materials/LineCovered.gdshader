shader_type spatial;
// A line under a surface: faint, and drawn through whatever covers it.
render_mode unshaded, cull_disabled, depth_test_disabled, depth_draw_never, shadows_disabled;

uniform vec4 _Color : source_color = vec4(1.0);

void fragment() {
	ALBEDO = _Color.rgb;
	ALPHA = 0.5;
}
