shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;
#include "res://addons/anime25d/Godot/Shaders/runtime.gdshaderinc"
#include "deformation.gdshaderinc"
vec2 model_deform(vec2 p, int vertex_id) { return deform_vertex(p, vertex_id); }
#include "res://addons/anime25d/Godot/Shaders/color_pass.gdshaderinc"
