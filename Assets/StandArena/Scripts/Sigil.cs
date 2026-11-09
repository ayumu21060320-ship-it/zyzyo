using UnityEngine;

namespace StandArena
{
    /// <summary>Draws a procedural emblem for a Stand. Same name + owner always gives the same emblem.</summary>
    public static class Sigil
    {
        static uint Hash(string s)
        {
            uint x = 2166136261;
            foreach (var ch in s ?? "") { x ^= ch; x *= 16777619; }
            return x;
        }

        public static Texture2D Make(Stand s, int size = 256)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var px = new Color[size * size];
            uint seed = Hash(s.name + s.owner);
            var rng = new System.Random((int)(seed & 0x7fffffff));
            float hue = (seed % 360) / 360f;
            var st = s.stats ?? new StandStats();
            float c = size / 2f;

            // Background with a soft colored halo.
            var bg = new Color(0.043f, 0.031f, 0.063f, 1f);
            var haloCol = Color.HSVToRGB(hue, 0.8f, 0.9f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / (size * 0.48f);
                    float halo = Mathf.Pow(Mathf.Clamp01(1 - d), 2) * 0.45f;
                    px[y * size + x] = Color.Lerp(bg, haloCol, halo);
                }

            int sym = 3 + (int)(seed % 6);
            int layers = 3 + st.potential;
            float scale = size / 400f;

            for (int L = 0; L < layers; L++)
            {
                float rad = (40 + L * (130f / layers) + (float)rng.NextDouble() * 10) * scale;
                int kind = rng.Next(3);
                float width = (1.2f + st.power / 5f * 2.2f * (1 - (float)L / layers)) * scale * 1.4f;
                var col = Color.HSVToRGB((hue + L * 0.07f) % 1f, 0.85f, Mathf.Min(1f, 0.7f + L * 0.04f));
                col.a = Mathf.Max(0.3f, 0.95f - L * 0.08f);

                for (int i = 0; i < sym; i++)
                {
                    float ang = i * Mathf.PI * 2 / sym;
                    if (kind == 0)
                    {
                        Bezier(px, size, c, ang, new Vector2(0, -rad * 0.3f), new Vector2(rad * 0.5f, -rad * 0.7f), new Vector2(0, -rad), width, col);
                        Bezier(px, size, c, ang, new Vector2(0, -rad * 0.3f), new Vector2(-rad * 0.5f, -rad * 0.7f), new Vector2(0, -rad), width, col);
                    }
                    else if (kind == 1)
                    {
                        Circle(px, size, c, ang, new Vector2(0, -rad), (6 + st.range * 2) * scale, width, col);
                    }
                    else
                    {
                        Line(px, size, c, ang, new Vector2(-rad * 0.18f, -rad * 0.55f), new Vector2(0, -rad * 1.02f), width, col);
                        Line(px, size, c, ang, new Vector2(0, -rad * 1.02f), new Vector2(rad * 0.18f, -rad * 0.55f), width, col);
                    }
                }
            }

            // Center ring (bigger with durability).
            var gold = new Color(1f, 0.79f, 0.235f, 1f);
            Circle(px, size, c, 0, Vector2.zero, (18 + st.durability * 3) * scale, 2.2f * scale, gold);

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static Vector2 Rot(Vector2 v, float a)
        {
            float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            return new Vector2(v.x * cs - v.y * sn, v.x * sn + v.y * cs);
        }

        static void Stamp(Color[] px, int size, float cx, float cy, float r, Color col)
        {
            int x0 = Mathf.FloorToInt(cx - r), x1 = Mathf.CeilToInt(cx + r);
            int y0 = Mathf.FloorToInt(cy - r), y1 = Mathf.CeilToInt(cy + r);
            for (int y = y0; y <= y1; y++)
            {
                if (y < 0 || y >= size) continue;
                for (int x = x0; x <= x1; x++)
                {
                    if (x < 0 || x >= size) continue;
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float a = Mathf.Clamp01(r - d + 0.5f) * col.a;
                    if (a <= 0) continue;
                    int i = y * size + x;
                    px[i] = Color.Lerp(px[i], new Color(col.r, col.g, col.b, 1f), a);
                }
            }
        }

        static void Segment(Color[] px, int size, float c, Vector2 a, Vector2 b, float w, Color col)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(2, Mathf.CeilToInt(len / Mathf.Max(0.5f, w * 0.4f)));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, (float)i / steps);
                Stamp(px, size, c + p.x, c - p.y, w * 0.5f, col);
            }
        }

        static void Line(Color[] px, int size, float c, float ang, Vector2 a, Vector2 b, float w, Color col)
            => Segment(px, size, c, Rot(a, ang), Rot(b, ang), w, col);

        static void Bezier(Color[] px, int size, float c, float ang, Vector2 p0, Vector2 p1, Vector2 p2, float w, Color col)
        {
            Vector2 prev = Rot(p0, ang);
            for (int i = 1; i <= 24; i++)
            {
                float t = i / 24f;
                var q = (1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * p1 + t * t * p2;
                var r = Rot(q, ang);
                Segment(px, size, c, prev, r, w, col);
                prev = r;
            }
        }

        static void Circle(Color[] px, int size, float c, float ang, Vector2 center, float radius, float w, Color col)
        {
            var cc = Rot(center, ang);
            int n = Mathf.Max(16, Mathf.CeilToInt(radius * 1.5f));
            Vector2 prev = cc + new Vector2(radius, 0);
            for (int i = 1; i <= n; i++)
            {
                float t = i * Mathf.PI * 2 / n;
                var p = cc + new Vector2(Mathf.Cos(t) * radius, Mathf.Sin(t) * radius);
                Segment(px, size, c, prev, p, w, col);
                prev = p;
            }
        }
    }
}
