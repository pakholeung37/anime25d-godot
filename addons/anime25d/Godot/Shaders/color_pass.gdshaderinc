varying vec2 model_position;
varying vec4 item_color;
void vertex() {
    VERTEX = runtime_position(model_deform(VERTEX, VERTEX_ID));
    model_position = VERTEX;
    item_color = COLOR;
}
void fragment() {
    vec4 texel = texture(TEXTURE, UV);
    if (runtime_clip) {
        vec2 p = model_position / runtime_canvas_size;
        if (any(lessThan(p, vec2(0.0))) || any(greaterThan(p, vec2(1.0)))) discard;
        if (texture(runtime_mask_texture, p).a < runtime_receiver_threshold) discard;
    }
    COLOR = vec4(texel.rgb * item_color.rgb * item_color.a, texel.a * item_color.a) * runtime_layer_alpha;
}
