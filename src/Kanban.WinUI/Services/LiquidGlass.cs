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
    /// Real liquid glass on the Visual Layer: a per-panel SpriteVisual whose brush is a
    /// Win2D effect graph — backdrop capture → gaussian blur → saturation boost (the
    /// "liquid" refraction feel) → theme tint composite — clipped to rounded corners.
    /// Runs entirely on the compositor thread, so it stays smooth during scrolling
    /// and drag &amp; drop. Adapts automatically to light/dark theme changes.
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
                if (host.ActualWidth <= 0 || host.ActualHeight <= 0)
                    return;

                var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
                bool dark = host.ActualTheme == ElementTheme.Dark;
                var tint = dark
                    ? Color.FromArgb(0xAE, 0x22, 0x1E, 0x1C)   // warm dark glass
                    : Color.FromArgb(0x9C, 0xFB, 0xFA, 0xF9);  // warm light glass

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
                                BlurAmount = 20f,
                                BorderMode = EffectBorderMode.Hard,
                                Source = new CompositionEffectSourceParameter("backdrop")
                            }
                        },
                        new ColorSourceEffect { Color = tint }
                    }
                };

                var brush = compositor.CreateEffectFactory(graph).CreateBrush();
                brush.SetSourceParameter("backdrop", compositor.CreateBackdropBrush());

                var sprite = compositor.CreateSpriteVisual();
                sprite.Brush = brush;
                sprite.RelativeSizeAdjustment = Vector2.One;

                var rounded = compositor.CreateRoundedRectangleGeometry();
                rounded.CornerRadius = new Vector2(cornerRadius);
                rounded.Size = new Vector2((float)host.ActualWidth, (float)host.ActualHeight);
                sprite.Clip = compositor.CreateGeometricClip(rounded);

                ElementCompositionPreview.SetElementChildVisual(host, sprite);
            }
            catch
            {
                // Effect graph unavailable (very old GPU/driver): the XAML brushes remain as fallback
            }
        }
    }
}
