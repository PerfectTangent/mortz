shader_type canvas_item;
render_mode unshaded;
uniform float seed = 12.0;
uniform float blood_burst : hint_range(0.0, 1.0) = 0.0;
uniform float burst_progress : hint_range(0.0, 1.0) = 1.0;
uniform vec2 area_size = vec2(240.0, 40.0);
uniform vec4 blood_dark : source_color = vec4(0.23, 0.018, 0.013, 1.0);
uniform vec4 blood_light : source_color = vec4(0.63, 0.075, 0.035, 1.0);
uniform float severity : hint_range(0.0, 1.0) = 0.0;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}
void fragment() {
    vec2 p = UV;
    float grain = hash(floor(p * vec2(270.0, 22.0)) + seed);
    float edge = hash(floor(vec2(p.x * 65.0, seed)));
    float center = 0.45 + sin(p.x * 13.0) * 0.055;
    float thickness = (0.06 + severity * 0.16 + edge * (0.08 + severity * 0.18))
        * sin(p.x * 3.14159);
    float smear = 1.0 - smoothstep(thickness, thickness + 0.065, abs(p.y - center));
    float dry = step(0.25 - severity * 0.15, grain);
    vec2 cell = floor(p * vec2(30.0, 8.0));
    float spot = hash(cell + seed + 13.0);
    float drops = (1.0 - smoothstep(0.08, 0.13 + severity * 0.18,
        length(fract(p * vec2(30.0, 8.0)) - 0.5))) * step(1.0 - severity * 0.13, spot);
    float alpha = max(smear * dry, drops * 0.8) * (0.6 + severity * 0.4);
    if (blood_burst > 0.0) {
        vec2 q = p * area_size;
        float spray = 0.0;
        for (int i = 0; i < 22; i++) {
            float index = float(i);
            float random = hash(vec2(index + seed, 11.0));
            vec2 center_point = vec2((index + 0.5) / 22.0, 0.2 + random * 0.58) * area_size;
            center_point.x = mix(area_size.x * 0.5, center_point.x, burst_progress);
            float radius = (1.0 + random * 3.1) * smoothstep(0.0, 0.25, burst_progress);
            float blot = 1.0 - smoothstep(radius - 0.6, radius + 0.6, length(q - center_point));
            spray = max(spray, blot);
        }
        float drips = 0.0;
        for (int i = 0; i < 6; i++) {
            float random = hash(vec2(float(i) + seed, 37.0));
            float x = (0.12 + float(i) * 0.15) * area_size.x;
            float start = area_size.y * (0.45 + random * 0.15);
            float end = min(area_size.y - 1.5, start + (7.0 + random * 8.0) * burst_progress);
            float stem = (1.0 - smoothstep(0.5, 1.4, abs(q.x - x)))
                * smoothstep(start - 1.0, start + 1.0, q.y)
                * (1.0 - smoothstep(end - 1.0, end + 1.0, q.y));
            float drop = 1.0 - smoothstep(0.8, 1.8, length(q - vec2(x, end)));
            drips = max(drips, max(stem, drop) * burst_progress);
        }
        float burst = max(smear * dry, max(spray * (0.8 + grain * 0.2), drips));
        alpha = mix(alpha, burst * smoothstep(0.0, 0.12, burst_progress), blood_burst);
    }
    vec3 blood = mix(blood_dark.rgb, blood_light.rgb, grain * 0.7);
    COLOR = vec4(blood, alpha) * COLOR;
}