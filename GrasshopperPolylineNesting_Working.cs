using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using Rhino;
using Rhino.Geometry;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

/// <summary>
/// Grasshopper C# Script Component for Polyline Nesting
/// Fixed version with proper Grasshopper integration
///
/// SETUP INSTRUCTIONS:
/// 1. Create a C# Script component in Grasshopper
/// 2. Copy this entire code into the component
/// 3. Right-click component > Manage Input Parameters:
///    - Polylines (Curve, List Access)
///    - SheetWidth (Number, Item Access) - Default: 1200
///    - SheetHeight (Number, Item Access) - Default: 800
///    - Margin (Number, Item Access) - Default: 5
///    - Spacing (Number, Item Access) - Default: 2
///    - SortStrategy (Integer, Item Access) - Default: 0
///    - PlacementStrategy (Integer, Item Access) - Default: 0
///    - RotationMode (Integer, Item Access) - Default: 1
///    - RotationStep (Number, Item Access) - Default: 15
///    - GridResolution (Number, Item Access) - Default: 1
///    - Tags (Text, List Access) - Optional
///    - Run (Boolean, Item Access) - Default: False
///
/// 4. Right-click component > Manage Output Parameters:
///    - PlacedPolylines (Geometry)
///    - BoundingBoxes (Geometry)
///    - Sheets (Geometry)
///    - Margins (Geometry)
///    - Info (Text)
///    - Statistics (Text)
///    - Warnings (Text)
///    - SheetCount (Integer)
///    - Colors (Colour)
/// </summary>

public class Script_Instance : GH_ScriptInstance
{
    #region Algorithm Enumerations

    public enum PolylineSortStrategy
    {
        AreaDescending = 0,
        AreaAscending = 1,
        LargestDimensionFirst = 2,
        PerimeterDescending = 3,
        ComplexityDescending = 4
    }

    public enum PlacementStrategy
    {
        BottomLeft = 0,
        BestFit = 1,
        Grid = 2
    }

    public enum RotationMode
    {
        None = 0,
        Rotation90 = 1,
        Rotation45 = 2,
        CustomStep = 3
    }

    #endregion

    #region Core Classes

    public class PolylineItem
    {
        public Polyline Geometry { get; set; }
        public int Id { get; set; }
        public string Tag { get; set; }
        public RotationMode AllowedRotation { get; set; }
        public BoundingBox BoundingBox { get; private set; }
        public double Area { get; private set; }
        public double Perimeter { get; private set; }
        public double MaxDimension { get; private set; }

        public PolylineItem(Polyline polyline, int id = 0, string tag = "", RotationMode rotation = RotationMode.Rotation90)
        {
            Geometry = polyline;
            Id = id;
            Tag = tag;
            AllowedRotation = rotation;
            BoundingBox = polyline.BoundingBox;
            Area = CalculateArea();
            Perimeter = polyline.Length;
            MaxDimension = Math.Max(
                BoundingBox.Max.X - BoundingBox.Min.X,
                BoundingBox.Max.Y - BoundingBox.Min.Y
            );
        }

        private double CalculateArea()
        {
            // Shoelace formula for polygon area
            double area = 0;
            for (int i = 0; i < Geometry.Count - 1; i++)
            {
                area += (Geometry[i].X * Geometry[i + 1].Y) - (Geometry[i + 1].X * Geometry[i].Y);
            }
            return Math.Abs(area / 2.0);
        }
    }

    public class PlacedPolyline
    {
        public PolylineItem Item { get; set; }
        public Point3d Position { get; set; }
        public double RotationDegrees { get; set; }
        public int SheetIndex { get; set; }
        public Polyline TransformedGeometry { get; private set; }
        public BoundingBox BoundingBox { get; private set; }

        public PlacedPolyline(PolylineItem item, Point3d position, double rotation, int sheetIndex)
        {
            Item = item;
            Position = position;
            RotationDegrees = rotation;
            SheetIndex = sheetIndex;
            TransformedGeometry = ComputeTransformedGeometry();
            BoundingBox = TransformedGeometry.BoundingBox;
        }

        private Polyline ComputeTransformedGeometry()
        {
            Polyline result = new Polyline(Item.Geometry);

            // Step 1: Move to origin based on original bounding box
            Point3d originalMin = Item.BoundingBox.Min;
            Transform moveToOrigin = Transform.Translation(-originalMin.X, -originalMin.Y, -originalMin.Z);
            result.Transform(moveToOrigin);

            // Step 2: Rotate around origin if needed
            if (Math.Abs(RotationDegrees) > 1e-6)
            {
                Transform rotate = Transform.Rotation(
                    RotationDegrees * Math.PI / 180.0,
                    Vector3d.ZAxis,
                    Point3d.Origin
                );
                result.Transform(rotate);
            }

            // Step 3: Get the NEW bounding box after rotation
            BoundingBox rotatedBBox = result.BoundingBox;
            Point3d rotatedMin = rotatedBBox.Min;

            // Step 4: Translate so the rotated bbox min aligns with target Position
            Transform moveToPosition = Transform.Translation(
                Position.X - rotatedMin.X,
                Position.Y - rotatedMin.Y,
                Position.Z - rotatedMin.Z
            );
            result.Transform(moveToPosition);

            return result;
        }

        public Rectangle3d GetBoundingRectangle()
        {
            Plane plane = Plane.WorldXY;
            plane.Origin = new Point3d(BoundingBox.Min.X, BoundingBox.Min.Y, 0);

            double width = BoundingBox.Max.X - BoundingBox.Min.X;
            double height = BoundingBox.Max.Y - BoundingBox.Min.Y;

            return new Rectangle3d(plane, width, height);
        }
    }

    public class NestingSheet
    {
        public int Index { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public List<PlacedPolyline> PlacedItems { get; set; }

        public NestingSheet(int index, double width, double height)
        {
            Index = index;
            Width = width;
            Height = height;
            PlacedItems = new List<PlacedPolyline>();
        }

        public BoundingBox GetOccupiedBounds()
        {
            if (PlacedItems.Count == 0)
                return BoundingBox.Empty;

            BoundingBox bounds = PlacedItems[0].BoundingBox;
            for (int i = 1; i < PlacedItems.Count; i++)
            {
                bounds.Union(PlacedItems[i].BoundingBox);
            }
            return bounds;
        }

        public double Area => Width * Height;

        public double Utilization
        {
            get
            {
                if (PlacedItems.Count == 0) return 0;
                double totalItemArea = PlacedItems.Sum(p => p.Item.Area);
                return (totalItemArea / Area) * 100.0;
            }
        }
    }

    #endregion

    #region Nesting Algorithm

    public class PolylineNestingAlgorithm
    {
        private readonly double _sheetWidth;
        private readonly double _sheetHeight;
        private readonly double _margin;
        private readonly double _spacing;
        private readonly PolylineSortStrategy _sortStrategy;
        private readonly PlacementStrategy _placementStrategy;
        private readonly RotationMode _defaultRotationMode;
        private readonly double _rotationStepDegrees;
        private readonly double _gridResolution;

        private List<NestingSheet> _sheets;
        private List<string> _warnings;
        private int _currentSheetIndex;
        private int _totalItemsPlaced;
        private int _totalItemsFailed;

        public PolylineNestingAlgorithm(
            double sheetWidth,
            double sheetHeight,
            double margin = 5.0,
            double spacing = 2.0,
            PolylineSortStrategy sortStrategy = PolylineSortStrategy.AreaDescending,
            PlacementStrategy placementStrategy = PlacementStrategy.BottomLeft,
            RotationMode defaultRotationMode = RotationMode.Rotation90,
            double rotationStepDegrees = 15.0,
            double gridResolution = 1.0)
        {
            _sheetWidth = sheetWidth;
            _sheetHeight = sheetHeight;
            _margin = margin;
            _spacing = spacing;
            _sortStrategy = sortStrategy;
            _placementStrategy = placementStrategy;
            _defaultRotationMode = defaultRotationMode;
            _rotationStepDegrees = rotationStepDegrees;
            _gridResolution = gridResolution;

            _sheets = new List<NestingSheet>();
            _warnings = new List<string>();
            _currentSheetIndex = 0;
            _totalItemsPlaced = 0;
            _totalItemsFailed = 0;
        }

        public void Nest(List<PolylineItem> items)
        {
            _sheets.Clear();
            _warnings.Clear();
            _currentSheetIndex = 0;
            _totalItemsPlaced = 0;
            _totalItemsFailed = 0;

            if (items == null || items.Count == 0)
            {
                _warnings.Add("No polyline items provided for nesting");
                return;
            }

            AddNewSheet();
            List<PolylineItem> sortedItems = SortItems(items);

            foreach (var item in sortedItems)
            {
                bool placed = TryPlaceItem(item);

                if (!placed)
                {
                    // Try on a new sheet
                    AddNewSheet();
                    placed = TryPlaceItem(item);

                    if (!placed)
                    {
                        _warnings.Add($"Item {item.Id} (Tag: '{item.Tag}') could not be nested - exceeds sheet dimensions");
                        _totalItemsFailed++;
                    }
                }
            }

            // Remove empty sheets
            _sheets.RemoveAll(s => s.PlacedItems.Count == 0);
        }

        public List<PlacedPolyline> GetPlacedPolylines()
        {
            List<PlacedPolyline> all = new List<PlacedPolyline>();
            foreach (var sheet in _sheets)
            {
                all.AddRange(sheet.PlacedItems);
            }
            return all;
        }

        public List<Polyline> GetPlacedGeometries()
        {
            return GetPlacedPolylines().Select(p => p.TransformedGeometry).ToList();
        }

        public List<Rectangle3d> GetBoundingBoxes()
        {
            return GetPlacedPolylines().Select(p => p.GetBoundingRectangle()).ToList();
        }

        public List<Rectangle3d> GetSheetRectangles()
        {
            List<Rectangle3d> rects = new List<Rectangle3d>();
            double sheetSpacing = _sheetWidth + 100;

            for (int i = 0; i < _sheets.Count; i++)
            {
                Plane plane = Plane.WorldXY;
                plane.Origin = new Point3d(i * sheetSpacing, 0, 0);
                rects.Add(new Rectangle3d(plane, _sheetWidth, _sheetHeight));
            }

            return rects;
        }

        public List<Rectangle3d> GetMarginRectangles()
        {
            List<Rectangle3d> rects = new List<Rectangle3d>();
            double sheetSpacing = _sheetWidth + 100;

            for (int i = 0; i < _sheets.Count; i++)
            {
                Plane plane = Plane.WorldXY;
                plane.Origin = new Point3d(i * sheetSpacing + _margin, _margin, 0);
                double innerWidth = _sheetWidth - 2 * _margin;
                double innerHeight = _sheetHeight - 2 * _margin;
                rects.Add(new Rectangle3d(plane, innerWidth, innerHeight));
            }

            return rects;
        }

        public List<string> GetItemInfo()
        {
            List<string> info = new List<string>();
            var placed = GetPlacedPolylines();

            foreach (var p in placed)
            {
                double width = p.BoundingBox.Max.X - p.BoundingBox.Min.X;
                double height = p.BoundingBox.Max.Y - p.BoundingBox.Min.Y;
                string rotation = $"{p.RotationDegrees:F1}°";
                string bounds = $"[{width:F1} × {height:F1}]";
                info.Add($"ID:{p.Item.Id} Tag:{p.Item.Tag} Sheet:{p.SheetIndex} Rot:{rotation} Size:{bounds}");
            }

            return info;
        }

        public List<string> GetStatistics()
        {
            List<string> stats = new List<string>();

            stats.Add($"=== NESTING STATISTICS ===");
            stats.Add($"Total Sheets Used: {_sheets.Count}");
            stats.Add($"Total Items Placed: {_totalItemsPlaced}");
            stats.Add($"Total Items Failed: {_totalItemsFailed}");
            stats.Add($"");

            double totalSheetArea = _sheets.Count * _sheetWidth * _sheetHeight;
            double totalItemArea = GetPlacedPolylines().Sum(p => p.Item.Area);
            double overallEfficiency = totalSheetArea > 0 ? (totalItemArea / totalSheetArea) * 100.0 : 0;

            stats.Add($"Overall Efficiency: {overallEfficiency:F2}%");
            stats.Add($"Total Sheet Area: {totalSheetArea:F2}");
            stats.Add($"Total Item Area: {totalItemArea:F2}");
            stats.Add($"");

            stats.Add($"=== PER-SHEET UTILIZATION ===");
            foreach (var sheet in _sheets)
            {
                stats.Add($"Sheet {sheet.Index}: {sheet.Utilization:F2}% ({sheet.PlacedItems.Count} items)");
            }

            return stats;
        }

        public List<string> GetWarnings() => new List<string>(_warnings);
        public int GetSheetCount() => _sheets.Count;

        private void AddNewSheet()
        {
            _sheets.Add(new NestingSheet(_sheets.Count, _sheetWidth, _sheetHeight));
            _currentSheetIndex = _sheets.Count - 1;
        }

        private List<PolylineItem> SortItems(List<PolylineItem> items)
        {
            switch (_sortStrategy)
            {
                case PolylineSortStrategy.AreaDescending:
                    return items.OrderByDescending(i => i.Area).ToList();
                case PolylineSortStrategy.AreaAscending:
                    return items.OrderBy(i => i.Area).ToList();
                case PolylineSortStrategy.LargestDimensionFirst:
                    return items.OrderByDescending(i => i.MaxDimension).ToList();
                case PolylineSortStrategy.PerimeterDescending:
                    return items.OrderByDescending(i => i.Perimeter).ToList();
                case PolylineSortStrategy.ComplexityDescending:
                    return items.OrderByDescending(i => i.Geometry.Count).ToList();
                default:
                    return new List<PolylineItem>(items);
            }
        }

        private bool TryPlaceItem(PolylineItem item)
        {
            NestingSheet currentSheet = _sheets[_currentSheetIndex];
            List<double> rotations = GetRotationAngles(item.AllowedRotation);

            foreach (double rotation in rotations)
            {
                Point3d? position = FindValidPosition(item, rotation, currentSheet);

                if (position.HasValue)
                {
                    PlacedPolyline placed = new PlacedPolyline(item, position.Value, rotation, currentSheet.Index);
                    currentSheet.PlacedItems.Add(placed);
                    _totalItemsPlaced++;
                    return true;
                }
            }

            return false;
        }

        private List<double> GetRotationAngles(RotationMode mode)
        {
            List<double> angles = new List<double>();

            switch (mode)
            {
                case RotationMode.None:
                    angles.Add(0);
                    break;
                case RotationMode.Rotation90:
                    angles.AddRange(new[] { 0.0, 90.0, 180.0, 270.0 });
                    break;
                case RotationMode.Rotation45:
                    for (double a = 0; a < 360; a += 45)
                        angles.Add(a);
                    break;
                case RotationMode.CustomStep:
                    if (_rotationStepDegrees > 0)
                    {
                        for (double a = 0; a < 360; a += _rotationStepDegrees)
                            angles.Add(a);
                    }
                    else
                    {
                        angles.Add(0);
                    }
                    break;
            }

            return angles;
        }

        private Point3d? FindValidPosition(PolylineItem item, double rotation, NestingSheet sheet)
        {
            // Test if item fits at all with this rotation
            PlacedPolyline test = new PlacedPolyline(item, Point3d.Origin, rotation, sheet.Index);
            double width = test.BoundingBox.Max.X - test.BoundingBox.Min.X;
            double height = test.BoundingBox.Max.Y - test.BoundingBox.Min.Y;

            if (width + 2 * _margin > _sheetWidth || height + 2 * _margin > _sheetHeight)
                return null;

            switch (_placementStrategy)
            {
                case PlacementStrategy.BottomLeft:
                    return FindPositionBottomLeft(item, rotation, sheet);
                case PlacementStrategy.BestFit:
                    return FindPositionBestFit(item, rotation, sheet);
                case PlacementStrategy.Grid:
                    return FindPositionGrid(item, rotation, sheet);
                default:
                    return FindPositionBottomLeft(item, rotation, sheet);
            }
        }

        private Point3d? FindPositionBottomLeft(PolylineItem item, double rotation, NestingSheet sheet)
        {
            PlacedPolyline temp = new PlacedPolyline(item, Point3d.Origin, rotation, sheet.Index);
            double width = temp.BoundingBox.Max.X - temp.BoundingBox.Min.X;
            double height = temp.BoundingBox.Max.Y - temp.BoundingBox.Min.Y;

            double sheetOffsetX = sheet.Index * (_sheetWidth + 100);

            // Scan from bottom-left to top-right
            for (double y = _margin; y <= _sheetHeight - height - _margin; y += _gridResolution)
            {
                for (double x = _margin; x <= _sheetWidth - width - _margin; x += _gridResolution)
                {
                    Point3d testPos = new Point3d(sheetOffsetX + x, y, 0);

                    if (IsValidPosition(item, testPos, rotation, sheet))
                    {
                        return testPos;
                    }
                }
            }

            return null;
        }

        private Point3d? FindPositionBestFit(PolylineItem item, double rotation, NestingSheet sheet)
        {
            // First try bottom-left strategy
            Point3d? position = FindPositionBottomLeft(item, rotation, sheet);

            if (position.HasValue && sheet.PlacedItems.Count > 0)
            {
                // Try to find a better position that minimizes wasted space
                List<Point3d> candidatePositions = new List<Point3d>();

                foreach (var placed in sheet.PlacedItems)
                {
                    // Try to the right of this item
                    candidatePositions.Add(new Point3d(
                        placed.BoundingBox.Max.X + _spacing,
                        placed.BoundingBox.Min.Y,
                        0));

                    // Try above this item
                    candidatePositions.Add(new Point3d(
                        placed.BoundingBox.Min.X,
                        placed.BoundingBox.Max.Y + _spacing,
                        0));
                }

                // Sort candidates by distance from origin (prefer bottom-left positions)
                candidatePositions = candidatePositions.OrderBy(p => p.X + p.Y).ToList();

                foreach (var pos in candidatePositions)
                {
                    if (IsValidPosition(item, pos, rotation, sheet))
                    {
                        return pos;
                    }
                }
            }

            return position;
        }

        private Point3d? FindPositionGrid(PolylineItem item, double rotation, NestingSheet sheet)
        {
            PlacedPolyline temp = new PlacedPolyline(item, Point3d.Origin, rotation, sheet.Index);
            double itemWidth = temp.BoundingBox.Max.X - temp.BoundingBox.Min.X;
            double itemHeight = temp.BoundingBox.Max.Y - temp.BoundingBox.Min.Y;

            double sheetOffsetX = sheet.Index * (_sheetWidth + 100);

            // Use item dimensions for grid sizing
            double cellWidth = itemWidth + _spacing;
            double cellHeight = itemHeight + _spacing;

            int cols = (int)Math.Max(1, (_sheetWidth - 2 * _margin) / cellWidth);
            int rows = (int)Math.Max(1, (_sheetHeight - 2 * _margin) / cellHeight);

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    Point3d testPos = new Point3d(
                        sheetOffsetX + _margin + col * cellWidth,
                        _margin + row * cellHeight,
                        0);

                    if (IsValidPosition(item, testPos, rotation, sheet))
                    {
                        return testPos;
                    }
                }
            }

            return null;
        }

        private bool IsValidPosition(PolylineItem item, Point3d position, double rotation, NestingSheet sheet)
        {
            PlacedPolyline test = new PlacedPolyline(item, position, rotation, sheet.Index);

            // Check sheet boundaries
            double sheetOffsetX = sheet.Index * (_sheetWidth + 100);
            if (test.BoundingBox.Min.X < sheetOffsetX + _margin ||
                test.BoundingBox.Max.X > sheetOffsetX + _sheetWidth - _margin ||
                test.BoundingBox.Min.Y < _margin ||
                test.BoundingBox.Max.Y > _sheetHeight - _margin)
            {
                return false;
            }

            // Check collisions with existing items
            foreach (var placed in sheet.PlacedItems)
            {
                if (HasCollision(test, placed))
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasCollision(PlacedPolyline a, PlacedPolyline b)
        {
            // First check bounding boxes with spacing
            BoundingBox boxA = a.BoundingBox;
            BoundingBox boxB = b.BoundingBox;

            // Add half spacing to each box for separation
            boxA.Inflate(_spacing / 2.0, _spacing / 2.0, 0);
            boxB.Inflate(_spacing / 2.0, _spacing / 2.0, 0);

            // Quick rejection test
            if (!BoundingBoxesOverlap(boxA, boxB))
                return false;

            // For more accurate collision detection, check polyline intersections
            Polyline polyA = a.TransformedGeometry;
            Polyline polyB = b.TransformedGeometry;

            // Check edge intersections
            for (int i = 0; i < polyA.Count - 1; i++)
            {
                Line lineA = new Line(polyA[i], polyA[i + 1]);

                for (int j = 0; j < polyB.Count - 1; j++)
                {
                    Line lineB = new Line(polyB[j], polyB[j + 1]);

                    double a_param, b_param;
                    const double TOLERANCE = 1e-6;

                    if (Rhino.Geometry.Intersect.Intersection.LineLine(
                        lineA, lineB, out a_param, out b_param, TOLERANCE, false))
                    {
                        if (a_param >= 0 && a_param <= 1 && b_param >= 0 && b_param <= 1)
                        {
                            return true; // Lines intersect
                        }
                    }
                }
            }

            // Check if one polyline is inside the other
            if (polyA.Count > 0 && IsPointInside(polyA[0], polyB))
                return true;
            if (polyB.Count > 0 && IsPointInside(polyB[0], polyA))
                return true;

            return false;
        }

        private bool IsPointInside(Point3d point, Polyline polyline)
        {
            // Ray casting algorithm
            int intersections = 0;

            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Point3d p1 = polyline[i];
                Point3d p2 = polyline[i + 1];

                if ((p1.Y > point.Y) != (p2.Y > point.Y))
                {
                    double xIntersect = (p2.X - p1.X) * (point.Y - p1.Y) / (p2.Y - p1.Y) + p1.X;
                    if (point.X < xIntersect)
                    {
                        intersections++;
                    }
                }
            }

            return (intersections % 2) == 1;
        }

        private bool BoundingBoxesOverlap(BoundingBox a, BoundingBox b)
        {
            if (!a.IsValid || !b.IsValid)
                return false;

            return !(a.Max.X < b.Min.X || a.Min.X > b.Max.X ||
                     a.Max.Y < b.Min.Y || a.Min.Y > b.Max.Y);
        }
    }

    #endregion

    #region Main Script Method

    private void RunScript(
		List<object> Polylines,
		double SheetWidth,
		double SheetHeight,
		double Margin,
		double Spacing,
		int SortStrategy,
		int PlacementStrategy,
		int RotationMode,
		double RotationStep,
		double GridResolution,
		List<string> Tags,
		bool Run,
		ref object PlacedPolylines,
		ref object BoundingBoxes,
		ref object Sheets,
		ref object Margins,
		ref object Info,
		ref object Statistics,
		ref object Warnings,
		ref object SheetCount,
		ref object Colors)
    {
        // Initialize outputs
        PlacedPolylines = null;
        BoundingBoxes = null;
        Sheets = null;
        Margins = null;
        Info = null;
        Statistics = null;
        Warnings = null;
        SheetCount = 0;
        Colors = null;

        // Check if should run
        if (!Run)
        {
            Warnings = new List<string> { "Set Run to True to execute nesting" };
            return;
        }

        // Validate inputs
        if (Polylines == null || Polylines.Count == 0)
        {
            Warnings = new List<string> { "No polylines provided" };
            return;
        }

        if (SheetWidth <= 0 || SheetHeight <= 0)
        {
            Warnings = new List<string> { "Sheet dimensions must be positive" };
            return;
        }

        // Convert curves to polylines
        List<Polyline> polylineList = new List<Polyline>();
        List<string> conversionWarnings = new List<string>();

        for (int i = 0; i < Polylines.Count; i++)
        {
            Curve curve = Polylines[i] as Curve;
            if (curve == null)
            {
                conversionWarnings.Add($"Curve {i} is null");
                continue;
            }

            Polyline polyline;
            if (curve.TryGetPolyline(out polyline))
            {
                polylineList.Add(polyline);
            }
            else
            {
                // Try to convert to polyline
                PolylineCurve polyCurve = curve.ToPolyline(0, 0, 0.01, 0.01, 0, 0.01, 0.01, 0, true);
                if (polyCurve != null && polyCurve.TryGetPolyline(out polyline))
                {
                    polylineList.Add(polyline);
                }
                else
                {
                    conversionWarnings.Add($"Could not convert curve {i} to polyline");
                }
            }
        }

        if (polylineList.Count == 0)
        {
            Warnings = new List<string> { "No valid polylines after conversion" };
            Warnings.AddRange(conversionWarnings);
            return;
        }

        // Set defaults for optional parameters
        if (Margin < 0) Margin = 5.0;
        if (Spacing < 0) Spacing = 2.0;
        if (RotationStep <= 0) RotationStep = 15.0;
        if (GridResolution <= 0) GridResolution = 1.0;

        // Ensure enum values are in valid range
        PolylineSortStrategy sortStrat = (PolylineSortStrategy)Math.Max(0, Math.Min(4, SortStrategy));
        PlacementStrategy placeStrat = (PlacementStrategy)Math.Max(0, Math.Min(2, PlacementStrategy));
        RotationMode rotMode = (RotationMode)Math.Max(0, Math.Min(3, RotationMode));

        // Create polyline items
        List<PolylineItem> items = new List<PolylineItem>();
        for (int i = 0; i < polylineList.Count; i++)
        {
            string tag = (Tags != null && i < Tags.Count) ? Tags[i] : $"Item_{i}";
            items.Add(new PolylineItem(polylineList[i], i, tag, rotMode));
        }

        // Create and run algorithm
        PolylineNestingAlgorithm algorithm = new PolylineNestingAlgorithm(
            SheetWidth,
            SheetHeight,
            Margin,
            Spacing,
            sortStrat,
            placeStrat,
            rotMode,
            RotationStep,
            GridResolution
        );

        try
        {
            algorithm.Nest(items);

            // Get outputs
            PlacedPolylines = algorithm.GetPlacedGeometries();
            BoundingBoxes = algorithm.GetBoundingBoxes();
            Sheets = algorithm.GetSheetRectangles();
            Margins = algorithm.GetMarginRectangles();
            Info = algorithm.GetItemInfo();
            Statistics = algorithm.GetStatistics();

            // Combine warnings
            List<string> allWarnings = new List<string>();
            allWarnings.AddRange(conversionWarnings);
            allWarnings.AddRange(algorithm.GetWarnings());
            if (allWarnings.Count > 0)
                Warnings = allWarnings;

            SheetCount = algorithm.GetSheetCount();

            // Generate colors
            Colors = GenerateColors(algorithm.GetPlacedPolylines());
        }
        catch (Exception ex)
        {
            Warnings = new List<string> { $"Error during nesting: {ex.Message}", ex.StackTrace };
        }
    }

    private List<Color> GenerateColors(List<PlacedPolyline> placed)
    {
        List<Color> colors = new List<Color>();
        double goldenRatio = 0.618033988749895;
        double hue = 0;

        foreach (var p in placed)
        {
            // Use sheet index to group colors
            double sheetHue = (p.SheetIndex * 0.3) % 1.0;
            hue = (sheetHue + (hue + goldenRatio)) % 1.0;

            Color color = HSVToRGB(hue, 0.7, 0.9);
            colors.Add(color);
        }

        return colors;
    }

    private Color HSVToRGB(double h, double s, double v)
    {
        int hi = Convert.ToInt32(Math.Floor(h * 6)) % 6;
        double f = h * 6 - Math.Floor(h * 6);

        double p = v * (1 - s);
        double q = v * (1 - f * s);
        double t = v * (1 - (1 - f) * s);

        double r, g, b;

        switch (hi)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }

        return Color.FromArgb(
            Math.Max(0, Math.Min(255, (int)(r * 255))),
            Math.Max(0, Math.Min(255, (int)(g * 255))),
            Math.Max(0, Math.Min(255, (int)(b * 255)))
        );
    }

    #endregion
}

/*
USAGE GUIDE - POLYLINE NESTING COMPONENT
=========================================

INPUT PARAMETERS:
-----------------
1. Polylines (Curve List): Closed curves/polylines to nest
2. SheetWidth (Number): Width of each sheet (default: 1200)
3. SheetHeight (Number): Height of each sheet (default: 800)
4. Margin (Number): Distance from sheet edges (default: 5)
5. Spacing (Number): Minimum gap between items (default: 2)
6. SortStrategy (Integer): Item sorting method
   - 0: Area Descending (largest first)
   - 1: Area Ascending (smallest first)
   - 2: Largest Dimension First
   - 3: Perimeter Descending
   - 4: Complexity Descending (most vertices first)
7. PlacementStrategy (Integer): Placement algorithm
   - 0: Bottom-Left (fast, good packing)
   - 1: Best Fit (better packing, slower)
   - 2: Grid (regular arrangement)
8. RotationMode (Integer): Rotation options
   - 0: None (no rotation)
   - 1: 90° steps (0°, 90°, 180°, 270°)
   - 2: 45° steps
   - 3: Custom step (uses RotationStep)
9. RotationStep (Number): Degrees for custom rotation (default: 15)
10. GridResolution (Number): Search grid size (default: 1, smaller = more accurate but slower)
11. Tags (Text List): Optional item labels
12. Run (Boolean): Execute nesting when True

OUTPUT PARAMETERS:
------------------
1. PlacedPolylines: Nested polylines in final positions
2. BoundingBoxes: Bounding rectangles of placed items
3. Sheets: Sheet boundary rectangles
4. Margins: Usable area within margins
5. Info: Detailed placement information
6. Statistics: Efficiency and utilization data
7. Warnings: Any errors or issues
8. SheetCount: Number of sheets used
9. Colors: Unique colors per item for visualization

TIPS FOR OPTIMAL USE:
---------------------
- Start with GridResolution=5 for testing, use 1 for final
- SortStrategy=0 (Area Descending) usually gives best results
- RotationMode=1 (90° steps) balances speed and efficiency
- Increase Spacing if items appear too close
- Use Tags to track specific parts/items
- PlacementStrategy=1 (BestFit) for maximum efficiency

PERFORMANCE NOTES:
------------------
- Processing time increases with:
  * Smaller GridResolution values
  * More rotation angles
  * Complex polylines (many vertices)
  * BestFit placement strategy
- For large batches (>100 items), consider:
  * Using GridResolution=2-5
  * Limiting rotation options
  * Using BottomLeft strategy

ERROR HANDLING:
---------------
- Component validates all inputs
- Converts curves to polylines automatically
- Reports items that couldn't be placed
- Removes empty sheets automatically
- Provides detailed warnings for troubleshooting
*/
