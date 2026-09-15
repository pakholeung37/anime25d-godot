shader_type canvas_item;
render_mode unshaded, blend_disabled;
#include "runtime.gdshaderinc"
vec2 model_deform(vec2 p, int vertex_id) { return p; }
#include "mask_pass.gdshaderinc"
