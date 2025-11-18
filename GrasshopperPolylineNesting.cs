using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using Rhino;
using Rhino.Geometry;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using PolylineNesting;

/// <summary>
/// Grasshopper C# Script Component for Polyline Nesting
///
/// SETUP INSTRUCTIONS:
/// 1. Create a C# Script component in Grasshopper
/// 2. Copy this entire code into the component
/// 3. Set component inputs (right-click component > Set Inputs):
///    - Polylines (Polyline, List Access)
///    - SheetWidth (double, Item Access)
///    - SheetHeight (double, Item Access)
///    - Margin (double, Item Access)
///    - Spacing (double, Item Access)
///    - SortStrategy (int, Item Access)
///    - PlacementStrategy (int, Item Access)
///    - RotationMode (int, Item Access)
///    - RotationStep (double, Item Access)
///    - GridResolution (double, Item Access)
///    - Tags (string, List Access) [Optional]
///    - Run (bool, Item Access)
///
/// 4. Set component outputs (right-click component > Set Outputs):
///    - PlacedPolylines (Polyline)
///    - BoundingBoxes (Rectangle3d)
///    - Sheets (Rectangle3d)
///    - Margins (Rectangle3d)
///    - Info (string)
///    - Statistics (string)
///    - Warnings (string)
///    - SheetCount (int)
///    - Colors (Color)
///
/// 5. Add reference to the algorithm library:
///    - Right-click component > Manage Assemblies
///    - Click "Add assembly"
///    - Browse to PolylineNestingAlgorithm.dll (after compiling the .cs file)
///    OR simply include the algorithm code directly in this script
/// </summary>

// ============================================================================
// EMBEDDED ALGORITHM CODE
// (Alternatively, reference the compiled DLL if available)
// ============================================================================

#region Algorithm Code

namespace PolylineNesting
{
    #region Enumerations

    public enum PolylineSortStrategy
    {
        AreaDescending,
        AreaAscending,
        LargestDimensionFirst,
        PerimeterDescending,
        ComplexityDescending
    }

    public enum PlacementStrategy
    {
        BottomLeft,
        BestFit,
        Grid
    }

    public enum RotationMode
    {
        None,
        Rotation90,
        Rotation45,
        CustomStep
    }

    #endregion

    #region Core Classes

    public class PolylineItem
    {
        public Polyline Geometry { get; set; }
        public int Id { get; set; }
        public string Tag { get; set; }
        public RotationMode AllowedRotation { get; set; }

        private BoundingBox _boundingBox;
        private double _area;

        public PolylineItem(Polyline polyline, int id = 0, string tag = "", RotationMode rotation = RotationMode.Rotation90)
        {
            Geometry = polyline;
            Id = id;
            Tag = tag;
            AllowedRotation = rotation;
            _boundingBox = polyline.BoundingBox;
            _area = CalculateArea();
        }

        public BoundingBox BoundingBox => _boundingBox;
        public double Area => _area;
        public double Perimeter => Geometry.Length;
        public double MaxDimension => Math.Max(BoundingBox.Max.X - BoundingBox.Min.X,
                                               BoundingBox.Max.Y - BoundingBox.Min.Y);

        double CalculateArea()
        {
            double area = 0;
            for (int i = 0; i < Geometry.Count - 1; i++)
            {
                area += Geometry[i].X * Geometry[i + 1].Y - Geometry[i + 1].X * Geometry[i].Y;
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
        public Polyline TransformedGeometry { get; set; }
        public BoundingBox BoundingBox { get; set; }

        public PlacedPolyline(PolylineItem item, Point3d position, double rotation, int sheetIndex)
        {
            Item = item;
            Position = position;
            RotationDegrees = rotation;
            SheetIndex = sheetIndex;
            TransformedGeometry = ComputeTransformedGeometry();
            BoundingBox = TransformedGeometry.BoundingBox;
        }

        Polyline ComputeTransformedGeometry()
        {
            Polyline result = Item.Geometry.Duplicate();

            // Step 1: Move to origin based on original bounding box
            Point3d originalMin = Item.BoundingBox.Min;
            Transform moveToOrigin = Transform.Translation(-originalMin.X, -originalMin.Y, 0);
            result.Transform(moveToOrigin);

            // Step 2: Rotate around origin
            if (Math.Abs(RotationDegrees) > 1e-6)
            {
                Transform rotate = Transform.Rotation(RotationDegrees * Math.PI / 180.0, Vector3d.ZAxis, Point3d.Origin);
                result.Transform(rotate);
            }

            // Step 3: Get the NEW bounding box after rotation
            BoundingBox rotatedBBox = result.BoundingBox;
            Point3d rotatedMin = rotatedBBox.Min;

            // Step 4: Translate so the rotated bbox min aligns with target Position
            Transform moveToPosition = Transform.Translation(
                Position.X - rotatedMin.X,
                Position.Y - rotatedMin.Y,
                0);
            result.Transform(moveToPosition);

            return result;
        }

        public Rectangle3d GetBoundingRectangle()
        {
            Plane plane = Plane.WorldXY;
            plane.Origin = new Point3d(BoundingBox.Min.X, BoundingBox.Min.Y, 0);
            double width = BoundingBox.Max.X - BoundingBox.Min.X;
            double height = BoundingBox.Max.Y - BoundingBox.Min.Y;

            Interval xInterval = new Interval(0, width);
            Interval yInterval = new Interval(0, height);

            return new Rectangle3d(plane, xInterval, yInterval);
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

    public class PolylineNestingAlgorithm
    {
        #region Fields

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

        #endregion

        #region Constructor

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

        #endregion

        #region Public Methods

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

            for (int i = 0; i < sortedItems.Count; i++)
            {
                PolylineItem item = sortedItems[i];
                bool placed = TryPlaceItem(item);

                if (!placed)
                {
                    AddNewSheet();
                    placed = TryPlaceItem(item);

                    if (!placed)
                    {
                        _warnings.Add($"Item {item.Id} (Tag: '{item.Tag}') could not be nested - exceeds sheet dimensions or no valid placement found");
                        _totalItemsFailed++;
                    }
                }
            }
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

            for (int i = 0; i < _sheets.Count; i++)
            {
                Plane plane = Plane.WorldXY;
                plane.Origin = new Point3d(i * (_sheetWidth + 100), 0, 0);

                Interval xInterval = new Interval(0, _sheetWidth);
                Interval yInterval = new Interval(0, _sheetHeight);

                rects.Add(new Rectangle3d(plane, xInterval, yInterval));
            }

            return rects;
        }

        public List<Rectangle3d> GetMarginRectangles()
        {
            List<Rectangle3d> rects = new List<Rectangle3d>();

            for (int i = 0; i < _sheets.Count; i++)
            {
                Plane plane = Plane.WorldXY;
                plane.Origin = new Point3d(i * (_sheetWidth + 100) + _margin, _margin, 0);

                Interval xInterval = new Interval(0, _sheetWidth - 2 * _margin);
                Interval yInterval = new Interval(0, _sheetHeight - 2 * _margin);

                rects.Add(new Rectangle3d(plane, xInterval, yInterval));
            }

            return rects;
        }

        public List<string> GetItemInfo()
        {
            List<string> info = new List<string>();
            var placed = GetPlacedPolylines();

            foreach (var p in placed)
            {
                string rotation = $"{p.RotationDegrees:F1}°";
                string bounds = $"[{p.BoundingBox.Max.X - p.BoundingBox.Min.X:F1} × {p.BoundingBox.Max.Y - p.BoundingBox.Min.Y:F1}]";
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
            for (int i = 0; i < _sheets.Count; i++)
            {
                var sheet = _sheets[i];
                stats.Add($"Sheet {i}: {sheet.Utilization:F2}% ({sheet.PlacedItems.Count} items)");
            }

            return stats;
        }

        public List<string> GetWarnings()
        {
            return new List<string>(_warnings);
        }

        public int GetSheetCount()
        {
            return _sheets.Count;
        }

        #endregion

        #region Private Methods

        void AddNewSheet()
        {
            _sheets.Add(new NestingSheet(_currentSheetIndex, _sheetWidth, _sheetHeight));
            _currentSheetIndex = _sheets.Count - 1;
        }

        List<PolylineItem> SortItems(List<PolylineItem> items)
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

        bool TryPlaceItem(PolylineItem item)
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

        List<double> GetRotationAngles(RotationMode mode)
        {
            List<double> angles = new List<double>();

            switch (mode)
            {
                case RotationMode.None:
                    angles.Add(0);
                    break;
                case RotationMode.Rotation90:
                    angles.Add(0);
                    angles.Add(90);
                    angles.Add(180);
                    angles.Add(270);
                    break;
                case RotationMode.Rotation45:
                    for (double a = 0; a < 360; a += 45)
                        angles.Add(a);
                    break;
                case RotationMode.CustomStep:
                    for (double a = 0; a < 360; a += _rotationStepDegrees)
                        angles.Add(a);
                    break;
            }

            return angles;
        }

        Point3d? FindValidPosition(PolylineItem item, double rotation, NestingSheet sheet)
        {
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

        Point3d? FindPositionBottomLeft(PolylineItem item, double rotation, NestingSheet sheet)
        {
            PlacedPolyline temp = new PlacedPolyline(item, Point3d.Origin, rotation, sheet.Index);
            double width = temp.BoundingBox.Max.X - temp.BoundingBox.Min.X;
            double height = temp.BoundingBox.Max.Y - temp.BoundingBox.Min.Y;

            double sheetOffsetX = sheet.Index * (_sheetWidth + 100);

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

        Point3d? FindPositionBestFit(PolylineItem item, double rotation, NestingSheet sheet)
        {
            Point3d? bestPosition = FindPositionBottomLeft(item, rotation, sheet);

            if (bestPosition.HasValue && sheet.PlacedItems.Count > 0)
            {
                BoundingBox currentBounds = sheet.GetOccupiedBounds();
                List<Point3d> candidatePositions = new List<Point3d>();
                double sheetOffsetX = sheet.Index * (_sheetWidth + 100);

                foreach (var placed in sheet.PlacedItems)
                {
                    candidatePositions.Add(new Point3d(
                        placed.BoundingBox.Max.X + _spacing,
                        placed.BoundingBox.Min.Y,
                        0));

                    candidatePositions.Add(new Point3d(
                        placed.BoundingBox.Min.X,
                        placed.BoundingBox.Max.Y + _spacing,
                        0));
                }

                foreach (var pos in candidatePositions)
                {
                    if (IsValidPosition(item, pos, rotation, sheet))
                    {
                        bestPosition = pos;
                        break;
                    }
                }
            }

            return bestPosition;
        }

        Point3d? FindPositionGrid(PolylineItem item, double rotation, NestingSheet sheet)
        {
            double sheetOffsetX = sheet.Index * (_sheetWidth + 100);
            double cellSize = 100;

            int cols = (int)((_sheetWidth - 2 * _margin) / cellSize);
            int rows = (int)((_sheetHeight - 2 * _margin) / cellSize);

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    Point3d testPos = new Point3d(
                        sheetOffsetX + _margin + col * cellSize,
                        _margin + row * cellSize,
                        0);

                    if (IsValidPosition(item, testPos, rotation, sheet))
                    {
                        return testPos;
                    }
                }
            }

            return null;
        }

        bool IsValidPosition(PolylineItem item, Point3d position, double rotation, NestingSheet sheet)
        {
            PlacedPolyline test = new PlacedPolyline(item, position, rotation, sheet.Index);

            double sheetOffsetX = sheet.Index * (_sheetWidth + 100);
            if (test.BoundingBox.Min.X < sheetOffsetX + _margin ||
                test.BoundingBox.Max.X > sheetOffsetX + _sheetWidth - _margin ||
                test.BoundingBox.Min.Y < _margin ||
                test.BoundingBox.Max.Y > _sheetHeight - _margin)
            {
                return false;
            }

            foreach (var placed in sheet.PlacedItems)
            {
                if (HasCollision(test, placed))
                {
                    return false;
                }
            }

            return true;
        }

        bool HasCollision(PlacedPolyline a, PlacedPolyline b)
        {
            BoundingBox boxA = a.BoundingBox;
            BoundingBox boxB = b.BoundingBox;

            boxA.Inflate(_spacing / 2.0);
            boxB.Inflate(_spacing / 2.0);

            // Check if bounding boxes overlap
            if (!BoundingBoxesOverlap(boxA, boxB))
                return false;

            Polyline polyA = a.TransformedGeometry;
            Polyline polyB = b.TransformedGeometry;

            for (int i = 0; i < polyA.Count - 1; i++)
            {
                Line lineA = new Line(polyA[i], polyA[i + 1]);

                for (int j = 0; j < polyB.Count - 1; j++)
                {
                    Line lineB = new Line(polyB[j], polyB[j + 1]);

                    double a_param, b_param;
                    // Use proper numerical tolerance (not _spacing)
                    const double TOLERANCE = 1e-6;
                    if (Rhino.Geometry.Intersect.Intersection.LineLine(lineA, lineB, out a_param, out b_param, TOLERANCE, false))
                    {
                        if (a_param >= 0 && a_param <= 1 && b_param >= 0 && b_param <= 1)
                        {
                            return true; // Collision detected
                        }
                    }
                }
            }

            if (IsPointInside(polyA[0], polyB) || IsPointInside(polyB[0], polyA))
            {
                return true;
            }

            return false;
        }

        bool IsPointInside(Point3d point, Polyline polyline)
        {
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

        bool BoundingBoxesOverlap(BoundingBox a, BoundingBox b)
        {
            // Check for empty or invalid bounding boxes
            if (!a.IsValid || !b.IsValid)
                return false;

            // Check if bounding boxes overlap in all three dimensions
            return !(a.Max.X < b.Min.X || a.Min.X > b.Max.X ||
                     a.Max.Y < b.Min.Y || a.Min.Y > b.Max.Y ||
                     a.Max.Z < b.Min.Z || a.Min.Z > b.Max.Z);
        }

        #endregion
    }
}

#endregion

// ============================================================================
// MAIN GRASSHOPPER COMPONENT CODE
// ============================================================================

void RunScript(
    List<Polyline> Polylines,
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
    // Clear outputs
    PlacedPolylines = null;
    BoundingBoxes = null;
    Sheets = null;
    Margins = null;
    Info = null;
    Statistics = null;
    Warnings = null;
    SheetCount = 0;
    Colors = null;

    // Input validation
    if (!Run)
    {
        Warnings = new List<string> { "Set Run to True to execute nesting" };
        return;
    }

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

    // Set defaults
    if (Margin < 0) Margin = 5.0;
    if (Spacing < 0) Spacing = 2.0;
    if (RotationStep <= 0) RotationStep = 15.0;
    if (GridResolution <= 0) GridResolution = 1.0;

    // Convert enums
    PolylineSortStrategy sortStrat = (PolylineSortStrategy)(SortStrategy % 5);
    PlacementStrategy placeStrat = (PlacementStrategy)(PlacementStrategy % 3);
    RotationMode rotMode = (RotationMode)(RotationMode % 4);

    // Create polyline items
    List<PolylineItem> items = new List<PolylineItem>();
    for (int i = 0; i < Polylines.Count; i++)
    {
        string tag = (Tags != null && i < Tags.Count) ? Tags[i] : $"Item_{i}";
        items.Add(new PolylineItem(Polylines[i], i, tag, rotMode));
    }

    // Create algorithm instance
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

    // Run nesting
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
        Warnings = algorithm.GetWarnings();
        SheetCount = algorithm.GetSheetCount();

        // Generate colors using golden ratio for distinction
        Colors = GenerateColors(algorithm.GetPlacedPolylines());
    }
    catch (Exception ex)
    {
        Warnings = new List<string> { $"Error during nesting: {ex.Message}" };
    }
}

/// <summary>
/// Generates distinct colors for polylines using golden ratio
/// </summary>
List<Color> GenerateColors(List<PlacedPolyline> placed)
{
    List<Color> colors = new List<Color>();
    double goldenRatio = 0.618033988749895;
    double hue = 0;

    foreach (var p in placed)
    {
        hue += goldenRatio;
        hue = hue % 1.0;

        // Convert HSV to RGB
        Color color = HSVToRGB(hue, 0.7, 0.9);
        colors.Add(color);
    }

    return colors;
}

/// <summary>
/// Converts HSV color to RGB
/// </summary>
Color HSVToRGB(double h, double s, double v)
{
    int hi = Convert.ToInt32(Math.Floor(h * 6)) % 6;
    double f = h * 6 - Math.Floor(h * 6);

    double p = v * (1 - s);
    double q = v * (1 - f * s);
    double t = v * (1 - (1 - f) * s);

    double r, g, b;

    switch (hi)
    {
        case 0:
            r = v; g = t; b = p;
            break;
        case 1:
            r = q; g = v; b = p;
            break;
        case 2:
            r = p; g = v; b = t;
            break;
        case 3:
            r = p; g = q; b = v;
            break;
        case 4:
            r = t; g = p; b = v;
            break;
        default:
            r = v; g = p; b = q;
            break;
    }

    return Color.FromArgb(
        (int)(r * 255),
        (int)(g * 255),
        (int)(b * 255)
    );
}

// ============================================================================
// USAGE GUIDE
// ============================================================================
/*

PARAMETER DESCRIPTIONS:
=======================

INPUTS:
-------
Polylines         - List of polyline curves to nest
SheetWidth        - Width of nesting sheet (e.g., 1200)
SheetHeight       - Height of nesting sheet (e.g., 800)
Margin            - Distance from sheet edge (default: 5.0)
Spacing           - Minimum spacing between polylines (default: 2.0)
SortStrategy      - How to sort items before nesting:
                    0 = AreaDescending (largest first)
                    1 = AreaAscending (smallest first)
                    2 = LargestDimensionFirst
                    3 = PerimeterDescending
                    4 = ComplexityDescending (most vertices first)
PlacementStrategy - How to place items:
                    0 = BottomLeft (scan from bottom-left)
                    1 = BestFit (minimize total bounds)
                    2 = Grid (regular grid pattern)
RotationMode      - Rotation options:
                    0 = None (no rotation)
                    1 = Rotation90 (0°, 90°, 180°, 270°)
                    2 = Rotation45 (every 45°)
                    3 = CustomStep (use RotationStep parameter)
RotationStep      - Custom rotation step in degrees (default: 15.0)
GridResolution    - Placement grid resolution (default: 1.0, smaller = slower but more accurate)
Tags              - Optional list of labels for each polyline
Run               - Set to True to execute nesting

OUTPUTS:
--------
PlacedPolylines   - Nested polylines in final positions
BoundingBoxes     - Bounding boxes of placed polylines
Sheets            - Sheet boundary rectangles
Margins           - Margin area rectangles
Info              - Detailed info for each placed item
Statistics        - Overall nesting statistics
Warnings          - Any warnings or errors
SheetCount        - Number of sheets used
Colors            - Distinct colors for each polyline

TIPS:
=====
1. Start with RotationMode=1 (90° rotation) for fastest results
2. Use GridResolution=5 for quick previews, 1 for final results
3. SortStrategy=0 (AreaDescending) typically gives best efficiency
4. Increase Spacing if items appear too close together
5. Use Tags to label specific polylines (e.g., part numbers)

*/
