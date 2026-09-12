shader_type canvas_item;

uniform vec4 dark_color : hint_color = vec4(0.0352941, 0.0431373, 0.0745098, 1.0);
uniform vec4 light_color : hint_color = vec4(218.0/255.0, 232.0/255.0, 230.0/255.0, 1.0);
uniform vec4 accent_color : hint_color = vec4(0.682353, 1.0, 0.0, 1.0);
uniform float black_threshold : hint_range(0.0, 1.0) = 0.18;
uniform float accent_green_minimum : hint_range(0.0, 1.0) = 0.5;
uniform float accent_green_dominance : hint_range(0.0, 1.0) = 0.12;
uniform float alpha_threshold : hint_range(0.0, 1.0) = 0.5;

void fragment()
{
	vec4 source = texture(TEXTURE, UV);
	if (source.a < alpha_threshold)
		discard;

	float brightness = dot(source.rgb, vec3(0.2126, 0.7152, 0.0722));
	bool is_accent = source.g >= accent_green_minimum
		&& source.g - max(source.r, source.b) >= accent_green_dominance;

	vec3 result = light_color.rgb;
	if (brightness <= black_threshold)
		result = dark_color.rgb;
	else if (is_accent)
		result = accent_color.rgb;

	COLOR = vec4(result, 1.0);
}
