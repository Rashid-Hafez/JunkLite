using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>Dark square timer backing and a lighter radial wipe, drawn together in one UI mesh.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ModCooldownGraphic : MaskableGraphic
    {
        private float remaining;
        private float startAngle;
        private bool wipeClockwise;
        private Color fillColor;
        private Color backgroundColor;

        public void SetAppearance(float fraction, ModCooldownStyle style, float flash = 0f, bool active = false)
        {
            fraction = Mathf.Clamp01(fraction);
            Color accent = active ? style.activeColor : Color.Lerp(style.finishingColor, style.cooldownColor,
                Mathf.Clamp01(fraction / Mathf.Max(0.01f, style.finishingFraction)));
            accent.a = style.overlayColor.a;
            Color fill = Color.Lerp(style.overlayColor, accent, Mathf.Clamp01(style.tintStrength));
            Color background = style.timerBackgroundColor;
            if (flash > 0f)
            {
                fraction = 1f;
                fill = style.finishingColor;
                fill.a *= Mathf.Clamp01(flash) * style.readyFlashOpacity;
                background = Color.clear;
            }

            if (remaining == fraction && startAngle == style.startAngle &&
                wipeClockwise == style.wipeClockwise && fillColor == fill && backgroundColor == background)
                return;

            remaining = fraction;
            startAngle = style.startAngle;
            wipeClockwise = style.wipeClockwise;
            fillColor = fill;
            backgroundColor = background;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float halfSize = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (halfSize <= 0f) return;

            // Keep the entire icon dimmed, even after the wipe has passed behind the numbers.
            if (backgroundColor.a > 0f)
            {
                Color tint = backgroundColor * color;
                vh.AddVert(rect.center + new Vector2(-halfSize, -halfSize), tint, Vector2.zero);
                vh.AddVert(rect.center + new Vector2(-halfSize, halfSize), tint, Vector2.zero);
                vh.AddVert(rect.center + new Vector2(halfSize, halfSize), tint, Vector2.zero);
                vh.AddVert(rect.center + new Vector2(halfSize, -halfSize), tint, Vector2.zero);
                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(2, 3, 0);
            }
            if (remaining <= 0f || fillColor.a <= 0f) return;

            // Angles run clockwise from the top. The remaining sector runs opposite the wipe.
            float direction = wipeClockwise ? -1f : 1f;
            float start = Mathf.Repeat(startAngle, 360f);
            float sweep = remaining * 360f;
            float firstCorner = 45f + 90f * (direction > 0f
                ? Mathf.Floor((start - 45f) / 90f) + 1f
                : Mathf.Ceil((start - 45f) / 90f) - 1f);
            Vector2 previous = SquareEdge(start, halfSize);
            for (int i = 0; i < 4; i++)
            {
                float corner = firstCorner + direction * i * 90f;
                if ((corner - start) * direction >= sweep) break;
                Vector2 next = SquareEdge(corner, halfSize);
                AddTriangle(vh, rect.center, previous, next);
                previous = next;
            }

            AddTriangle(vh, rect.center, previous, SquareEdge(start + direction * sweep, halfSize));
        }

        private static Vector2 SquareEdge(float angle, float halfSize)
        {
            float radians = angle * Mathf.Deg2Rad;
            Vector2 ray = new(Mathf.Sin(radians), Mathf.Cos(radians));
            return ray * (halfSize / Mathf.Max(Mathf.Abs(ray.x), Mathf.Abs(ray.y)));
        }

        private void AddTriangle(VertexHelper vh, Vector2 center, Vector2 from, Vector2 to)
        {
            int first = vh.currentVertCount;
            Color tint = fillColor * color;
            vh.AddVert(center, tint, Vector2.zero);
            vh.AddVert(center + from, tint, Vector2.zero);
            vh.AddVert(center + to, tint, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2);
        }
    }
}
