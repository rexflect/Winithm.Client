shader_type canvas_item;

uniform float brightness : hint_range(0.0, 5.0) = 0.35;

void fragment() {
  vec4 color = texture(TEXTURE, UV);
  color.rgb *= brightness;

  COLOR = color;
}
