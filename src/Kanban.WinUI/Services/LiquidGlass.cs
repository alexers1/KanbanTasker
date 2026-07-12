using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.Effects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace Kanban_WinUI.Services
{
    /// <summary>
    /// True liquid glass on the Visual Layer + Win2D:
    ///  - per-panel SpriteVisual with an effect graph: backdrop capture → gaussian blur →
    ///    saturation boost (refraction "juice") → warm theme tint composite
    ///  - a compositor PointLight that follows the pointer, feeding a SceneLightingEffect
    ///    specular layer inside the same graph — the glass GLINTS as the mouse moves,
    ///    which is the behavior that separates liquid glass from plain acrylic
    ///  - rounded-rect geometric clip; everything runs on the compositor thread
    ///  - re-applies on theme change (light/dark tints) and resize; XAML brushes remain
    ///    as fallback when the effect graph is unavailable.
    /// </summary>
    public static class LiquidGlass
    {
        private static PointLight? _light;

        public static void Attach(FrameworkElement host, float cornerRadius = 8f)
        {
            Apply(host, cornerRadius);
            host.ActualThemeChanged += (_, _) => Apply(host, cornerRadius);
            host.SizeChanged += (_, _) => Apply(host, cornerRadius);
        }

        /// <summary>Call once on the page root: the glint light follows the pointer.</summary>
        public static void AttachPointerGlint(FrameworkElement root)
        {
            try
            {
                var rootVisual = ElementCompositionPreview.GetElementVisual(root);
                var compositor = rootVisual.Compositor;

                _light = compositor.CreatePointLight();
                _light.CoordinateSpace = rootVisual;
                _light.Color = Microsoft.UI.Colors.White;
                _light.Intensity = 1.4f;
                _light.Offset = new Vector3(400, 200, 220);

                root.PointerMoved += (s, e) =>
                {
                    var p = e.GetCurrentPoint(root).Position;
                    if (_light != null)
                        _light.Offset = new Vector3((float)p.X, (float)p.Y, 220);
                };
            }
            catch { /* lighting unavailable - glass still renders without glint */ }
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
                    ? Color.FromArgb(0xA8, 0x22, 0x1E, 0x1C)   // warm dark glass
                    : Color.FromArgb(0x96, 0xFB, 0xFA, 0xF9);  // warm light glass

                var graph = new CompositeEffect
                {
                    Mode = CanvasComposite.Add,   // specular light ADDS on top of the glass
                    Sources =
                    {
                        new CompositeEffect
                        {
                            Mode = CanvasComposite.SourceOver,
                            Sources =
                            {
                                new SaturationEffect
                                {
                                    Saturation = 1.4f,
                                    Source = new GaussianBlurEffect
                                    {
                                        BlurAmount = 26f,
                                        BorderMode = EffectBorderMode.Hard,
                                        Source = new CompositionEffectSourceParameter("backdrop")
                                    }
                                },
                                new ColorSourceEffect { Color = tint }
                            }
                        },
                        // Pointer-following specular sheen (lit by the PointLight)
                        new SceneLightingEffect
                        {
                            AmbientAmount = 0f,
                            DiffuseAmount = 0.06f,
                            SpecularAmount = 0.45f,
                            SpecularShine = 42f
                        }
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

                _light?.Targets.Add(sprite);   // this panel glints under the pointer light
            }
            catch
            {
                // Effect graph unavailable (very old GPU/driver): XAML brushes remain as fallback
            }
        }
    }
}
