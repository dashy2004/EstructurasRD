using System;
using System.Numerics;
using Avalonia;
using LosasPlus.Models;
using LosasPlus.Vigas;

namespace LosasPlus.Views;

public static class PlantaSnapEngine
{
    public struct SnapResult
    {
        public double X;
        public double Y;
        public bool IsSnapped;
        public string Description;
    }

    public static SnapResult CalculateSnap(
        double rawX, double rawY,
        Nivel? nivel,
        bool isSnappingEnabled,
        double gridStep,
        double searchRadiusMeters)
    {
        // 1. If snapping is disabled, return raw coordinates
        if (!isSnappingEnabled)
        {
            return new SnapResult { X = rawX, Y = rawY, IsSnapped = false, Description = "Libre" };
        }

        // 2. Entity snap
        if (nivel != null)
        {
            double bestDist = double.MaxValue;
            SnapResult bestSnap = default;

            // --- PASS 1: Point Snaps (highest priority) ---

            // Columns (Centers)
            foreach (var col in nivel.Columnas)
            {
                double dx = rawX - col.CoordenadaX;
                double dy = rawY - col.CoordenadaY;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < searchRadiusMeters && d < bestDist)
                {
                    bestDist = d;
                    bestSnap = new SnapResult
                    {
                        X = col.CoordenadaX,
                        Y = col.CoordenadaY,
                        IsSnapped = true,
                        Description = $"Centro de {col.Nombre}"
                    };
                }
            }

            // Beams (Endpoints: Origen & Extremo)
            foreach (var viga in nivel.Vigas)
            {
                // Origen
                double dx = rawX - viga.OrigenX;
                double dy = rawY - viga.OrigenY;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < searchRadiusMeters && d < bestDist)
                {
                    bestDist = d;
                    bestSnap = new SnapResult
                    {
                        X = viga.OrigenX,
                        Y = viga.OrigenY,
                        IsSnapped = true,
                        Description = $"Origen de {viga.Nombre}"
                    };
                }

                // Extremo
                double ex = viga.ExtremoX;
                double ey = viga.ExtremoY;
                dx = rawX - ex;
                dy = rawY - ey;
                d = Math.Sqrt(dx * dx + dy * dy);
                if (d < searchRadiusMeters && d < bestDist)
                {
                    bestDist = d;
                    bestSnap = new SnapResult
                    {
                        X = ex,
                        Y = ey,
                        IsSnapped = true,
                        Description = $"Extremo de {viga.Nombre}"
                    };
                }
            }

            // Slabs (Vertices)
            foreach (var sys in nivel.Sistemas)
            {
                foreach (var losa in sys.Losas)
                {
                    double x0 = losa.CoordenadaX;
                    double y0 = losa.CoordenadaY;
                    double lx = losa.Lx;
                    double ly = losa.Ly;

                    double[] xs = { x0, x0 + lx, x0 + lx, x0 };
                    double[] ys = { y0, y0, y0 + ly, y0 + ly };

                    for (int i = 0; i < 4; i++)
                    {
                        double dx = rawX - xs[i];
                        double dy = rawY - ys[i];
                        double d = Math.Sqrt(dx * dx + dy * dy);
                        if (d < searchRadiusMeters && d < bestDist)
                        {
                            bestDist = d;
                            bestSnap = new SnapResult
                            {
                                X = xs[i],
                                Y = ys[i],
                                IsSnapped = true,
                                Description = $"Vértice Losa {losa.Id}"
                            };
                        }
                    }
                }
            }

            // --- PASS 2: Line/Axis Snaps (only if no point snap was hit) ---
            if (!bestSnap.IsSnapped)
            {
                foreach (var viga in nivel.Vigas)
                {
                    double ex = viga.ExtremoX;
                    double ey = viga.ExtremoY;
                    var pStart = new Point(viga.OrigenX, viga.OrigenY);
                    var pEnd = new Point(ex, ey);
                    double l2 = (pStart.X - pEnd.X) * (pStart.X - pEnd.X) + (pStart.Y - pEnd.Y) * (pStart.Y - pEnd.Y);
                    if (l2 > 0)
                    {
                        double t = ((rawX - pStart.X) * (pEnd.X - pStart.X) + (rawY - pStart.Y) * (pEnd.Y - pStart.Y)) / l2;
                        t = Math.Clamp(t, 0.0, 1.0);
                        double prx = pStart.X + t * (pEnd.X - pStart.X);
                        double pry = pStart.Y + t * (pEnd.Y - pStart.Y);
                        double dx = rawX - prx;
                        double dy = rawY - pry;
                        double d = Math.Sqrt(dx * dx + dy * dy);
                        if (d < searchRadiusMeters && d < bestDist)
                        {
                            bestDist = d;
                            bestSnap = new SnapResult
                            {
                                X = prx,
                                Y = pry,
                                IsSnapped = true,
                                Description = $"Eje de {viga.Nombre}"
                            };
                        }
                    }
                }
            }

            if (bestSnap.IsSnapped)
            {
                return bestSnap;
            }
        }

        // 3. Grid snap
        if (gridStep > 0.0)
        {
            double snappedX = Math.Round(rawX / gridStep) * gridStep;
            double snappedY = Math.Round(rawY / gridStep) * gridStep;
            return new SnapResult
            {
                X = snappedX,
                Y = snappedY,
                IsSnapped = true,
                Description = $"Rejilla ({gridStep:0.##}m)"
            };
        }

        return new SnapResult { X = rawX, Y = rawY, IsSnapped = false, Description = "Libre" };
    }
}
