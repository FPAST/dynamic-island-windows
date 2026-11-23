using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// Liquid glass over the blurred desktop picture: near the rim the glass is a thick lens that bends the picture
/// (it samples further out along the edge normal), with a hint of colour fringing at the very edge, a thin rim of
/// light brighter on the side facing the light, and a little extra saturation. Applied to a picture that is a
/// band wider than the card on every side, so the lens has something to pull in.
/// </summary>
public sealed class GlassEffect : ShaderEffect
{
    // c0: size of the input in pixels (w, h), band around the card (px), corner radius (px)
    // c1: lens width (px), lens strength (px), colour fringe (px), saturation
    // c2: light direction (x, y), rim brightness, 1 = bend the input itself (no second picture, no hairline)
    private const string Hlsl = @"
sampler2D input : register(s0);
sampler2D clear : register(s1);
float4 geo : register(c0);
float4 lens : register(c1);
float4 light : register(c2);

float sdRound(float2 p, float2 h, float r)
{
    float2 q = abs(p) - h + r;
    return length(max(q, 0)) + min(max(q.x, q.y), 0) - r;
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 size = geo.xy;
    float2 p = uv * size - size * 0.5;
    float2 h = size * 0.5 - geo.z;
    float d = sdRound(p, h, geo.w);

    // the outward normal of the nearest edge (the SDF's gradient)
    float e = 1.0;
    float2 n = float2(sdRound(p + float2(e, 0), h, geo.w) - sdRound(p - float2(e, 0), h, geo.w),
                      sdRound(p + float2(0, e), h, geo.w) - sdRound(p - float2(0, e), h, geo.w));
    n = n / max(length(n), 1e-4);

    // 1 at the rim, easing to 0 a lens-width inside: a thick lens only along the edge
    float k = saturate(1.0 + d / lens.x);
    k = k * k * (3.0 - 2.0 * k);
    float2 bend = n * k * lens.y / size;
    float2 fringe = n * k * k * lens.z / size;

    // frosted in the middle; towards the rim the glass turns clear and bends the desktop behind it
    float3 frost = tex2D(input, uv).rgb;
    float3 sharp = float3(tex2D(clear, uv + bend + fringe).r, tex2D(clear, uv + bend).g, tex2D(clear, uv + bend - fringe).b);
    float3 own = float3(tex2D(input, uv + bend + fringe).r, tex2D(input, uv + bend).g, tex2D(input, uv + bend - fringe).b);
    // bending its own picture, the bent picture replaces the plain one (a cross-fade would show both)
    float3 c = lerp(frost, lerp(sharp, own, light.w), lerp(k * 0.9, 1.0, light.w));

    float l = dot(c, float3(0.299, 0.587, 0.114));
    c = lerp(float3(l, l, l), c, lens.w);

    // a hairline of light along the rim, brighter where the edge faces the light
    float rim = saturate(1.0 - abs(d + 1.2) / 1.4) * (1.0 - light.w);
    float facing = saturate(dot(n, light.xy) * 0.6 + 0.4);   // light.xy points towards the light
    c += rim * facing * light.z;
    // and the lens itself catches a little of it
    c += k * facing * light.z * 0.14;

    return float4(c, 1.0);
}";

    private static PixelShader? _shader;
    private static bool _tried;

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(GlassEffect), 0);
    public static readonly DependencyProperty ClearProperty = RegisterPixelShaderSamplerProperty("Clear", typeof(GlassEffect), 1);
    public static readonly DependencyProperty GeometryProperty = DependencyProperty.Register(nameof(Geometry), typeof(Point4D), typeof(GlassEffect),
        new UIPropertyMetadata(new Point4D(100, 100, 18, 26), PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty LensProperty = DependencyProperty.Register(nameof(Lens), typeof(Point4D), typeof(GlassEffect),
        new UIPropertyMetadata(new Point4D(14, 9, 1.5, 1.3), PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty LightProperty = DependencyProperty.Register(nameof(Light), typeof(Point4D), typeof(GlassEffect),
        new UIPropertyMetadata(new Point4D(-0.45, -0.9, 0.35, 0), PixelShaderConstantCallback(2)));

    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    /// <summary>The same piece of desktop, barely blurred (what the rim bends).</summary>
    public Brush Clear { get => (Brush)GetValue(ClearProperty); set => SetValue(ClearProperty, value); }
    /// <summary>(width, height, band, radius) in pixels of the picture the effect is on.</summary>
    public Point4D Geometry { get => (Point4D)GetValue(GeometryProperty); set => SetValue(GeometryProperty, value); }
    /// <summary>(lens width, bend, colour fringe, saturation).</summary>
    public Point4D Lens { get => (Point4D)GetValue(LensProperty); set => SetValue(LensProperty, value); }
    /// <summary>(direction x, direction y, rim brightness, mode): where the light comes from; mode 1 bends the input itself.</summary>
    public Point4D Light { get => (Point4D)GetValue(LightProperty); set => SetValue(LightProperty, value); }

    private GlassEffect(PixelShader shader)
    {
        PixelShader = shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(ClearProperty);
        UpdateShaderValue(GeometryProperty);
        UpdateShaderValue(LensProperty);
        UpdateShaderValue(LightProperty);
    }

    /// <summary>null when the shader can't be compiled here (the glass is then the plain blurred picture).</summary>
    public static GlassEffect? TryCreate()
    {
        if (!_tried)
        {
            _tried = true;
            try
            {
                var ps = new PixelShader();
                ps.SetStreamSource(new MemoryStream(ChromaticEffect.Compile(Hlsl, "main", "ps_3_0")));
                _shader = ps;
            }
            catch (Exception e) { Log.Write("glass shader: " + e.Message); }
        }
        return _shader == null ? null : new GlassEffect(_shader);
    }
}
