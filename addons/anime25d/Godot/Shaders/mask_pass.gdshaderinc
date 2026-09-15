void vertex() { VERTEX = runtime_position(model_deform(VERTEX, VERTEX_ID)); }
void fragment() {
    if (texture(TEXTURE, UV).a < runtime_source_threshold) discard;
    COLOR = vec4(1.0);
}
