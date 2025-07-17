using System;
using System.Diagnostics;
using System.Windows; // Required for Point
using IxMilia.Dxf; // Required for DxfPoint, DxfVector

namespace RobTeach.Utils
{
    public static class GeometryUtils
    {
        public static (DxfPoint Center, double Radius, double StartAngle, double EndAngle, DxfVector Normal, bool IsClockwise)? CalculateArcParametersFromThreePoints(DxfPoint p1, DxfPoint p2, DxfPoint p3, double tolerance = 1e-6)
        {
            // Implementation of 3-point to arc parameters calculation.
            // Source for algorithm idea: https://www.ambrsoft.com/TrigoCalc/Circle3D.htm and various geometry resources.

            // Check for collinearity or coincident points
            // Vector P1P2
            double v12x = p2.X - p1.X;
            double v12y = p2.Y - p1.Y;
            double v12z = p2.Z - p1.Z;

            // Vector P1P3
            double v13x = p3.X - p1.X;
            double v13y = p3.Y - p1.Y;
            double v13z = p3.Z - p1.Z;

            // Cross product (P1P2) x (P1P3)
            double crossX = v12y * v13z - v12z * v13y;
            double crossY = v12z * v13x - v12x * v13z;
            double crossZ = v12x * v13y - v12y * v13x;

            double crossLengthSq = crossX * crossX + crossY * crossY + crossZ * crossZ;
            if (crossLengthSq < tolerance * tolerance) // Points are collinear or too close
            {
                Debug.WriteLine("[WARNING] CalculateArcParametersFromThreePoints: Points are collinear or coincident.");
                return null;
            }

            DxfVector normal = new DxfVector(crossX, crossY, crossZ).Normalize();

            // Using a formula for circumcenter of a triangle in 2D (projected, assuming normal is mainly Z)
            Point pt1_2d = new Point(p1.X, p1.Y); // Using System.Windows.Point for 2D calculations
            Point pt2_2d = new Point(p2.X, p2.Y);
            Point pt3_2d = new Point(p3.X, p3.Y);

            double D_2d = 2 * (pt1_2d.X * (pt2_2d.Y - pt3_2d.Y) + pt2_2d.X * (pt3_2d.Y - pt1_2d.Y) + pt3_2d.X * (pt1_2d.Y - pt2_2d.Y));
            if (Math.Abs(D_2d) < tolerance) // Collinear in 2D projection
            {
                Debug.WriteLine("[WARNING] CalculateArcParametersFromThreePoints: Points are collinear in 2D projection.");
                return null;
            }

            double pt1Sq_2d = pt1_2d.X * pt1_2d.X + pt1_2d.Y * pt1_2d.Y;
            double pt2Sq_2d = pt2_2d.X * pt2_2d.X + pt2_2d.Y * pt2_2d.Y;
            double pt3Sq_2d = pt3_2d.X * pt3_2d.X + pt3_2d.Y * pt3_2d.Y;

            double centerX_2d = (pt1Sq_2d * (pt2_2d.Y - pt3_2d.Y) + pt2Sq_2d * (pt3_2d.Y - pt1_2d.Y) + pt3Sq_2d * (pt1_2d.Y - pt2_2d.Y)) / D_2d;
            double centerY_2d = (pt1Sq_2d * (pt3_2d.X - pt2_2d.X) + pt2Sq_2d * (pt1_2d.X - pt3_2d.X) + pt3Sq_2d * (pt2_2d.X - pt1_2d.X)) / D_2d;

            // Assuming Z is constant for the arc based on p1.Z. This is a simplification.
            // For true 3D arcs, the center's Z would be on the plane defined by p1, p2, p3.
            DxfPoint center = new DxfPoint(centerX_2d, centerY_2d, p1.Z);

            double radius = Math.Sqrt(Math.Pow(pt1_2d.X - centerX_2d, 2) + Math.Pow(pt1_2d.Y - centerY_2d, 2));

            double startAngle = Math.Atan2(p1.Y - center.Y, p1.X - center.X) * (180.0 / Math.PI);
            double midAngle = Math.Atan2(p2.Y - center.Y, p2.X - center.X) * (180.0 / Math.PI);
            double endAngle = Math.Atan2(p3.Y - center.Y, p3.X - center.X) * (180.0 / Math.PI);

            startAngle = (startAngle % 360 + 360) % 360;
            midAngle = (midAngle % 360 + 360) % 360;
            endAngle = (endAngle % 360 + 360) % 360;

            bool isClockwise;
            double sweepCCW = (endAngle - startAngle + 360) % 360;
            double midRelativeToStartCCW = (midAngle - startAngle + 360) % 360;

            if (midRelativeToStartCCW < sweepCCW)
            {
                isClockwise = false;
            }
            else
            {
                isClockwise = true;
                double tempAngle = startAngle;
                startAngle = endAngle;
                endAngle = tempAngle;
            }

            // Ensure DxfArc standard: StartAngle < EndAngle for CCW, or adjust if normal is flipped.
            // The DxfArc entity itself typically expects angles to define a CCW path if Normal is +Z.
            // The isClockwise flag helps understand the original P1->P2->P3 orientation.

            // If the geometric normal points generally in -Z, flip it and swap angles to maintain CCW interpretation for DXF.
            if (normal.Z < 0) { // This might need more robust check if normal isn't primarily along Z
                // For a general 3D arc, the "handedness" of the coordinate system defined by
                // u=(p2-p1), v=(p3-p1) and their cross product (normal) matters.
                // DxfArc always assumes CCW sweep from start to end angle in its own coordinate system (defined by its Normal).
                // The returned normal should be consistent with the Start/End angles for a CCW sweep.
                // If our calculated normal (from p1,p2,p3 cross product) implies a CW sweep for p1->p2->p3 with given angles,
                // we might need to flip the normal OR adjust angles.
                // For simplicity, if normal.Z < 0 for an XY dominant plane, we flip normal and swap angles.
                // This part of arc definition can be tricky.
            }

            return (center, radius, startAngle, endAngle, normal, isClockwise);
        }

        /// <summary>
        /// Calculates the center, radius, and normal of a circle defined by three distinct, non-collinear 3D points.
        /// </summary>
        /// <param name="p1">First point on the circle.</param>
        /// <param name="p2">Second point on the circle.</param>
        /// <param name="p3">Third point on the circle.</param>
        /// <param name="tolerance">Tolerance for floating point comparisons and collinearity checks.</param>
        /// <returns>A tuple (Center, Radius, Normal) or null if points are collinear or calculation fails.</returns>
        public static (DxfPoint Center, double Radius, DxfVector Normal)?
            CalculateCircleCenterRadiusFromThreePoints(DxfPoint p1, DxfPoint p2, DxfPoint p3, double tolerance = 1e-9) // Using a slightly higher precision tolerance internally
        {
            DxfVector v12 = p2 - p1;
            DxfVector v13 = p3 - p1;

            DxfVector normal = v12.Cross(v13);
            double normalLengthSq = normal.LengthSquared;

            if (normalLengthSq < tolerance * tolerance) // Points are collinear
            {
                Debug.WriteLine($"[JULES_DEBUG] GeometryUtils.CalculateCircleCenterRadiusFromThreePoints: Points P1={p1}, P2={p2}, P3={p3} are collinear (normal vector zero or too small). normalLengthSq={normalLengthSq}");
                return null;
            }
            normal = normal.Normalize(); // Normalize for consistent direction

            // Using Eric Lengyel's formula for circumcenter of a 3D triangle (from "Mathematics for 3D Game Programming and Computer Graphics")
            // Let p1, p2, p3 be the points.
            // Define vectors from one point, e.g., ab = p2 - p1, ac = p3 - p1
            DxfVector ab = p2 - p1;
            DxfVector ac = p3 - p1;

            // Denominator part: 2 * |ab x ac|^2
            // ab_cross_ac is normal * (some length scalar related to area*2)
            // normalLengthSq = |ab x ac|^2
            double denominator = 2.0 * normalLengthSq;
            // Already checked normalLengthSq for being too small (collinearity)

            // Numerator part: ( |ac|^2 * (ab - ac)·ab * ab - |ab|^2 * (ac - ab)·ac * ac ) -- this is not Lengyel's formula directly
            // Lengyel's formula: ( (ac * ab_sq - ab * ac_sq) X (ab X ac) ) / (2 * |ab X ac|^2) + p1
            // where ab_sq = ab.LengthSquared, ac_sq = ac.LengthSquared
            double ab_len_sq = ab.LengthSquared;
            double ac_len_sq = ac.LengthSquared;

            DxfVector term1_vec = new DxfVector(ac.X * ab_len_sq, ac.Y * ab_len_sq, ac.Z * ab_len_sq);
            DxfVector term2_vec = new DxfVector(ab.X * ac_len_sq, ab.Y * ac_len_sq, ab.Z * ac_len_sq);
            DxfVector diff_terms = term1_vec - term2_vec; // This is (ac * |ab|^2 - ab * |ac|^2)

            DxfVector ab_cross_ac_not_normalized = v12.Cross(v13); // This is the same as normal * Sqrt(normalLengthSq)

            DxfVector numerator_cross_product = diff_terms.Cross(ab_cross_ac_not_normalized);

            // Ensure denominator is not zero before division (already checked by normalLengthSq check earlier)
            DxfVector scaled_numerator_cross_product = new DxfVector(
                numerator_cross_product.X / denominator,
                numerator_cross_product.Y / denominator,
                numerator_cross_product.Z / denominator);
            DxfPoint center = p1 + scaled_numerator_cross_product;
            double radius = (p1 - center).Length;

            if (radius < tolerance) // Or some other check if radius is unreasonably small
            {
                Debug.WriteLine($"[JULES_DEBUG] GeometryUtils.CalculateCircleCenterRadiusFromThreePoints: Calculated radius {radius} is too small. P1={p1}, P2={p2}, P3={p3}. Center={center}");
                return null;
            }

            // Sanity check if other points are equidistant (using a relative tolerance based on radius)
            double relTol = radius * 0.001; // 0.1% relative tolerance
            if (Math.Abs((p2 - center).Length - radius) > relTol ||
                Math.Abs((p3 - center).Length - radius) > relTol)
            {
                Debug.WriteLine($"[JULES_WARNING] GeometryUtils.CalculateCircleCenterRadiusFromThreePoints: Points not perfectly equidistant from calculated center. This might indicate input points are not truly on a circle or numerical precision limits.\n" +
                                $"P1={p1}, P2={p2}, P3={p3}\n" +
                                $"Center={center}, Radius={radius}\n" +
                                $"Dist P1-C: {(p1-center).Length}\n" +
                                $"Dist P2-C: {(p2-center).Length}\n" +
                                $"Dist P3-C: {(p3-center).Length}");
                // Depending on strictness, one might return null here.
                // For now, we proceed, as the calculation itself is standard.
            }

            Debug.WriteLine($"[JULES_DEBUG] GeometryUtils.CalculateCircleCenterRadiusFromThreePoints: P1={p1}, P2={p2}, P3={p3} -> Center={center}, Radius={radius}, Normal={normal}");
            return (center, radius, normal);
        }

        public static (DxfPoint p1, DxfPoint p2, DxfPoint p3)? ConvertBulgeToArcPoints(DxfPoint start, DxfPoint end, double bulge)
        {
            if (Math.Abs(bulge) < 1e-6) return null;

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double chord = Math.Sqrt(dx * dx + dy * dy);

            double sagitta = bulge * chord / 2;
            double radius = (sagitta * sagitta + chord * chord / 4) / (2 * sagitta);
            double angle = 4 * Math.Atan(bulge);

            double midX = start.X + dx / 2;
            double midY = start.Y + dy / 2;

            double perpDx = -dy;
            double perpDy = dx;
            double perpLen = Math.Sqrt(perpDx * perpDx + perpDy * perpDy);
            perpDx /= perpLen;
            perpDy /= perpLen;

            double centerX = midX - (radius - sagitta) * perpDx;
            double centerY = midY - (radius - sagitta) * perpDy;

            double startAngle = Math.Atan2(start.Y - centerY, start.X - centerX);
            double endAngle = Math.Atan2(end.Y - centerY, end.X - centerX);

            if (bulge < 0)
            {
                var temp = startAngle;
                startAngle = endAngle;
                endAngle = temp;
            }

            if (endAngle < startAngle)
            {
                endAngle += 2 * Math.PI;
            }

            double midAngle = (startAngle + endAngle) / 2;

            DxfPoint p1 = start;
            DxfPoint p2 = new DxfPoint(centerX + radius * Math.Cos(midAngle), centerY + radius * Math.Sin(midAngle), start.Z);
            DxfPoint p3 = end;

            return (p1, p2, p3);
        }

        public static double DistanceTo(Models.Point3D p1, Models.Point3D p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double dz = p2.Z - p1.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
