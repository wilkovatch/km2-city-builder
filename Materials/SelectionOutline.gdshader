shader_type spatial;
// Screen-space selection outline, drawn on a fullscreen quad by RuntimeGizmos.SelectionOutline.
// mask_texture holds the silhouette of the selected objects in its alpha channel; every pixel outside
// the silhouette within outline_width pixels of it is colored, with an anti-aliased outer edge.
render_mode unshaded, depth_test_disabled, depth_draw_never, cull_disabled, blend_mix, shadows_disabled, fog_disabled;

uniform sampler2D mask_texture : filter_nearest, repeat_disable;
uniform vec4 outline_color : source_color = vec4(1.0, 0.5, 0.0, 1.0);
uniform float outline_width : hint_range(1.0, 8.0) = 3.0;

const float MASK_THRESHOLD = 0.1;
const int MAX_RADIUS = 9;

void vertex() {
	POSITION = vec4(VERTEX.xy, 0.5, 1.0);
}

bool is_masked(vec2 uv) {
	return texture(mask_texture, uv).a > MASK_THRESHOLD;
}

void fragment() {
	if (is_masked(SCREEN_UV)) discard;

	vec2 texel = 1.0 / vec2(textureSize(mask_texture, 0));
	int radius = min(int(ceil(outline_width + 0.5)), MAX_RADIUS);
	float nearest = float(radius) + 1.0;
	for (int x = -radius; x <= radius; x++) {
		for (int y = -radius; y <= radius; y++) {
			float d = length(vec2(float(x), float(y)));
			if (d < nearest && is_masked(SCREEN_UV + vec2(float(x), float(y)) * texel)) {
				nearest = d;
			}
		}
	}

	float coverage = 1.0 - smoothstep(outline_width - 0.5, outline_width + 0.5, nearest);
	if (coverage <= 0.0) discard;
	ALBEDO = outline_color.rgb;
	ALPHA = outline_color.a * coverage;
}
