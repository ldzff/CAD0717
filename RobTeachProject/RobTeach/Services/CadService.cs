using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Linq;
using RobTeach.Models;
using RobTeach.Utils; // For AppLogger

namespace RobTeach.Services
{
    public class CadService
    {
        public DxfFile LoadDxf(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException(nameof(filePath), "File path cannot be null or empty.");
            }
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("DXF file not found.", filePath);
            }
            
            try
            {
                DxfFile dxf = DxfFile.Load(filePath);
                return dxf;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading or parsing DXF file: {ex.Message}", ex);
            }
        }
        
        public List<System.Windows.Shapes.Shape?> GetWpfShapesFromDxf(DxfFile dxfFile) // Allow null shapes in list
        {
            var wpfShapes = new List<System.Windows.Shapes.Shape?>(); // Allow null shapes in list
            if (dxfFile == null)
            {
                AppLogger.Log("[CadService] GetWpfShapesFromDxf: dxfFile is null. Returning empty list.", LogLevel.Warning);
                return wpfShapes;
            }
            AppLogger.Log($"[CadService] GetWpfShapesFromDxf: Processing {dxfFile.Entities.Count()} entities from DXF document.", LogLevel.Debug);
            int entityCounter = 0;

            foreach (DxfEntity entity in dxfFile.Entities) // Ensure entity is typed as DxfEntity for direct Handle access
            {
                System.Windows.Shapes.Shape? wpfShape = null;
                AppLogger.Log($"[CadService] GetWpfShapesFromDxf: Processing entity at index {entityCounter} (C# Type: {entity.GetType().Name}, Layer: {entity.Layer}).", LogLevel.Debug);

                try
                {
                    switch (entity)
                    {
                        case DxfLine dxfLine:
                            wpfShape = new System.Windows.Shapes.Line
                            {
                                X1 = dxfLine.P1.X,
                                Y1 = dxfLine.P1.Y,
                                X2 = dxfLine.P2.X,
                                Y2 = dxfLine.P2.Y,
                                IsHitTestVisible = true
                            };
                            AppLogger.Log($"[CadService]   Converted DxfLine ({dxfLine.P1.X:F2},{dxfLine.P1.Y:F2}) to ({dxfLine.P2.X:F2},{dxfLine.P2.Y:F2})", LogLevel.Debug);
                            break;

                        case DxfArc dxfArc:
                            wpfShape = CreateArcPath(dxfArc);
                            if (wpfShape != null)
                                AppLogger.Log($"[CadService]   Converted DxfArc (Center:{dxfArc.Center.X:F2},{dxfArc.Center.Y:F2}, R:{dxfArc.Radius:F2}, Start:{dxfArc.StartAngle:F2}, End:{dxfArc.EndAngle:F2})", LogLevel.Debug);
                            else
                                AppLogger.Log($"[CadService]   FAILED to convert DxfArc.", LogLevel.Warning);
                            break;

                        case DxfCircle dxfCircle:
                            var ellipseGeometry = new EllipseGeometry(
                                new System.Windows.Point(dxfCircle.Center.X, dxfCircle.Center.Y),
                                dxfCircle.Radius,
                                dxfCircle.Radius
                            );
                            wpfShape = new System.Windows.Shapes.Path
                            {
                                Data = ellipseGeometry,
                                Fill = Brushes.Transparent,
                                IsHitTestVisible = true
                            };
                            AppLogger.Log($"[CadService]   Converted DxfCircle (Center:{dxfCircle.Center.X:F2},{dxfCircle.Center.Y:F2}, R:{dxfCircle.Radius:F2})", LogLevel.Debug);
                            break;

                        case DxfLwPolyline lwPoly:
                            wpfShape = ConvertLwPolylineToWpfPath(lwPoly);
                            if(wpfShape != null)
                                AppLogger.Log($"[CadService]   Converted DxfLwPolyline with {lwPoly.Vertices.Count} vertices", LogLevel.Debug);
                            else
                                AppLogger.Log($"[CadService]   FAILED to convert DxfLwPolyline.", LogLevel.Warning);
                            break;

                        case DxfInsert insert:
                            // Handle block insertions
                            var block = dxfFile.Blocks.FirstOrDefault(b => b.Name == insert.Name);
                            if (block != null)
                            {
                                foreach (var blockEntity in block.Entities)
                                {
                                    var transformedEntity = TransformBlockEntity(blockEntity, insert);
                                    if (transformedEntity != null)
                                    {
                                        var tempFile = new DxfFile();
                                        tempFile.Entities.Add(transformedEntity);
                                        var blockShape = GetWpfShapesFromDxf(tempFile).FirstOrDefault();
                                        if (blockShape != null)
                                        {
                                            wpfShapes.Add(blockShape);
                                            AppLogger.Log($"[CadService]   Converted block entity {blockEntity.GetType().Name}", LogLevel.Debug);
                                        }
                                    }
                                }
                                wpfShape = null; // Skip adding the insert itself since we added its contents
                            }
                            break;

                        default:
                            AppLogger.Log($"[CadService]   EntityType '{entity.GetType().Name}' not supported for WPF shape conversion.", LogLevel.Debug);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[CadService] Error converting entity {entity.GetType().Name}: {ex.Message}", ex, LogLevel.Error);
                    wpfShape = null;
                }

                if (wpfShape != null)
                {
                    wpfShapes.Add(wpfShape);
                }
                entityCounter++;
            }
            AppLogger.Log($"[CadService] GetWpfShapesFromDxf: Finished processing. Returning list with {wpfShapes.Count} elements.", LogLevel.Debug);
            return wpfShapes;
        }

        private System.Windows.Shapes.Path? CreateArcPath(DxfArc dxfArc)
        {
            if (dxfArc == null) {
                AppLogger.Log("[CadService] CreateArcPath: Input DxfArc is null.", LogLevel.Warning);
                return null;
            }
            try
            {
                double startAngleRad = dxfArc.StartAngle * Math.PI / 180.0;
                double endAngleRad = dxfArc.EndAngle * Math.PI / 180.0;
                
                double arcStartX = dxfArc.Center.X + dxfArc.Radius * Math.Cos(startAngleRad);
                double arcStartY = dxfArc.Center.Y + dxfArc.Radius * Math.Sin(startAngleRad);
                var pathStartPoint = new System.Windows.Point(arcStartX, arcStartY);

                double arcEndX = dxfArc.Center.X + dxfArc.Radius * Math.Cos(endAngleRad);
                double arcEndY = dxfArc.Center.Y + dxfArc.Radius * Math.Sin(endAngleRad);
                var arcSegmentEndPoint = new System.Windows.Point(arcEndX, arcEndY);

                double sweepAngleDegrees = dxfArc.EndAngle - dxfArc.StartAngle;
                if (sweepAngleDegrees < 0) sweepAngleDegrees += 360;
                // Ensure sweep is not exactly 0 or 360 if start and end are same, which can happen for full circles passed as arcs
                if (Math.Abs(sweepAngleDegrees) < 0.001) sweepAngleDegrees = 360;

                bool isLargeArc = sweepAngleDegrees > 180.0;
                SweepDirection sweepDirection = SweepDirection.Counterclockwise; // DXF arcs are CCW by convention

                ArcSegment arcSegment = new ArcSegment
                {
                    Point = arcSegmentEndPoint,
                    Size = new System.Windows.Size(dxfArc.Radius, dxfArc.Radius),
                    IsLargeArc = isLargeArc,
                    SweepDirection = sweepDirection,
                    RotationAngle = 0, // DXF Arcs are circular, no rotation of ellipse axes
                    IsStroked = true
                };

                PathFigure pathFigure = new PathFigure
                {
                    StartPoint = pathStartPoint,
                    IsClosed = false // Arcs are not closed paths by definition
                };
                pathFigure.Segments.Add(arcSegment);

                PathGeometry pathGeometry = new PathGeometry();
                pathGeometry.Figures.Add(pathFigure);

                AppLogger.Log($"[CadService] CreateArcPath: Created arc path with start angle {dxfArc.StartAngle:F2}, end angle {dxfArc.EndAngle:F2}, sweep angle {sweepAngleDegrees:F2}, isLargeArc {isLargeArc}, sweepDirection {sweepDirection}", LogLevel.Debug);

                return new System.Windows.Shapes.Path
                {
                    Data = pathGeometry,
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = true
                };
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[CadService] CreateArcPath: Error creating arc path: {ex.Message}", ex, LogLevel.Error);
                return null;
            }
        }

        private System.Windows.Shapes.Path? ConvertLwPolylineToWpfPath(DxfLwPolyline lwPolyline)
        {
            if (lwPolyline == null || lwPolyline.Vertices.Count < 2)
            {
                AppLogger.Log("[CadService] ConvertLwPolylineToWpfPath: Invalid polyline (null or less than 2 vertices).", LogLevel.Warning);
                return null;
            }

            try
            {
                PathGeometry pathGeometry = new PathGeometry();
                PathFigure pathFigure = new PathFigure();
                pathFigure.StartPoint = new System.Windows.Point(lwPolyline.Vertices[0].X, lwPolyline.Vertices[0].Y);
                pathFigure.IsClosed = lwPolyline.IsClosed;

                for (int i = 1; i < lwPolyline.Vertices.Count; i++)
                {
                    var currentVertex = lwPolyline.Vertices[i];
                    var previousVertex = lwPolyline.Vertices[i - 1];
                    var bulge = previousVertex.Bulge;

                    if (Math.Abs(bulge) < 0.001)
                    {
                        // Straight line segment
                        var lineSegment = new LineSegment(
                            new System.Windows.Point(currentVertex.X, currentVertex.Y),
                            true // IsStroked
                        );
                        pathFigure.Segments.Add(lineSegment);
                        AppLogger.Log($"[CadService] ConvertLwPolylineToWpfPath: Added line segment from ({previousVertex.X:F2},{previousVertex.Y:F2}) to ({currentVertex.X:F2},{currentVertex.Y:F2})", LogLevel.Debug);
                    }
                    else
                    {
                        // Arc segment
                        var p1 = new System.Windows.Point(previousVertex.X, previousVertex.Y);
                        var p2 = new System.Windows.Point(currentVertex.X, currentVertex.Y);
                        var arcSegment = CalculateArcSegmentFromBulge(p1, p2, bulge);
                        if (arcSegment != null)
                        {
                            pathFigure.Segments.Add(arcSegment);
                            AppLogger.Log($"[CadService] ConvertLwPolylineToWpfPath: Added arc segment from ({previousVertex.X:F2},{previousVertex.Y:F2}) to ({currentVertex.X:F2},{currentVertex.Y:F2}) with bulge {bulge:F3}", LogLevel.Debug);
                        }
                        else
                        {
                            // Fallback to line segment if arc calculation fails
                            var lineSegment = new LineSegment(
                                new System.Windows.Point(currentVertex.X, currentVertex.Y),
                                true // IsStroked
                            );
                            pathFigure.Segments.Add(lineSegment);
                            AppLogger.Log($"[CadService] ConvertLwPolylineToWpfPath: Failed to create arc segment, using line segment instead from ({previousVertex.X:F2},{previousVertex.Y:F2}) to ({currentVertex.X:F2},{currentVertex.Y:F2})", LogLevel.Warning);
                        }
                    }
                }

                // Handle closing segment if polyline is closed
                if (lwPolyline.IsClosed && lwPolyline.Vertices.Count > 0)
                {
                    var lastVertex = lwPolyline.Vertices[lwPolyline.Vertices.Count - 1];
                    var firstVertex = lwPolyline.Vertices[0];
                    var bulge = lastVertex.Bulge;

                    if (Math.Abs(bulge) < 0.001)
                    {
                        // Straight closing segment
                        var lineSegment = new LineSegment(
                            new System.Windows.Point(firstVertex.X, firstVertex.Y),
                            true // IsStroked
                        );
                        pathFigure.Segments.Add(lineSegment);
                        AppLogger.Log($"[CadService] ConvertLwPolylineToWpfPath: Added closing line segment from ({lastVertex.X:F2},{lastVertex.Y:F2}) to ({firstVertex.X:F2},{firstVertex.Y:F2})", LogLevel.Debug);
                    }
                    else
                    {
                        // Arc closing segment
                        var p1 = new System.Windows.Point(lastVertex.X, lastVertex.Y);
                        var p2 = new System.Windows.Point(firstVertex.X, firstVertex.Y);
                        var arcSegment = CalculateArcSegmentFromBulge(p1, p2, bulge);
                        if (arcSegment != null)
                        {
                            pathFigure.Segments.Add(arcSegment);
                            AppLogger.Log($"[CadService] ConvertLwPolylineToWpfPath: Added closing arc segment from ({lastVertex.X:F2},{lastVertex.Y:F2}) to ({firstVertex.X:F2},{firstVertex.Y:F2}) with bulge {bulge:F3}", LogLevel.Debug);
                        }
                        else
                        {
                            // Fallback to line segment if arc calculation fails
                            var lineSegment = new LineSegment(
                                new System.Windows.Point(firstVertex.X, firstVertex.Y),
                                true // IsStroked
                            );
                            pathFigure.Segments.Add(lineSegment);
                            AppLogger.Log($"[CadService] ConvertLwPolylineToWpfPath: Failed to create closing arc segment, using line segment instead from ({lastVertex.X:F2},{lastVertex.Y:F2}) to ({firstVertex.X:F2},{firstVertex.Y:F2})", LogLevel.Warning);
                        }
                    }
                }

                pathGeometry.Figures.Add(pathFigure);

                return new System.Windows.Shapes.Path
                {
                    Data = pathGeometry,
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = true
                };
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[CadService] ConvertLwPolylineToWpfPath: Error converting polyline: {ex.Message}", ex, LogLevel.Error);
                return null;
            }
        }

    private ArcSegment? CalculateArcSegmentFromBulge(System.Windows.Point p1, System.Windows.Point p2, double bulge)
    {
        try
        {
            if (Math.Abs(bulge) < 0.001)
            {
                AppLogger.Log($"[CadService] CalculateArcSegmentFromBulge: Bulge too small ({bulge:F6}), should use line segment instead.", LogLevel.Debug);
                return null;
            }

            // Calculate arc parameters from bulge
            double chordLength = Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
            if (chordLength < 0.001)
            {
                AppLogger.Log($"[CadService] CalculateArcSegmentFromBulge: Chord length too small ({chordLength:F6}), points too close.", LogLevel.Debug);
                return null;
            }

            double theta = 4 * Math.Atan(Math.Abs(bulge)); // Included angle
            double radius = chordLength * (1 + bulge * bulge) / (4 * Math.Abs(bulge));
            bool isLargeArc = theta > Math.PI;
            SweepDirection sweepDirection = bulge > 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;

            AppLogger.Log($"[CadService] CalculateArcSegmentFromBulge: Calculated parameters - Chord length: {chordLength:F3}, Theta: {theta * 180 / Math.PI:F2}°, Radius: {radius:F3}, IsLargeArc: {isLargeArc}, SweepDirection: {sweepDirection}", LogLevel.Debug);

            return new ArcSegment
            {
                Point = p2,
                Size = new Size(radius, radius),
                IsLargeArc = isLargeArc,
                SweepDirection = sweepDirection,
                RotationAngle = 0,
                IsStroked = true
            };
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[CadService] CalculateArcSegmentFromBulge: Error calculating arc segment: {ex.Message}", ex, LogLevel.Error);
            return null;
        }
    }

        // Method stubs for trajectory point conversion - to be reviewed/completed if needed by other parts
        public List<System.Windows.Point> ConvertLineToPoints(DxfLine line)
        {
            var points = new List<System.Windows.Point>();
            if (line == null) return points;
            points.Add(new System.Windows.Point(line.P1.X, line.P1.Y));
            points.Add(new System.Windows.Point(line.P2.X, line.P2.Y));
            return points;
        }

        public List<System.Windows.Point> ConvertArcToPoints(DxfArc arc, double resolutionDegrees)
        {
            var points = new List<System.Windows.Point>();
            if (arc == null || resolutionDegrees <= 0) return points;
            // ... (implementation as before) ...
            double startAngle = arc.StartAngle;
            double endAngle = arc.EndAngle;
            double radius = arc.Radius;
            System.Windows.Point center = new System.Windows.Point(arc.Center.X, arc.Center.Y);
            if (endAngle < startAngle) endAngle += 360;
            double currentAngle = startAngle;
            while (currentAngle <= endAngle)
            {
                double radAngle = currentAngle * Math.PI / 180.0;
                double x = center.X + radius * Math.Cos(radAngle);
                double y = center.Y + radius * Math.Sin(radAngle);
                points.Add(new System.Windows.Point(x, y));
                currentAngle += resolutionDegrees;
            }
            if (Math.Abs(currentAngle - resolutionDegrees - endAngle) > 0.001)
            {
                double endRadAngle = endAngle * Math.PI / 180.0;
                points.Add(new System.Windows.Point(center.X + radius * Math.Cos(endRadAngle), center.Y + radius * Math.Sin(endRadAngle)));
            }
            return points;
        }

        public List<System.Windows.Point> ConvertCircleToPoints(DxfCircle circle, double resolutionDegrees)
        {
            List<System.Windows.Point> points = new List<System.Windows.Point>();
            if (circle == null || resolutionDegrees <= 0) return points;
            for (double angle = 0; angle < 360.0; angle += resolutionDegrees)
            {
                double radAngle = angle * Math.PI / 180.0;
                double x = circle.Center.X + circle.Radius * Math.Cos(radAngle);
                double y = circle.Center.Y + circle.Radius * Math.Sin(radAngle);
                points.Add(new System.Windows.Point(x, y));
            }
             if (points.Count > 0) points.Add(points[0]); // Close the circle
            return points;
        }
        public List<System.Windows.Point> ConvertLineTrajectoryToPoints(Trajectory trajectory)
        {
            var points = new List<System.Windows.Point>();
            if (trajectory == null || trajectory.PrimitiveType != "Line") return points;
            DxfPoint start = trajectory.LineStartPoint;
            DxfPoint end = trajectory.LineEndPoint;
            if (trajectory.IsReversed) { points.Add(new System.Windows.Point(end.X, end.Y)); points.Add(new System.Windows.Point(start.X, start.Y)); }
            else { points.Add(new System.Windows.Point(start.X, start.Y)); points.Add(new System.Windows.Point(end.X, end.Y)); }
            return points;
        }
        public List<System.Windows.Point> ConvertArcTrajectoryToPoints(Trajectory trajectory, double resolutionDegrees)
        {
            var points = new List<System.Windows.Point>();
            // ... (implementation as before, needs robust 3-point to arc param logic if OriginalDxfEntity is not DxfArc) ...
            if (trajectory == null || trajectory.PrimitiveType != "Arc" || resolutionDegrees <= 0) return points;
            if (trajectory.OriginalDxfEntity is DxfArc dxfArc) {
                double startAngle = dxfArc.StartAngle;
                double endAngle = dxfArc.EndAngle;
                if (trajectory.IsReversed) { double temp = startAngle; startAngle = endAngle; endAngle = temp; }
                if (endAngle < startAngle) endAngle += 360.0;
                for (double currentAngle = startAngle; currentAngle <= endAngle; currentAngle += resolutionDegrees) {
                    double rad = currentAngle * Math.PI / 180.0;
                    points.Add(new System.Windows.Point(dxfArc.Center.X + dxfArc.Radius * Math.Cos(rad), dxfArc.Center.Y + dxfArc.Radius * Math.Sin(rad)));
                }
                double finalRad = endAngle * Math.PI / 180.0;
                points.Add(new System.Windows.Point(dxfArc.Center.X + dxfArc.Radius * Math.Cos(finalRad), dxfArc.Center.Y + dxfArc.Radius * Math.Sin(finalRad)));

            } else { AppLogger.Log($"[CadService] ConvertArcTrajectoryToPoints: Trajectory '{trajectory.ToString()}' OriginalDxfEntity is not a DxfArc or is null.", LogLevel.Warning); }
            return points;
        }
        public List<System.Windows.Point> ConvertCircleTrajectoryToPoints(Trajectory trajectory, double resolutionDegrees)
        {
            AppLogger.Log("[CadService] ConvertCircleTrajectoryToPoints called - Note: This method is obsolete for point generation; MainWindow.PopulateTrajectoryPoints should be used.", LogLevel.Debug);
            return new List<System.Windows.Point>(); // Obsolete
        }

        private DxfEntity? TransformBlockEntity(DxfEntity entity, DxfInsert insert)
        {
            try
            {
                DxfEntity transformed;
                switch (entity)
                {
                    case DxfLwPolyline poly:
                        var newVertices = new List<DxfLwPolylineVertex>();
                        foreach (var vertex in poly.Vertices)
                        {
                            var point = new DxfPoint(vertex.X, vertex.Y, 0);
                            var transformed_point = TransformPoint(point, insert);
                            var newVertex = new DxfLwPolylineVertex();
                            newVertex.X = transformed_point.X;
                            newVertex.Y = transformed_point.Y;
                            newVertex.Bulge = vertex.Bulge * (insert.XScaleFactor < 0 ? -1 : 1); // Flip bulge if X scale is negative
                            newVertices.Add(newVertex);
                        }
                        transformed = new DxfLwPolyline(newVertices);
                        break;
                    case DxfArc arc:
                        transformed = new DxfArc(
                            TransformPoint(arc.Center, insert),
                            arc.Radius * Math.Abs(insert.XScaleFactor), // Use absolute scale value
                            arc.StartAngle + insert.Rotation, // Add rotation to angles
                            arc.EndAngle + insert.Rotation
                        );
                        break;
                    case DxfCircle circle:
                        transformed = new DxfCircle(
                            TransformPoint(circle.Center, insert),
                            circle.Radius * Math.Abs(insert.XScaleFactor) // Use absolute scale value
                        );
                        break;
                    case DxfLine line:
                        transformed = new DxfLine(
                            TransformPoint(line.P1, insert),
                            TransformPoint(line.P2, insert)
                        );
                        break;
                    default:
                        return null;
                }
                transformed.Layer = entity.Layer;
                return transformed;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[CadService] TransformBlockEntity: Error transforming block entity: {ex.Message}", ex, LogLevel.Error);
                return null;
            }
        }

        private DxfPoint TransformPoint(DxfPoint point, DxfInsert insert)
        {
            try
            {
                // Apply scaling
                double x = point.X * insert.XScaleFactor;
                double y = point.Y * insert.YScaleFactor;

                // Apply rotation
                double angleRad = insert.Rotation * Math.PI / 180.0;
                double cos = Math.Cos(angleRad);
                double sin = Math.Sin(angleRad);
                double rotatedX = x * cos - y * sin;
                double rotatedY = x * sin + y * cos;

                // Apply translation
                return new DxfPoint(
                    rotatedX + insert.Location.X,
                    rotatedY + insert.Location.Y,
                    point.Z + insert.Location.Z
                );
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[CadService] TransformPoint: Error transforming point: {ex.Message}", ex, LogLevel.Error);
                return point; // Return original point on error
            }
        }
    }
}
