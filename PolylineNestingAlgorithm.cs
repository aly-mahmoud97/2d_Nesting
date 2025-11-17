using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace PolylineNesting
{
    #region Enumerations

    /// <summary>
    /// Defines how polylines should be sorted before nesting
    /// </summary>
    public enum PolylineSortStrategy
    {
        /// <summary>Sort by bounding box area, largest first</summary>
        AreaDescending,
        /// <summary>Sort by bounding box area, smallest first</summary>
        AreaAscending,
        /// <summary>Sort by maximum dimension (width or height), largest first</summary>
        LargestDimensionFirst,
        /// <summary>Sort by perimeter length, longest first</summary>
        PerimeterDescending,
        /// <summary>Sort by number of vertices, most complex first</summary>
        ComplexityDescending
    }

    /// <summary>
    /// Placement strategy for positioning polylines on the sheet
    /// </summary>
    public enum PlacementStrategy
    {
        /// <summary>Try to place at bottom-left corner first, then scan right and up</summary>
        BottomLeft,
        /// <summary>Find the position that minimizes bounding box of all placed items</summary>
        BestFit,
        /// <summary>Place items in a grid pattern</summary>
        Grid
    }

    /// <summary>
    /// Rotation mode for polylines during nesting
    /// </summary>
    public enum RotationMode
    {
        /// <summary>No rotation allowed</summary>
        None,
        /// <summary>Allow 90-degree rotation only</summary>
        Rotation90,
        /// <summary>Allow rotation at 45-degree intervals (0, 45, 90, 135, etc.)</summary>
        Rotation45,
        /// <summary>Allow rotation at any angle with specified step</summary>
        CustomStep
    }

    #endregion

    #region Core Classes

    /// <summary>
    /// Represents a polyline item to be nested
    /// </summary>
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

        /// <summary>Gets the bounding box of the polyline</summary>
        public BoundingBox BoundingBox => _boundingBox;

        /// <summary>Gets the area of the polyline (approximate using bounding box)</summary>
        public double Area => _area;

        /// <summary>Gets the perimeter length of the polyline</summary>
        public double Perimeter => Geometry.Length;

        /// <summary>Gets the maximum dimension (width or height) of the bounding box</summary>
        public double MaxDimension => Math.Max(BoundingBox.Max.X - BoundingBox.Min.X,
                                               BoundingBox.Max.Y - BoundingBox.Min.Y);

        private double CalculateArea()
        {
            // Calculate signed area using shoelace formula
            double area = 0;
            for (int i = 0; i < Geometry.Count - 1; i++)
            {
                area += Geometry[i].X * Geometry[i + 1].Y - Geometry[i + 1].X * Geometry[i].Y;
            }
            return Math.Abs(area / 2.0);
        }
    }

    /// <summary>
    /// Represents a placed polyline with its transformation
    /// </summary>
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

        private Polyline ComputeTransformedGeometry()
        {
            Polyline result = Item.Geometry.Duplicate();

            // Move to origin (based on bounding box min)
            Point3d originalMin = Item.BoundingBox.Min;
            Transform moveToOrigin = Transform.Translation(-originalMin.X, -originalMin.Y, 0);
            result.Transform(moveToOrigin);

            // Rotate around origin
            if (Math.Abs(RotationDegrees) > 1e-6)
            {
                Transform rotate = Transform.Rotation(RotationDegrees * Math.PI / 180.0, Vector3d.ZAxis, Point3d.Origin);
                result.Transform(rotate);
            }

            // Move to final position
            Transform moveToPosition = Transform.Translation(Position.X, Position.Y, 0);
            result.Transform(moveToPosition);

            return result;
        }

        /// <summary>Gets a rectangle representing the bounding box</summary>
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

    /// <summary>
    /// Represents a nesting sheet
    /// </summary>
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

        /// <summary>Gets the bounding box of all placed items</summary>
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

        /// <summary>Gets the area of the sheet</summary>
        public double Area => Width * Height;

        /// <summary>Gets the utilization percentage of the sheet</summary>
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

    /// <summary>
    /// Main polyline nesting algorithm
    /// </summary>
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

        // Statistics
        private int _totalItemsPlaced;
        private int _totalItemsFailed;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new polyline nesting algorithm instance
        /// </summary>
        /// <param name="sheetWidth">Width of the nesting sheet</param>
        /// <param name="sheetHeight">Height of the nesting sheet</param>
        /// <param name="margin">Margin from sheet edges (default: 5.0)</param>
        /// <param name="spacing">Spacing between polylines (default: 2.0)</param>
        /// <param name="sortStrategy">Strategy for sorting polylines (default: AreaDescending)</param>
        /// <param name="placementStrategy">Strategy for placing polylines (default: BottomLeft)</param>
        /// <param name="defaultRotationMode">Default rotation mode (default: Rotation90)</param>
        /// <param name="rotationStepDegrees">Step for custom rotation (default: 15.0)</param>
        /// <param name="gridResolution">Grid resolution for collision detection (default: 1.0)</param>
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

        /// <summary>
        /// Nest the given polylines
        /// </summary>
        public void Nest(List<PolylineItem> items)
        {
            // Reset state
            _sheets.Clear();
            _warnings.Clear();
            _currentSheetIndex = 0;
            _totalItemsPlaced = 0;
            _totalItemsFailed = 0;

            // Validate input
            if (items == null || items.Count == 0)
            {
                _warnings.Add("No polyline items provided for nesting");
                return;
            }

            // Add first sheet
            AddNewSheet();

            // Sort items
            List<PolylineItem> sortedItems = SortItems(items);

            // Try to place each item
            for (int i = 0; i < sortedItems.Count; i++)
            {
                PolylineItem item = sortedItems[i];
                bool placed = TryPlaceItem(item);

                if (!placed)
                {
                    // Try on a new sheet
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

        /// <summary>
        /// Gets all placed polylines across all sheets
        /// </summary>
        public List<PlacedPolyline> GetPlacedPolylines()
        {
            List<PlacedPolyline> all = new List<PlacedPolyline>();
            foreach (var sheet in _sheets)
            {
                all.AddRange(sheet.PlacedItems);
            }
            return all;
        }

        /// <summary>
        /// Gets the transformed geometries of all placed polylines
        /// </summary>
        public List<Polyline> GetPlacedGeometries()
        {
            return GetPlacedPolylines().Select(p => p.TransformedGeometry).ToList();
        }

        /// <summary>
        /// Gets bounding boxes of all placed polylines
        /// </summary>
        public List<Rectangle3d> GetBoundingBoxes()
        {
            return GetPlacedPolylines().Select(p => p.GetBoundingRectangle()).ToList();
        }

        /// <summary>
        /// Gets all sheet rectangles
        /// </summary>
        public List<Rectangle3d> GetSheetRectangles()
        {
            List<Rectangle3d> rects = new List<Rectangle3d>();

            for (int i = 0; i < _sheets.Count; i++)
            {
                Plane plane = Plane.WorldXY;
                plane.Origin = new Point3d(i * (_sheetWidth + 100), 0, 0); // Offset sheets horizontally

                Interval xInterval = new Interval(0, _sheetWidth);
                Interval yInterval = new Interval(0, _sheetHeight);

                rects.Add(new Rectangle3d(plane, xInterval, yInterval));
            }

            return rects;
        }

        /// <summary>
        /// Gets margin rectangles for visualization
        /// </summary>
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

        /// <summary>
        /// Gets information about each placed polyline
        /// </summary>
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

        /// <summary>
        /// Gets detailed statistics about the nesting
        /// </summary>
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

        /// <summary>
        /// Gets all warnings generated during nesting
        /// </summary>
        public List<string> GetWarnings()
        {
            return new List<string>(_warnings);
        }

        /// <summary>
        /// Gets the number of sheets used
        /// </summary>
        public int GetSheetCount()
        {
            return _sheets.Count;
        }

        #endregion

        #region Private Methods

        private void AddNewSheet()
        {
            _sheets.Add(new NestingSheet(_currentSheetIndex, _sheetWidth, _sheetHeight));
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

            // Get rotation angles to try
            List<double> rotations = GetRotationAngles(item.AllowedRotation);

            // Try each rotation
            foreach (double rotation in rotations)
            {
                // Try to find a valid position
                Point3d? position = FindValidPosition(item, rotation, currentSheet);

                if (position.HasValue)
                {
                    // Place the item
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

        private Point3d? FindValidPosition(PolylineItem item, double rotation, NestingSheet sheet)
        {
            // Create a test placement at origin to get rotated bounding box
            PlacedPolyline test = new PlacedPolyline(item, Point3d.Origin, rotation, sheet.Index);
            double width = test.BoundingBox.Max.X - test.BoundingBox.Min.X;
            double height = test.BoundingBox.Max.Y - test.BoundingBox.Min.Y;

            // Check if it fits on the sheet at all (with margin)
            if (width + 2 * _margin > _sheetWidth || height + 2 * _margin > _sheetHeight)
                return null;

            // Use placement strategy
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
            // Create temporary placement to get dimensions
            PlacedPolyline temp = new PlacedPolyline(item, Point3d.Origin, rotation, sheet.Index);
            double width = temp.BoundingBox.Max.X - temp.BoundingBox.Min.X;
            double height = temp.BoundingBox.Max.Y - temp.BoundingBox.Min.Y;

            // Calculate sheet offset for grid layout
            double sheetOffsetX = sheet.Index * (_sheetWidth + 100);

            // Scan from bottom-left with grid resolution
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
            // For best fit, try to minimize the bounding box of all placed items
            // Start with bottom-left as fallback
            Point3d? bestPosition = FindPositionBottomLeft(item, rotation, sheet);

            if (bestPosition.HasValue && sheet.PlacedItems.Count > 0)
            {
                // Try positions that minimize total bounding box
                // This is a simplified approach - could be more sophisticated
                BoundingBox currentBounds = sheet.GetOccupiedBounds();

                // Try positions along the edges of existing items
                List<Point3d> candidatePositions = new List<Point3d>();
                double sheetOffsetX = sheet.Index * (_sheetWidth + 100);

                foreach (var placed in sheet.PlacedItems)
                {
                    // Try right of this item
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

                // Test each candidate
                foreach (var pos in candidatePositions)
                {
                    if (IsValidPosition(item, pos, rotation, sheet))
                    {
                        bestPosition = pos;
                        break; // Use first valid position found
                    }
                }
            }

            return bestPosition;
        }

        private Point3d? FindPositionGrid(PolylineItem item, double rotation, NestingSheet sheet)
        {
            // Grid placement: arrange items in a regular grid
            double sheetOffsetX = sheet.Index * (_sheetWidth + 100);

            // Calculate grid cell size based on largest item (simplified)
            double cellSize = 100; // Default cell size

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

        private bool IsValidPosition(PolylineItem item, Point3d position, double rotation, NestingSheet sheet)
        {
            // Create test placement
            PlacedPolyline test = new PlacedPolyline(item, position, rotation, sheet.Index);

            // Check sheet bounds (with margin)
            double sheetOffsetX = sheet.Index * (_sheetWidth + 100);
            if (test.BoundingBox.Min.X < sheetOffsetX + _margin ||
                test.BoundingBox.Max.X > sheetOffsetX + _sheetWidth - _margin ||
                test.BoundingBox.Min.Y < _margin ||
                test.BoundingBox.Max.Y > _sheetHeight - _margin)
            {
                return false;
            }

            // Check collisions with already placed items
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

            // Expand boxes by spacing amount
            boxA.Inflate(_spacing / 2.0);
            boxB.Inflate(_spacing / 2.0);

            // Quick rejection test
            if (!boxA.Intersects(boxB))
                return false;

            // Detailed polyline intersection test
            // Check if any segment of A intersects any segment of B
            Polyline polyA = a.TransformedGeometry;
            Polyline polyB = b.TransformedGeometry;

            // Check segment intersections
            for (int i = 0; i < polyA.Count - 1; i++)
            {
                Line lineA = new Line(polyA[i], polyA[i + 1]);

                for (int j = 0; j < polyB.Count - 1; j++)
                {
                    Line lineB = new Line(polyB[j], polyB[j + 1]);

                    double a_param, b_param;
                    if (Rhino.Geometry.Intersect.Intersection.LineLine(lineA, lineB, out a_param, out b_param, _spacing, false))
                    {
                        if (a_param >= 0 && a_param <= 1 && b_param >= 0 && b_param <= 1)
                        {
                            return true; // Collision detected
                        }
                    }
                }
            }

            // Check if one polyline is completely inside the other
            // This is a simplified check - could use point-in-polygon test
            if (IsPointInside(polyA[0], polyB) || IsPointInside(polyB[0], polyA))
            {
                return true;
            }

            return false;
        }

        private bool IsPointInside(Point3d point, Polyline polyline)
        {
            // Ray casting algorithm for point-in-polygon test
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

        #endregion
    }
}
