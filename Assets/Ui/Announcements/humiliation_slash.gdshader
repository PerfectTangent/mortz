shader_type canvas_item;
render_mode unshaded;
uniform float progress : hint_range(0.0, 1.0) = 1.0;
uniform float seed = 12.0;
uniform vec2 area_size = vec2(160.0, 22.0);

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}
void fragment() {
    vec2 q = UV * area_size;
    float edge = hash(vec2(floor(q.x * 0.5), seed));
    float center = 0.72 - UV.x * 0.44;
    float taper = pow(max(0.0, sin(UV.x * 3.14159)), 0.35);
    float thickness = (0.06 + edge * 0.065) * taper;
    float stroke = 1.0 - smoothstep(thickness, thickness + 0.035, abs(UV.y - center));
    float scratch = step(0.12, hash(floor(q) + seed));
    float reveal = (1.0 - smoothstep(progress - 0.025, progress, UV.x)) * step(0.001, progress);
    COLOR.a *= stroke * mix(0.45, 1.0, scratch) * reveal;
}