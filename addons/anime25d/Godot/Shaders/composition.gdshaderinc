uniform int compose_count;
uniform int compose_kinds[16];
uniform vec4 compose_matrices[16];
uniform vec2 compose_translations[16];
uniform float compose_weights[16];
uniform sampler2D compose_offsets : filter_nearest, repeat_disable;
vec2 model_deform(vec2 p, int vertex_id) {
    for (int i = 0; i < compose_count; i++) {
        if (compose_kinds[i] == 0) {
            vec4 m = compose_matrices[i];
            p = vec2(p.x * m.x + p.y * m.z, p.x * m.y + p.y * m.w) + compose_translations[i];
        } else {
            p += texelFetch(compose_offsets, ivec2(vertex_id, i), 0).xy * compose_weights[i];
        }
    }
    return p;
}
