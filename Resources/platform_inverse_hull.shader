shader_type spatial;
render_mode unshaded, cull_front, depth_draw_never;

uniform vec4 outline_color : hint_color = vec4(0.0, 1.0, 0.0, 1.0);
uniform vec3 hull_center = vec3(0.0);
uniform vec3 hull_scale = vec3(1.04);

void vertex()
{
	VERTEX = hull_center + (VERTEX - hull_center) * hull_scale;
}

void fragment()
{
	ALBEDO = outline_color.rgb;
	ALPHA = outline_color.a;
}
