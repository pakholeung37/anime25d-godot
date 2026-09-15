shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

uniform float layer_alpha = 1.0;
uniform bool clip_eye = false;
uniform sampler2D eye_mask : filter_nearest, repeat_disable;
uniform vec2 canvas_size = vec2(768.0);
varying vec2 model_position;
varying vec4 item_color;

void vertex() {
    model_position = VERTEX;
    item_color = COLOR;
}

void fragment() {
    vec4 texel = texture(TEXTURE, UV);
    if (clip_eye) {
        vec2 mask_uv = model_position / canvas_size;
        if (any(lessThan(mask_uv, vec2(0.0))) || any(greaterThan(mask_uv, vec2(1.0)))) discard;
        if (texture(eye_mask, mask_uv).a < 0.5) discard;
    }
    // Texture RGB is premultiplied by the importer before linear filtering.
    COLOR = vec4(texel.rgb * item_color.rgb * item_color.a, texel.a * item_color.a) * layer_alpha;
}
