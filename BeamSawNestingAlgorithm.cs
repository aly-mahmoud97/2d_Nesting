/*
 * Beam Saw 2D Panel Nesting Algorithm for Rhino 8 Grasshopper
 *
 * This implementation follows strict guillotine cutting constraints:
 * - Cuts must be straight (horizontal or vertical only)
 * - Every cut must run completely through the sheet or sub-sheet
 * - Cuts cannot stop midway
 * - Kerf thickness is applied to all cuts
 * - Respects wood grain direction and rotation constraints
 *
 * Author: Claude Code
 * Date: 2025-11-16
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace BeamSawNesting
{
    #region Enumerations

    /// <summary>
    /// Rotation constraint for individual panels
    /// </summary>
    public enum RotationConstraint
    {
        NoRotation,          // Panel cannot be rotated
        Rotation90Allowed    // Panel can be rotated 0° or 90°
    }

    /// <summary>
    /// Grain direction constraint for panels
    /// </summary>
    public enum PanelGrainDirection
    {
        MatchSheet,          // Must match sheet grain direction
        FixedHorizontal,     // Must be horizontal
        FixedVertical        // Must be vertical
    }

    /// <summary>
    /// Sheet grain direction
    /// </summary>
    public enum SheetGrainDirection
    {
        Horizontal,
        Vertical
    }

    /// <summary>
    /// Cut orientation for guillotine cutting
    /// </summary>
    public enum CutOrientation
    {
        Horizontal,
        Vertical
    }

    /// <summary>
    /// Panel sorting strategy
    /// </summary>
    public enum PanelSortStrategy
    {
        LargestFirst,        // Sort by largest dimension first
        SmallestFirst,       // Sort by smallest dimension first
        AreaDescending,      // Sort by area (largest first)
        AreaAscending        // Sort by area (smallest first)
    }

    #endregion

    #region Data Structures

    /// <summary>
    /// Represents a panel to be nested
    /// </summary>
    public class Panel
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public RotationConstraint RotationConstraint { get; set; }
        public PanelGrainDirection GrainDirection { get; set; }
        public int Id { get; set; }
        public string Tag { get; set; }

        public Panel(double width, double height,
                    RotationConstraint rotation = RotationConstraint.Rotation90Allowed,
                    PanelGrainDirection grain = PanelGrainDirection.MatchSheet,
                    int id = 0, string tag = "")
        {
            Width = width;
            Height = height;
            RotationConstraint = rotation;
            GrainDirection = grain;
            Id = id;
            Tag = tag;
        }

        /// <summary>
        /// Get area of panel
        /// </summary>
        public double Area => Width * Height;

        /// <summary>
        /// Get larger dimension
        /// </summary>
        public double MaxDimension => Math.Max(Width, Height);

        /// <summary>
        /// Get smaller dimension
        /// </summary>
        public double MinDimension => Math.Min(Width, Height);
    }

    /// <summary>
    /// Represents a placed panel with position and rotation
    /// </summary>
    public class PlacedPanel
    {
        public Panel Panel { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public int RotationDegrees { get; set; }
        public string FinalGrainDirection { get; set; }
        public int SheetIndex { get; set; }

        public PlacedPanel(Panel panel, double x, double y, double width, double height,
                          int rotation, string grainDir, int sheetIndex)
        {
            Panel = panel;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            RotationDegrees = rotation;
            FinalGrainDirection = grainDir;
            SheetIndex = sheetIndex;
        }

        /// <summary>
        /// Get the rectangle boundary of the placed panel
        /// </summary>
        public Rectangle3d GetRectangle()
        {
            Plane plane = new Plane(new Point3d(X, Y, 0), Vector3d.ZAxis);
            return new Rectangle3d(plane, Width, Height);
        }

        /// <summary>
        /// Get bounding box min point
        /// </summary>
        public Point3d Min => new Point3d(X, Y, 0);

        /// <summary>
        /// Get bounding box max point
        /// </summary>
        public Point3d Max => new Point3d(X + Width, Y + Height, 0);
    }

    /// <summary>
    /// Represents a rectangular sub-sheet (either the main sheet or result of guillotine cut)
    /// </summary>
    public class SubSheet
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public int Level { get; set; }  // Depth in cut tree
        public int ParentCutId { get; set; }  // Which cut created this sub-sheet
        public int SheetIndex { get; set; }  // Track which sheet this sub-sheet belongs to

        public SubSheet(double x, double y, double width, double height, int level = 0, int parentCutId = -1, int sheetIndex = 0)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Level = level;
            ParentCutId = parentCutId;
            SheetIndex = sheetIndex;
        }

        /// <summary>
        /// Get area of sub-sheet
        /// </summary>
        public double Area => Width * Height;

        /// <summary>
        /// Get the rectangle boundary
        /// </summary>
        public Rectangle3d GetRectangle()
        {
            Plane plane = new Plane(new Point3d(X, Y, 0), Vector3d.ZAxis);
            return new Rectangle3d(plane, Width, Height);
        }

        /// <summary>
        /// Check if a panel can fit in this sub-sheet
        /// </summary>
        public bool CanFit(double panelWidth, double panelHeight)
        {
            // Use negative tolerance to provide safety margin (panels must be slightly smaller)
            const double TOLERANCE = 1e-6;
            return panelWidth <= Width - TOLERANCE && panelHeight <= Height - TOLERANCE;
        }

        /// <summary>
        /// Clone this sub-sheet
        /// </summary>
        public SubSheet Clone()
        {
            return new SubSheet(X, Y, Width, Height, Level, ParentCutId, SheetIndex);
        }
    }

    /// <summary>
    /// Represents a guillotine cut line
    /// </summary>
    public class CutLine
    {
        public int Id { get; set; }
        public CutOrientation Orientation { get; set; }
        public double Position { get; set; }  // Position along the perpendicular axis
        public double Start { get; set; }      // Start position along the cut axis
        public double End { get; set; }        // End position along the cut axis
        public double KerfThickness { get; set; }
        public int SheetIndex { get; set; }
        public SubSheet SourceSubSheet { get; set; }

        public CutLine(int id, CutOrientation orientation, double position,
                      double start, double end, double kerf, int sheetIndex, SubSheet source)
        {
            Id = id;
            Orientation = orientation;
            Position = position;
            Start = start;
            End = end;
            KerfThickness = kerf;
            SheetIndex = sheetIndex;
            SourceSubSheet = source;
        }

        /// <summary>
        /// Get the cut line as a Rhino Line
        /// </summary>
        public Line GetLine()
        {
            if (Orientation == CutOrientation.Horizontal)
            {
                return new Line(new Point3d(Start, Position, 0), new Point3d(End, Position, 0));
            }
            else
            {
                return new Line(new Point3d(Position, Start, 0), new Point3d(Position, End, 0));
            }
        }

        /// <summary>
        /// Get the kerf region (the material removed by the saw blade)
        /// </summary>
        public Rectangle3d GetKerfRectangle()
        {
            if (Orientation == CutOrientation.Horizontal)
            {
                Plane plane = new Plane(new Point3d(Start, Position - KerfThickness / 2, 0), Vector3d.ZAxis);
                return new Rectangle3d(plane, End - Start, KerfThickness);
            }
            else
            {
                Plane plane = new Plane(new Point3d(Position - KerfThickness / 2, Start, 0), Vector3d.ZAxis);
                return new Rectangle3d(plane, KerfThickness, End - Start);
            }
        }
    }

    /// <summary>
    /// Represents a cutting operation in sequence
    /// </summary>
    public class CutOperation
    {
        public int SequenceNumber { get; set; }
        public CutLine Cut { get; set; }
        public string Description { get; set; }
        public SubSheet ResultingSubSheet1 { get; set; }
        public SubSheet ResultingSubSheet2 { get; set; }

        public CutOperation(int seqNum, CutLine cut, string desc,
                          SubSheet sub1, SubSheet sub2)
        {
            SequenceNumber = seqNum;
            Cut = cut;
            Description = desc;
            ResultingSubSheet1 = sub1;
            ResultingSubSheet2 = sub2;
        }
    }

    #endregion

    #region Main Algorithm

    /// <summary>
    /// Main Beam Saw Nesting Algorithm
    /// Implements strict guillotine cutting with grain direction constraints
    /// </summary>
    public class BeamSawNestingAlgorithm
    {
        // Configuration
        private double sheetWidth;
        private double sheetHeight;
        private SheetGrainDirection sheetGrain;
        private double kerfThickness;
        private CutOrientation preferredCutOrientation;
        private PanelSortStrategy sortStrategy;

        // Results
        private List<PlacedPanel> placedPanels;
        private List<Panel> failedPanels;
        private List<SubSheet> remainingSubSheets;
        private List<CutLine> cutLines;
        private List<CutOperation> cutSequence;
        private int currentSheetIndex;
        private int nextCutId;

        public BeamSawNestingAlgorithm(
            double sheetWidth,
            double sheetHeight,
            SheetGrainDirection sheetGrain,
            double kerfThickness = 5.0,
            CutOrientation preferredCut = CutOrientation.Horizontal,
            PanelSortStrategy sortStrategy = PanelSortStrategy.AreaDescending)
        {
            this.sheetWidth = sheetWidth;
            this.sheetHeight = sheetHeight;
            this.sheetGrain = sheetGrain;
            this.kerfThickness = kerfThickness;
            this.preferredCutOrientation = preferredCut;
            this.sortStrategy = sortStrategy;

            this.placedPanels = new List<PlacedPanel>();
            this.failedPanels = new List<Panel>();
            this.remainingSubSheets = new List<SubSheet>();
            this.cutLines = new List<CutLine>();
            this.cutSequence = new List<CutOperation>();
            this.currentSheetIndex = 0;
            this.nextCutId = 0;
        }

        /// <summary>
        /// Run the nesting algorithm
        /// </summary>
        public void Nest(List<Panel> panels)
        {
            // Sort panels according to strategy
            var sortedPanels = SortPanels(panels);

            // Initialize with first sheet
            AddNewSheet();

            // Process each panel
            foreach (var panel in sortedPanels)
            {
                bool placed = TryPlacePanel(panel);

                // If couldn't place on current sheets, add a new sheet
                if (!placed)
                {
                    AddNewSheet();
                    placed = TryPlacePanel(panel);

                    if (!placed)
                    {
                        // Panel couldn't be placed - track as failure
                        failedPanels.Add(panel);
                        Console.WriteLine($"Warning: Panel {panel.Id} (size {panel.Width}x{panel.Height}) could not be placed. " +
                            $"Reason: Either too large for sheet ({sheetWidth}x{sheetHeight}) or grain constraints cannot be satisfied.");
                    }
                }
            }
        }

        /// <summary>
        /// Sort panels according to the chosen strategy
        /// </summary>
        private List<Panel> SortPanels(List<Panel> panels)
        {
            switch (sortStrategy)
            {
                case PanelSortStrategy.LargestFirst:
                    return panels.OrderByDescending(p => p.MaxDimension).ToList();

                case PanelSortStrategy.SmallestFirst:
                    return panels.OrderBy(p => p.MaxDimension).ToList();

                case PanelSortStrategy.AreaDescending:
                    return panels.OrderByDescending(p => p.Area).ToList();

                case PanelSortStrategy.AreaAscending:
                    return panels.OrderBy(p => p.Area).ToList();

                default:
                    return panels;
            }
        }

        /// <summary>
        /// Add a new sheet to the nesting
        /// </summary>
        private void AddNewSheet()
        {
            // Increment sheet index for all sheets after the first one
            if (remainingSubSheets.Count > 0 || placedPanels.Count > 0)
            {
                currentSheetIndex++;
            }

            SubSheet newSheet = new SubSheet(0, 0, sheetWidth, sheetHeight, 0, -1, currentSheetIndex);
            remainingSubSheets.Add(newSheet);
        }

        /// <summary>
        /// Try to place a panel in one of the available sub-sheets
        /// </summary>
        private bool TryPlacePanel(Panel panel)
        {
            // Only consider sub-sheets from the CURRENT sheet
            var sortedSubSheets = remainingSubSheets
                .Where(s => s.SheetIndex == currentSheetIndex)
                .OrderBy(s => s.Area)
                .ToList();

            foreach (var subSheet in sortedSubSheets)
            {
                // Try without rotation
                if (CanPlacePanelInSubSheet(panel, subSheet, false, out var placement1))
                {
                    PlacePanel(panel, subSheet, placement1);
                    return true;
                }

                // Try with 90° rotation if allowed
                if (panel.RotationConstraint == RotationConstraint.Rotation90Allowed)
                {
                    if (CanPlacePanelInSubSheet(panel, subSheet, true, out var placement2))
                    {
                        PlacePanel(panel, subSheet, placement2);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Check if a panel can be placed in a sub-sheet with grain validation
        /// </summary>
        private bool CanPlacePanelInSubSheet(Panel panel, SubSheet subSheet, bool rotate,
                                             out PanelPlacement placement)
        {
            placement = null;

            double panelW = rotate ? panel.Height : panel.Width;
            double panelH = rotate ? panel.Width : panel.Height;

            // Check if panel fits dimensionally
            if (!subSheet.CanFit(panelW, panelH))
            {
                return false;
            }

            // Validate grain direction
            string finalGrainDir;
            if (!ValidateGrainDirection(panel, rotate, out finalGrainDir))
            {
                return false;
            }

            placement = new PanelPlacement
            {
                Panel = panel,
                SubSheet = subSheet,
                Rotated = rotate,
                Width = panelW,
                Height = panelH,
                FinalGrainDirection = finalGrainDir
            };

            return true;
        }

        /// <summary>
        /// Validate grain direction constraints
        /// </summary>
        private bool ValidateGrainDirection(Panel panel, bool rotated, out string finalGrainDir)
        {
            finalGrainDir = "";
            // Explicit parentheses for clarity and correctness
            bool isHorizontal = (!rotated && panel.Width >= panel.Height) ||
                               (rotated && panel.Height >= panel.Width);

            switch (panel.GrainDirection)
            {
                case PanelGrainDirection.MatchSheet:
                    finalGrainDir = sheetGrain == SheetGrainDirection.Horizontal ? "Horizontal" : "Vertical";

                    // Check if orientation matches sheet grain
                    if (sheetGrain == SheetGrainDirection.Horizontal && !isHorizontal)
                        return false;
                    if (sheetGrain == SheetGrainDirection.Vertical && isHorizontal)
                        return false;
                    break;

                case PanelGrainDirection.FixedHorizontal:
                    finalGrainDir = "Horizontal";
                    if (!isHorizontal)
                        return false;
                    break;

                case PanelGrainDirection.FixedVertical:
                    finalGrainDir = "Vertical";
                    if (isHorizontal)
                        return false;
                    break;
            }

            return true;
        }

        /// <summary>
        /// Place a panel in a sub-sheet and perform guillotine cut
        /// </summary>
        private void PlacePanel(Panel panel, SubSheet subSheet, PanelPlacement placement)
        {
            // Validate panel stays within sheet boundaries
            const double TOLERANCE = 1e-6;
            if (subSheet.X + placement.Width > sheetWidth + TOLERANCE)
            {
                throw new InvalidOperationException(
                    $"Panel placement exceeds sheet width: Panel at X={subSheet.X:F2} with width {placement.Width:F2} " +
                    $"extends to {subSheet.X + placement.Width:F2}, but sheet width is {sheetWidth:F2}");
            }

            if (subSheet.Y + placement.Height > sheetHeight + TOLERANCE)
            {
                throw new InvalidOperationException(
                    $"Panel placement exceeds sheet height: Panel at Y={subSheet.Y:F2} with height {placement.Height:F2} " +
                    $"extends to {subSheet.Y + placement.Height:F2}, but sheet height is {sheetHeight:F2}");
            }

            // Create placed panel
            var placed = new PlacedPanel(
                panel,
                subSheet.X,
                subSheet.Y,
                placement.Width,
                placement.Height,
                placement.Rotated ? 90 : 0,
                placement.FinalGrainDirection,
                subSheet.SheetIndex  // Use the sub-sheet's sheet index
            );
            placedPanels.Add(placed);

            // Remove the used sub-sheet
            remainingSubSheets.Remove(subSheet);

            // Perform guillotine cut to create remaining sub-sheets
            PerformGuillotineCut(subSheet, placement.Width, placement.Height);
        }

        /// <summary>
        /// Perform guillotine cut and create remaining sub-sheets
        /// IMPORTANT: All cuts must extend completely through the material (true guillotine constraint)
        /// </summary>
        private void PerformGuillotineCut(SubSheet subSheet, double usedWidth, double usedHeight)
        {
            double remainingWidth = subSheet.Width - usedWidth - kerfThickness;
            double remainingHeight = subSheet.Height - usedHeight - kerfThickness;

            // Determine cut orientation based on remaining space
            bool cutVerticalFirst = preferredCutOrientation == CutOrientation.Vertical;

            // Override based on which dimension has more remaining space
            if (Math.Abs(remainingWidth - remainingHeight) > 1e-6)
            {
                cutVerticalFirst = remainingWidth > remainingHeight;
            }

            // CORRECT GUILLOTINE CUTTING PATTERN:
            // All cuts must extend completely through the material

            if (cutVerticalFirst)
            {
                // VERTICAL-FIRST APPROACH:
                // 1. Vertical cut extends FULL HEIGHT (divides into left and right)
                // 2. Horizontal cut on LEFT piece only (divides left into panel and top-left waste)

                if (remainingWidth > 1e-6)
                {
                    // First cut: VERTICAL cut through FULL HEIGHT
                    double cutX = subSheet.X + usedWidth;
                    var vCut = new CutLine(
                        nextCutId++,
                        CutOrientation.Vertical,
                        cutX,
                        subSheet.Y,
                        subSheet.Y + subSheet.Height,  // ✓ FULL HEIGHT
                        kerfThickness,
                        subSheet.SheetIndex,
                        subSheet
                    );
                    cutLines.Add(vCut);

                    // Create RIGHT sub-sheet (full height)
                    var rightSheet = new SubSheet(
                        cutX + kerfThickness,
                        subSheet.Y,
                        remainingWidth,
                        subSheet.Height,  // ✓ FULL HEIGHT
                        subSheet.Level + 1,
                        vCut.Id,
                        subSheet.SheetIndex
                    );
                    remainingSubSheets.Add(rightSheet);

                    cutSequence.Add(new CutOperation(
                        cutSequence.Count + 1,
                        vCut,
                        $"Vertical guillotine cut at X={cutX:F2} (full height)",
                        rightSheet,
                        null
                    ));
                }

                if (remainingHeight > 1e-6)
                {
                    // Second cut: HORIZONTAL cut across the LEFT piece only
                    double cutY = subSheet.Y + usedHeight;
                    var hCut = new CutLine(
                        nextCutId++,
                        CutOrientation.Horizontal,
                        cutY,
                        subSheet.X,
                        subSheet.X + usedWidth,  // ✓ FULL WIDTH of left piece
                        kerfThickness,
                        subSheet.SheetIndex,
                        subSheet
                    );
                    cutLines.Add(hCut);

                    // Create TOP-LEFT sub-sheet
                    var topLeftSheet = new SubSheet(
                        subSheet.X,
                        cutY + kerfThickness,
                        usedWidth,  // Width of left piece only
                        remainingHeight,
                        subSheet.Level + 1,
                        hCut.Id,
                        subSheet.SheetIndex
                    );
                    remainingSubSheets.Add(topLeftSheet);

                    cutSequence.Add(new CutOperation(
                        cutSequence.Count + 1,
                        hCut,
                        $"Horizontal guillotine cut at Y={cutY:F2} (left piece)",
                        null,
                        topLeftSheet
                    ));
                }
            }
            else
            {
                // HORIZONTAL-FIRST APPROACH:
                // 1. Horizontal cut extends FULL WIDTH (divides into bottom and top)
                // 2. Vertical cut on BOTTOM piece only (divides bottom into panel and bottom-right waste)

                if (remainingHeight > 1e-6)
                {
                    // First cut: HORIZONTAL cut through FULL WIDTH
                    double cutY = subSheet.Y + usedHeight;
                    var hCut = new CutLine(
                        nextCutId++,
                        CutOrientation.Horizontal,
                        cutY,
                        subSheet.X,
                        subSheet.X + subSheet.Width,  // ✓ FULL WIDTH
                        kerfThickness,
                        subSheet.SheetIndex,
                        subSheet
                    );
                    cutLines.Add(hCut);

                    // Create TOP sub-sheet (full width)
                    var topSheet = new SubSheet(
                        subSheet.X,
                        cutY + kerfThickness,
                        subSheet.Width,  // ✓ FULL WIDTH
                        remainingHeight,
                        subSheet.Level + 1,
                        hCut.Id,
                        subSheet.SheetIndex
                    );
                    remainingSubSheets.Add(topSheet);

                    cutSequence.Add(new CutOperation(
                        cutSequence.Count + 1,
                        hCut,
                        $"Horizontal guillotine cut at Y={cutY:F2} (full width)",
                        null,
                        topSheet
                    ));
                }

                if (remainingWidth > 1e-6)
                {
                    // Second cut: VERTICAL cut through the BOTTOM piece only
                    double cutX = subSheet.X + usedWidth;
                    var vCut = new CutLine(
                        nextCutId++,
                        CutOrientation.Vertical,
                        cutX,
                        subSheet.Y,
                        subSheet.Y + usedHeight,  // ✓ FULL HEIGHT of bottom piece
                        kerfThickness,
                        subSheet.SheetIndex,
                        subSheet
                    );
                    cutLines.Add(vCut);

                    // Create BOTTOM-RIGHT sub-sheet
                    var bottomRightSheet = new SubSheet(
                        cutX + kerfThickness,
                        subSheet.Y,
                        remainingWidth,
                        usedHeight,  // Height of bottom piece only
                        subSheet.Level + 1,
                        vCut.Id,
                        subSheet.SheetIndex
                    );
                    remainingSubSheets.Add(bottomRightSheet);

                    cutSequence.Add(new CutOperation(
                        cutSequence.Count + 1,
                        vCut,
                        $"Vertical guillotine cut at X={cutX:F2} (bottom piece)",
                        bottomRightSheet,
                        null
                    ));
                }
            }
        }

        #region Public Accessors

        public List<PlacedPanel> GetPlacedPanels() => placedPanels;
        public List<Panel> GetFailedPanels() => failedPanels;
        public List<SubSheet> GetRemainingSubSheets() => remainingSubSheets;
        public List<CutLine> GetCutLines() => cutLines;
        public List<CutOperation> GetCutSequence() => cutSequence;
        public int GetSheetCount() => currentSheetIndex + 1;

        /// <summary>
        /// Get utilization percentage for each sheet
        /// </summary>
        public List<double> GetSheetUtilization()
        {
            var utilization = new List<double>();
            double sheetArea = sheetWidth * sheetHeight;

            for (int i = 0; i <= currentSheetIndex; i++)
            {
                var panelsOnSheet = placedPanels.Where(p => p.SheetIndex == i).ToList();
                double usedArea = panelsOnSheet.Sum(p => p.Width * p.Height);
                utilization.Add((usedArea / sheetArea) * 100.0);
            }

            return utilization;
        }

        /// <summary>
        /// Get overall efficiency
        /// </summary>
        public double GetOverallEfficiency()
        {
            double totalSheetArea = (currentSheetIndex + 1) * sheetWidth * sheetHeight;
            double totalPanelArea = placedPanels.Sum(p => p.Width * p.Height);
            return (totalPanelArea / totalSheetArea) * 100.0;
        }

        #endregion
    }

    /// <summary>
    /// Helper class for panel placement
    /// </summary>
    internal class PanelPlacement
    {
        public Panel Panel { get; set; }
        public SubSheet SubSheet { get; set; }
        public bool Rotated { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string FinalGrainDirection { get; set; }
    }

    #endregion
}
