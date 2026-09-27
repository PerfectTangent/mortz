shader_type canvas_item;
render_mode unshaded;
uniform float seed = 12.0;
uniform float blood_stain : hint_range(0.0, 1.0) = 0.0;
uniform vec4 blood_color : source_color = vec4(0.8, 0.2, 0.12, 1.0);
varying vec2 ink_position;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}
void vertex() {
    ink_position = VERTEX;
}
void fragment() {
    // Local coordinates keep wear continuous across glyph atlas entries.
    vec2 p = ink_position + vec2(seed * 17.0, seed);
    float fleck = hash(floor(p * 1.4));
    float patch = hash(floor(p / vec2(4.0, 2.0)));
    float scratch = step(0.98, hash(floor(vec2(p.x * 0.09, p.y * 1.7))));
    float wear = step(0.055, fleck + patch * 0.08) * (1.0 - scratch * 0.55);
    COLOR.rgb *= 0.92 + 0.08 * patch;
    float soak = smoothstep(15.0, 32.0, ink_position.y + sin(ink_position.x * 0.18) * 3.0);
    COLOR.rgb = mix(COLOR.rgb, blood_color.rgb, soak * blood_stain);
    COLOR.a *= wear;
}