shader_type canvas_item;
render_mode unshaded, blend_disabled;

void fragment() {
    if (texture(TEXTURE, UV).a < 0.25) discard;
    COLOR = vec4(1.0);
}
