uniform float runtime_layer_alpha = 1.0;
uniform vec3 runtime_transform_x = vec3(1.0, 0.0, 0.0);
uniform vec3 runtime_transform_y = vec3(0.0, 1.0, 0.0);
uniform bool runtime_clip = false;
uniform sampler2D runtime_mask_texture : filter_nearest, repeat_disable;
uniform vec2 runtime_canvas_size = vec2(768.0);
uniform float runtime_receiver_threshold = 0.5;
uniform float runtime_source_threshold = 0.25;
vec2 runtime_position(vec2 p) {
    return vec2(dot(runtime_transform_x, vec3(p, 1.0)), dot(runtime_transform_y, vec3(p, 1.0)));
}
