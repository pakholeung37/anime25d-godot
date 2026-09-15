// Vertex-only deformation. Both the visible layer and eye-mask pass use this implementation.
#include "bindings.gdshaderinc"

uniform bool gpu_deformation = false;
uniform sampler2D pose_texture : filter_nearest, repeat_disable;
uniform sampler2D weight_texture : filter_nearest, repeat_disable;
uniform int part_role;
uniform int part_group;
uniform int part_side;
uniform int fade_mode;
uniform int strand_count;
uniform vec2 spring_displacements[6];
uniform float layer_depth;
uniform float face_scale;
uniform vec4 part_rectangle;
uniform vec4 face_rectangle;
uniform vec2 face_center;
uniform vec4 mouth_rectangle;
uniform vec2 mouth_center;
uniform vec2 neck_pivot;
uniform vec2 body_pivot;
uniform vec2 neck_limits;
uniform bool has_eye;
uniform vec4 eye_rectangle;
uniform vec2 iris_center;
uniform float closed_eye_y;

float pose_value(int index) {
    return texelFetch(pose_texture, ivec2(index / 4, 0), 0)[index % 4];
}

vec4 vertex_weights(int vertex_id, int block) {
    int address = vertex_id * 3 + block;
    return texelFetch(weight_texture, ivec2(address % 256, address / 256), 0);
}

float ease_weight(float value) {
    float weight = clamp(value, 0.0, 1.0);
    return weight * weight * (3.0 - 2.0 * weight);
}

vec2 rotate_point(vec2 position, vec2 pivot, float angle) {
    float cosine = cos(angle);
    float sine = sin(angle);
    vec2 relative = position - pivot;
    return pivot + vec2(relative.x * cosine - relative.y * sine, relative.x * sine + relative.y * cosine);
}

vec2 deform_features(vec2 position, float eye_openness) {
    bool left = part_side == SIDE_LEFT;
    vec2 center = part_rectangle.xy + part_rectangle.zw / 2.0;
    if (has_eye && (part_role == ROLE_CLOSED_EYE || part_role == ROLE_ALTERNATE_CLOSED_EYE)) {
        float scale = pose_value(left ? LEFT_EYE_SCALE : RIGHT_EYE_SCALE);
        vec2 eye_center = (eye_rectangle.xy + eye_rectangle.zw) / 2.0;
        position = eye_center + (position - eye_center) * scale;
    }
    if (part_role == ROLE_OPEN_MOUTH || part_role == ROLE_CLOSED_MOUTH) {
        position = mouth_center + (position - mouth_center) * pose_value(MOUTH_SCALE);
    }
    if (fade_mode == FADE_OPEN_EYE && has_eye) {
        if (part_role == ROLE_IRIS) {
            position = iris_center + (position - iris_center) * pose_value(IRIS_SCALE)
                * vec2(pose_value(IRIS_BOUNCE_X), pose_value(IRIS_BOUNCE_Y));
            position += vec2(pose_value(GAZE_HORIZONTAL) * tuning_gaze_horizontal_pixels,
                pose_value(GAZE_VERTICAL) * tuning_gaze_vertical_pixels) * face_scale;
            position.y = closed_eye_y + (position.y - closed_eye_y) * (1.0 - tuning_iris_close_compression
                * ease_weight((tuning_iris_close_threshold - eye_openness) / tuning_iris_close_threshold));
        } else {
            position.y = closed_eye_y + (position.y - closed_eye_y) * (1.0 - tuning_eye_close_compression * (1.0 - eye_openness));
        }
    }
    if ((fade_mode == FADE_CLOSED_EYE || fade_mode == FADE_ALTERNATE_CLOSED_EYE) && has_eye) {
        position.y -= eye_openness * tuning_closed_eye_lift_pixels;
        position.y += pose_value(CLOSED_EYE_OFFSET_Y) * tuning_closed_eye_offset_pixels * face_scale;
        position = rotate_point(position, center, pose_value(CLOSED_EYE_ANGLE) * tuning_closed_eye_angle_radians * (left ? 1.0 : -1.0));
    }
    if (part_role == ROLE_EYEBROW) {
        position.y += (-pose_value(EYEBROW_HEIGHT) * tuning_eyebrow_lift_pixels + (1.0 - eye_openness) * tuning_eyebrow_blink_pixels) * face_scale;
        float angle = (left ? pose_value(LEFT_EYEBROW_ANGLE) + pose_value(SYMMETRIC_EYEBROW_ANGLE)
            : pose_value(RIGHT_EYEBROW_ANGLE) - pose_value(SYMMETRIC_EYEBROW_ANGLE)) * tuning_eyebrow_angle_radians;
        position = rotate_point(position, center, angle);
    }
    float mouth_openness = pose_value(MOUTH_OPENNESS);
    if (fade_mode == FADE_OPEN_MOUTH) {
        position.y = mouth_rectangle.y + (position.y - mouth_rectangle.y) * (tuning_mouth_minimum_height + tuning_mouth_opening_height * mouth_openness);
        float half_width = (mouth_rectangle.z - mouth_rectangle.x) / 2.0;
        float curvature = pow(abs(position.x - mouth_center.x) / (half_width + tuning_mouth_curve_padding_pixels), tuning_mouth_curve_exponent);
        position.y -= pose_value(MOUTH_SHAPE) * tuning_mouth_curve_pixels * face_scale * (curvature - tuning_mouth_curve_offset);
    }
    if (fade_mode == FADE_CLOSED_MOUTH) {
        position.y += pose_value(CLOSED_MOUTH_OFFSET_Y) * tuning_closed_mouth_offset_pixels * face_scale;
        position = rotate_point(position, mouth_center, pose_value(CLOSED_MOUTH_ANGLE) * tuning_closed_mouth_angle_radians);
    }
    if (part_role == ROLE_FACE && position.y > mouth_center.y) {
        position.y += mouth_openness * tuning_jaw_open_pixels * face_scale * ease_weight((position.y - mouth_center.y) / (face_rectangle.w - mouth_center.y));
    }
    return position;
}

vec2 deform_head(vec2 position) {
    float weight = part_group == GROUP_HEAD ? 1.0 : tuning_body_head_influence;
    if (part_role == ROLE_NECK) weight = tuning_neck_head_influence * ease_weight((neck_limits.y - position.y) / max(1.0, neck_limits.y - neck_limits.x));
    if (weight > 0.0) {
        vec2 rotated = rotate_point(position, neck_pivot, pose_value(HEAD_ROLL) * tuning_head_roll_radians);
        position += (rotated - position) * weight;
        position.x += weight * face_scale * (pose_value(HEAD_YAW) * (tuning_head_yaw_pixels + tuning_head_yaw_depth_pixels * (layer_depth - 1.0))
            + pose_value(HEAD_YAW) * (neck_pivot.y - position.y) * tuning_head_yaw_shear);
        position.y += weight * face_scale * (-pose_value(HEAD_PITCH) * (tuning_head_pitch_pixels + tuning_head_pitch_depth_pixels * (layer_depth - 1.0))
            - pose_value(HEAD_PITCH) * (layer_depth - 1.0) * (position.y - face_center.y) * tuning_head_pitch_shear);
    }
    return position;
}

vec2 deform_body(vec2 position) {
    float breath = pose_value(BREATH);
    position.y -= (part_group == GROUP_BODY ? breath * tuning_body_breath_pixels : pose_value(BREATH_HEAD) * tuning_head_breath_pixels) * face_scale;
    if (part_role == ROLE_UPPER_CLOTHING) {
        float chest_y = neck_limits.y + (face_rectangle.w - face_rectangle.y) * tuning_chest_center_face_ratio;
        vec2 radius = max(vec2(1.0), (face_rectangle.zw - face_rectangle.xy) * vec2(tuning_chest_width_face_ratio, tuning_chest_height_face_ratio));
        if (position.y < chest_y) position.y -= breath * tuning_chest_breath_pixels * face_scale * ease_weight((chest_y - position.y) / (radius.y * tuning_chest_breath_falloff));
        position.x = neck_pivot.x + (position.x - neck_pivot.x) * (1.0 + breath * tuning_chest_breath_expansion);
        vec2 distance = (position - vec2(neck_pivot.x, chest_y + pose_value(CHEST_BOUNCE_OFFSET_Y) * tuning_chest_offset_pixels * face_scale)) / radius;
        position.y += pose_value(CHEST_DISPLACEMENT) * pose_value(CHEST_BOUNCE_AMPLITUDE) * exp(-distance.x * distance.x - distance.y * distance.y);
    }
    if (part_role == ROLE_ARM_CLOTHING) {
        float weight = ease_weight((position.y - part_rectangle.y) / part_rectangle.w * tuning_arm_influence_scale);
        position.y -= pose_value(ARM_LIFT) * tuning_arm_lift_pixels * face_scale * weight;
        position.y += pose_value(ARM_OFFSET_Y) * tuning_arm_offset_pixels * face_scale;
        position.x += pose_value(ARM_LIFT) * tuning_arm_sideways_pixels * face_scale * weight * (position.x < neck_pivot.x ? 1.0 : -1.0);
    }
    return position;
}

vec2 deform_hair(vec2 position, int vertex_id) {
    if (strand_count == 0) return position;
    vec4 weights_first = vertex_weights(vertex_id, 0);
    vec4 weights_second = vertex_weights(vertex_id, 1);
    vec3 fringe_weights = vertex_weights(vertex_id, 2).xyz;
    float strand_position = weights_second.z;
    bool fringe = part_role == ROLE_FRINGE;
    if (fringe) {
        float offset = pow(strand_position, tuning_fringe_offset_exponent) * tuning_fringe_offset_pixels * face_scale;
        position.x += dot(vec3(pose_value(LEFT_FRINGE_OFFSET), pose_value(CENTER_FRINGE_OFFSET), pose_value(RIGHT_FRINGE_OFFSET)), fringe_weights) * offset;
    }
    if (pose_value(PHYSICS_ENABLED) > 0.5) {
        float distance = fringe ? min(1.0, strand_position * tuning_fringe_length_scale) : strand_position;
        float amplitude = pow(distance, fringe ? tuning_fringe_sway_exponent : tuning_hair_sway_exponent)
            * pose_value(fringe ? FRINGE_SWAY_AMPLITUDE : HAIR_SWAY_AMPLITUDE);
        float softness = pow(distance, tuning_hair_softness_exponent) * pose_value(fringe ? FRINGE_SOFTNESS : HAIR_SOFTNESS);
        float displacement = 0.0;
        for (int strand = 0; strand < strand_count; strand++) {
            float weight = strand < 4 ? weights_first[strand] : weights_second[strand - 4];
            if (weight < 0.001) continue;
            displacement += weight * (spring_displacements[strand].x * (1.0 - softness) + spring_displacements[strand].y * softness);
        }
        position.x += displacement * amplitude;
        position.y += abs(displacement) * amplitude * tuning_hair_vertical_sway;
    }
    return position;
}

vec2 deform_vertex(vec2 position, int vertex_id) {
    if (!gpu_deformation) return position;
    float openness = pose_value(part_side == SIDE_LEFT ? LEFT_EYE_OPENNESS : RIGHT_EYE_OPENNESS);
    position = deform_features(position, openness);
    position = deform_head(position);
    position = deform_body(position);
    position = deform_hair(position, vertex_id);
    float body_rotation = pose_value(BODY_ROLL) * tuning_body_roll_radians;
    if (abs(body_rotation) > 0.0001) position = rotate_point(position, body_pivot, body_rotation);
    return position;
}
