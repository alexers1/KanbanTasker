using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace Kanban_WinUI.Services
{
    /// <summary>
    /// Liquid-glass material on the Visual Layer, modelled on Apple's description of the
    /// material: it "reflects and refracts its surroundings". Each panel is built from three
    /// stacked composition layers so it reads as a physical pane of glass, not flat frost:
    ///
    ///   1. BODY   — backdrop capture → gaussian blur → saturation lift → warm tint.
    ///               This is the refraction: content behind is bent/softened and its colour
    ///               intensified, informed by the surroundings.
    ///   2. SHEEN  — a soft glossy highlight over the top third, giving the wet, curved-glass look.
    ///   3. RIM    — a crisp specular stroke tracing the rounded edge, bright at the top and
    ///               fading toward the bottom, like light catching the bevel of real glass.
    ///
    /// Everything runs on the compositor thread and rebuilds on theme change (light/dark) and
    /// resize. If the effect graph is unavailable the XAML brushes remain as a fallback.
    /// </summary>
    public static class LiquidGlass
    {
        public static void Attach(FrameworkElement host, float cornerRadius = 8f)
        {
            Apply(host, cornerRadius);
            host.ActualThemeChanged += (_, _) => Apply(host, cornerRadius);
            host.SizeChanged += (_, _) => Apply(host, cornerRadius);
        }

        private static void Apply(FrameworkElement host, float cornerRadius)
        {
            try
            {
                float w = (float)host.ActualWidth, h = (float)host.ActualHeight;
                if (w <= 0 || h <= 0)
                    return;

                var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
                bool dark = host.ActualTheme == ElementTheme.Dark;

                // --- palette per theme -------------------------------------------------
                var tint = dark
                    ? Color.FromArgb(0x8C, 0x1C, 0x18, 0x16)   // translucent warm dark glass
                    : Color.FromArgb(0x7A, 0xFC, 0xFB, 0xFA);  // translucent warm light glass
                var rimTop = dark ? Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF);
                var rimBottom = dark ? Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
                var sheenTop = dark ? Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
                var clear = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);

                var container = compositor.CreateContainerVisual();
                container.RelativeSizeAdjustment = Vector2.One;

                var clip = compositor.CreateGeometricClip(RoundedGeometry(compositor, cornerRadius, 0f, w, h));

                // 1. BODY: refraction (blur + saturation) + tint --------------------------
                var graph = new CompositeEffect
                {
                    Mode = CanvasComposite.SourceOver,
                    Sources =
                    {
                        new SaturationEffect
                        {
                            Saturation = 1.35f,
                            Source = new GaussianBlurEffect
                            {
                                BlurAmount = 18f,
                                BorderMode = EffectBorderMode.Hard,
                                Source = new CompositionEffectSourceParameter("backdrop")
                            }
                        },
                        new ColorSourceEffect { Color = tint }
                    }
                };
                var bodyBrush = compositor.CreateEffectFactory(graph).CreateBrush();
                bodyBrush.SetSourceParameter("backdrop", compositor.CreateBackdropBrush());
                var body = compositor.CreateSpriteVisual();
                body.Brush = bodyBrush;
                body.RelativeSizeAdjustment = Vector2.One;
                body.Clip = clip;
                container.Children.InsertAtTop(body);

                // 2. SHEEN: glossy highlight fading over the top third --------------------
                var sheenBrush = compositor.CreateLinearGradientBrush();
                sheenBrush.MappingMode = CompositionMappingMode.Relative;
                sheenBrush.StartPoint = new Vector2(0f, 0f);
                sheenBrush.EndPoint = new Vector2(0f, 1f);
                sheenBrush.ColorStops.Add(compositor.CreateColorGradientStop(0f, sheenTop));
                sheenBrush.ColorStops.Add(compositor.CreateColorGradientStop(0.38f, clear));
                var sheen = compositor.CreateSpriteVisual();
                sheen.Brush = sheenBrush;
                sheen.RelativeSizeAdjustment = Vector2.One;
                sheen.Clip = compositor.CreateGeometricClip(RoundedGeometry(compositor, cornerRadius, 0f, w, h));
                container.Children.InsertAtTop(sheen);

                // 3. RIM: crisp specular stroke on the rounded edge -----------------------
                var rimBrush = compositor.CreateLinearGradientBrush();
                rimBrush.MappingMode = CompositionMappingMode.Relative;
                rimBrush.StartPoint = new Vector2(0f, 0f);
                rimBrush.EndPoint = new Vector2(0f, 1f);
                rimBrush.ColorStops.Add(compositor.CreateColorGradientStop(0f, rimTop));
                rimBrush.ColorStops.Add(compositor.CreateColorGradientStop(0.5f, rimBottom));
                rimBrush.ColorStops.Add(compositor.CreateColorGradientStop(1f, rimTop));

                const float inset = 0.75f;
                var rimShape = compositor.CreateSpriteShape(RoundedGeometry(compositor, cornerRadius - inset, inset, w - inset * 2, h - inset * 2));
                rimShape.StrokeThickness = 1.2f;
                rimShape.StrokeBrush = rimBrush;
                var rimVisual = compositor.CreateShapeVisual();
                rimVisual.RelativeSizeAdjustment = Vector2.One;
                rimVisual.Shapes.Add(rimShape);
                container.Children.InsertAtTop(rimVisual);

                ElementCompositionPreview.SetElementChildVisual(host, container);
            }
            catch
            {
                // Effect graph unavailable (very old GPU/driver): XAML brushes remain as fallback
            }
        }

        private static CompositionRoundedRectangleGeometry RoundedGeometry(
            Compositor compositor, float radius, float offset, float w, float h)
        {
            var geo = compositor.CreateRoundedRectangleGeometry();
            geo.CornerRadius = new Vector2(Math.Max(0f, radius));
            geo.Offset = new Vector2(offset, offset);
            geo.Size = new Vector2(Math.Max(0f, w), Math.Max(0f, h));
            return geo;
        }
    }
}
