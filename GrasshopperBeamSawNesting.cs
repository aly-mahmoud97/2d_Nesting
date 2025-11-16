/*
 * GRASSHOPPER C# SCRIPT COMPONENT - BEAM SAW PANEL NESTING
 *
 * Copy this entire script into a C# Script component in Grasshopper
 *
 * ===== INPUTS (Right-click component → Manage Inputs) =====
 * Name                 Type          Description
 * ----------------------------------------------------------------
 * SheetWidth           double        Width of the sheet material
 * SheetHeight          double        Height of the sheet material
 * SheetGrain           string        "Horizontal" or "Vertical"
 * PanelWidths          List<double>  List of panel widths
 * PanelHeights         List<double>  List of panel heights
 * RotationAllowed      List<bool>    Can each panel rotate 90°? (optional, default: all true)
 * PanelGrains          List<string>  Grain per panel: "MatchSheet", "FixedHorizontal", "FixedVertical" (optional)
 * Kerf                 double        Saw blade thickness (default: 5.0)
 * CutOrientationPref   string        "Horizontal" or "Vertical" - preferred first cut (optional)
 * SortStrategy         string        "LargestFirst", "SmallestFirst", "AreaDescending", "AreaAscending" (optional)
 * Run                  bool          Set to true to run the algorithm
 *
 * ===== OUTPUTS (Right-click component → Manage Outputs) =====
 * Name              Type          Description
 * ----------------------------------------------------------------
 * PlacedRectangles  List          Rectangles showing placed panel positions (in grid layout)
 * PanelInfo         List          Detailed info for each placed panel
 * SheetRectangles   List          Sheet boundaries (in grid layout)
 * CutLines          List          All guillotine cut lines (in grid layout)
 * KerfRegions       List          Visual representation of kerf (in grid layout)
 * RemainingSheets   List          Unused sub-sheet rectangles (in grid layout)
 * CutSequence       List          Ordered cutting operations for manufacturing
 * Statistics        List          Summary statistics
 * Transforms        List          Transformations from origin (0,0,0) to each panel position
 * PanelTags         List          Text tags for each panel (Width x Height, ID)
 * A                 string        Debug messages and status
 *
 */

// Grasshopper Script Instance
#region Usings
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;

using Rhino;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

// ============================================================================
// ENUMERATIONS
// ============================================================================

public enum RotationConstraint
{
    NoRotation,
    Rotation90Allowed
}

public enum PanelGrainDirection
{
    MatchSheet,
    FixedHorizontal,
    FixedVertical
}

public enum SheetGrainDirection
{
    Horizontal,
    Vertical
}

public enum CutOrientation
{
    Horizontal,
    Vertical
}

public enum PanelSortStrategy
{
    LargestFirst,
    SmallestFirst,
    AreaDescending,
    AreaAscending
}

// ============================================================================
// DATA CLASSES
// ============================================================================

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

    public double Area => Width * Height;
    public double MaxDimension => Math.Max(Width, Height);
    public double MinDimension => Math.Min(Width, Height);
}

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

    public Rectangle3d GetRectangle()
    {
        Plane plane = new Plane(new Point3d(X, Y, 0), Vector3d.ZAxis);
        return new Rectangle3d(plane, Width, Height);
    }

    public Point3d Min => new Point3d(X, Y, 0);
    public Point3d Max => new Point3d(X + Width, Y + Height, 0);
}

public class SubSheet
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int Level { get; set; }
    public int ParentCutId { get; set; }
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

    public double Area => Width * Height;

    public Rectangle3d GetRectangle()
    {
        Plane plane = new Plane(new Point3d(X, Y, 0), Vector3d.ZAxis);
        return new Rectangle3d(plane, Width, Height);
    }

    public bool CanFit(double panelWidth, double panelHeight)
    {
        return panelWidth <= Width + 1e-6 && panelHeight <= Height + 1e-6;
    }

    public SubSheet Clone()
    {
        return new SubSheet(X, Y, Width, Height, Level, ParentCutId, SheetIndex);
    }
}

public class CutLine
{
    public int Id { get; set; }
    public CutOrientation Orientation { get; set; }
    public double Position { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
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

public class CutOperation
{
    public int SequenceNumber { get; set; }
    public CutLine Cut { get; set; }
    public string Description { get; set; }
    public SubSheet ResultingSubSheet1 { get; set; }
    public SubSheet ResultingSubSheet2 { get; set; }

    public CutOperation(int seqNum, CutLine cut, string desc, SubSheet sub1, SubSheet sub2)
    {
        SequenceNumber = seqNum;
        Cut = cut;
        Description = desc;
        ResultingSubSheet1 = sub1;
        ResultingSubSheet2 = sub2;
    }
}

// ============================================================================
// ALGORITHM
// ============================================================================

public class BeamSawNestingAlgorithm
{
    private double sheetWidth;
    private double sheetHeight;
    private SheetGrainDirection sheetGrain;
    private double kerfThickness;
    private CutOrientation preferredCutOrientation;
    private PanelSortStrategy sortStrategy;

    private List<PlacedPanel> placedPanels;
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
        this.remainingSubSheets = new List<SubSheet>();
        this.cutLines = new List<CutLine>();
        this.cutSequence = new List<CutOperation>();
        this.currentSheetIndex = 0;
        this.nextCutId = 0;
    }

    public void Nest(List<Panel> panels)
    {
        var sortedPanels = SortPanels(panels);
        AddNewSheet();

        foreach (var panel in sortedPanels)
        {
            bool placed = TryPlacePanel(panel);

            if (!placed)
            {
                AddNewSheet();
                placed = TryPlacePanel(panel);

                if (!placed)
                {
                    Console.WriteLine($"Warning: Panel {panel.Id} (size {panel.Width}x{panel.Height}) is too large for sheet {sheetWidth}x{sheetHeight}");
                }
            }
        }
    }

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

    private bool TryPlacePanel(Panel panel)
    {
        // Only consider sub-sheets from the CURRENT sheet
        var currentSheetSubSheets = remainingSubSheets
            .Where(s => s.SheetIndex == currentSheetIndex)
            .OrderBy(s => s.Area)
            .ToList();

        foreach (var subSheet in currentSheetSubSheets)
        {
            if (CanPlacePanelInSubSheet(panel, subSheet, false, out var placement1))
            {
                PlacePanel(panel, subSheet, placement1);
                return true;
            }

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

    private bool CanPlacePanelInSubSheet(Panel panel, SubSheet subSheet, bool rotate, out PanelPlacement placement)
    {
        placement = null;

        double panelW = rotate ? panel.Height : panel.Width;
        double panelH = rotate ? panel.Width : panel.Height;

        if (!subSheet.CanFit(panelW, panelH))
            return false;

        string finalGrainDir;
        if (!ValidateGrainDirection(panel, rotate, out finalGrainDir))
            return false;

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

    private bool ValidateGrainDirection(Panel panel, bool rotated, out string finalGrainDir)
    {
        finalGrainDir = "";
        bool isHorizontal = !rotated && panel.Width >= panel.Height || rotated && panel.Height >= panel.Width;

        switch (panel.GrainDirection)
        {
            case PanelGrainDirection.MatchSheet:
                finalGrainDir = sheetGrain == SheetGrainDirection.Horizontal ? "Horizontal" : "Vertical";
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

    private void PlacePanel(Panel panel, SubSheet subSheet, PanelPlacement placement)
    {
        var placed = new PlacedPanel(
            panel,
            subSheet.X,
            subSheet.Y,
            placement.Width,
            placement.Height,
            placement.Rotated ? 90 : 0,
            placement.FinalGrainDirection,
            subSheet.SheetIndex  // Use the sub-sheet's sheet index, not currentSheetIndex
        );
        placedPanels.Add(placed);
        remainingSubSheets.Remove(subSheet);
        PerformGuillotineCut(subSheet, placement.Width, placement.Height);
    }

    private void PerformGuillotineCut(SubSheet subSheet, double usedWidth, double usedHeight)
    {
        double remainingWidth = subSheet.Width - usedWidth - kerfThickness;
        double remainingHeight = subSheet.Height - usedHeight - kerfThickness;

        bool cutHorizontalFirst = preferredCutOrientation == CutOrientation.Horizontal;

        if (Math.Abs(remainingWidth - remainingHeight) > 1e-6)
        {
            cutHorizontalFirst = remainingHeight > remainingWidth;
        }

        if (cutHorizontalFirst)
        {
            if (remainingHeight > 1e-6)
            {
                double cutY = subSheet.Y + usedHeight;
                var hCut = new CutLine(nextCutId++, CutOrientation.Horizontal, cutY,
                    subSheet.X, subSheet.X + subSheet.Width, kerfThickness, subSheet.SheetIndex, subSheet);
                cutLines.Add(hCut);

                var topSheet = new SubSheet(subSheet.X, cutY + kerfThickness,
                    subSheet.Width, remainingHeight, subSheet.Level + 1, hCut.Id, subSheet.SheetIndex);
                remainingSubSheets.Add(topSheet);

                cutSequence.Add(new CutOperation(cutSequence.Count + 1, hCut,
                    $"Horizontal cut at Y={cutY:F2}", null, topSheet));
            }

            if (remainingWidth > 1e-6)
            {
                double cutX = subSheet.X + usedWidth;
                var vCut = new CutLine(nextCutId++, CutOrientation.Vertical, cutX,
                    subSheet.Y, subSheet.Y + usedHeight, kerfThickness, subSheet.SheetIndex, subSheet);
                cutLines.Add(vCut);

                var rightSheet = new SubSheet(cutX + kerfThickness, subSheet.Y,
                    remainingWidth, usedHeight, subSheet.Level + 1, vCut.Id, subSheet.SheetIndex);
                remainingSubSheets.Add(rightSheet);

                cutSequence.Add(new CutOperation(cutSequence.Count + 1, vCut,
                    $"Vertical cut at X={cutX:F2}", rightSheet, null));
            }
        }
        else
        {
            if (remainingWidth > 1e-6)
            {
                double cutX = subSheet.X + usedWidth;
                var vCut = new CutLine(nextCutId++, CutOrientation.Vertical, cutX,
                    subSheet.Y, subSheet.Y + subSheet.Height, kerfThickness, subSheet.SheetIndex, subSheet);
                cutLines.Add(vCut);

                var rightSheet = new SubSheet(cutX + kerfThickness, subSheet.Y,
                    remainingWidth, subSheet.Height, subSheet.Level + 1, vCut.Id, subSheet.SheetIndex);
                remainingSubSheets.Add(rightSheet);

                cutSequence.Add(new CutOperation(cutSequence.Count + 1, vCut,
                    $"Vertical cut at X={cutX:F2}", rightSheet, null));
            }

            if (remainingHeight > 1e-6)
            {
                double cutY = subSheet.Y + usedHeight;
                var hCut = new CutLine(nextCutId++, CutOrientation.Horizontal, cutY,
                    subSheet.X, subSheet.X + usedWidth, kerfThickness, subSheet.SheetIndex, subSheet);
                cutLines.Add(hCut);

                var topSheet = new SubSheet(subSheet.X, cutY + kerfThickness,
                    usedWidth, remainingHeight, subSheet.Level + 1, hCut.Id, subSheet.SheetIndex);
                remainingSubSheets.Add(topSheet);

                cutSequence.Add(new CutOperation(cutSequence.Count + 1, hCut,
                    $"Horizontal cut at Y={cutY:F2}", null, topSheet));
            }
        }
    }

    public List<PlacedPanel> GetPlacedPanels() => placedPanels;
    public List<SubSheet> GetRemainingSubSheets() => remainingSubSheets;
    public List<CutLine> GetCutLines() => cutLines;
    public List<CutOperation> GetCutSequence() => cutSequence;
    public int GetSheetCount() => currentSheetIndex + 1;

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

    public double GetOverallEfficiency()
    {
        double totalSheetArea = (currentSheetIndex + 1) * sheetWidth * sheetHeight;
        double totalPanelArea = placedPanels.Sum(p => p.Width * p.Height);
        return (totalPanelArea / totalSheetArea) * 100.0;
    }
}

internal class PanelPlacement
{
    public Panel Panel { get; set; }
    public SubSheet SubSheet { get; set; }
    public bool Rotated { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string FinalGrainDirection { get; set; }
}

// ============================================================================
// GRASSHOPPER SCRIPT INSTANCE
// ============================================================================

public class Script_Instance : GH_ScriptInstance
{
    #region Notes
    /*
      Members:
        RhinoDoc RhinoDocument
        GH_Document GrasshopperDocument
        IGH_Component Component
        int Iteration

      Methods (Virtual & overridable):
        Print(string text)
        Print(string format, params object[] args)
        Reflect(object obj)
        Reflect(object obj, string method_name)
    */
    #endregion

    private void RunScript(
        double SheetWidth,
        double SheetHeight,
        string SheetGrain,
        List<double> PanelWidths,
        List<double> PanelHeights,
        List<bool> RotationAllowed,
        List<string> PanelGrains,
        double Kerf,
        string CutOrientationPref,
        string SortStrategy,
        bool Run,
        ref object PlacedRectangles,
        ref object PanelInfo,
        ref object SheetRectangles,
        ref object CutLines,
        ref object KerfRegions,
        ref object RemainingSheets,
        ref object CutSequence,
        ref object Statistics,
        ref object Transforms,
        ref object PanelTags,
        ref object A)
    {
        var debugMessages = new List<string>();

        try
        {
            if (!Run)
            {
                A = "Set 'Run' to true to execute the algorithm";
                return;
            }

            // Validate inputs
            if (PanelWidths == null || PanelHeights == null || PanelWidths.Count == 0)
            {
                A = "ERROR: PanelWidths and PanelHeights are required";
                return;
            }

            if (PanelWidths.Count != PanelHeights.Count)
            {
                A = "ERROR: PanelWidths and PanelHeights must have the same count";
                return;
            }

            if (SheetWidth <= 0 || SheetHeight <= 0)
            {
                A = "ERROR: SheetWidth and SheetHeight must be positive";
                return;
            }

            if (Kerf < 0)
            {
                A = "ERROR: Kerf cannot be negative";
                return;
            }

            debugMessages.Add($"=== BEAM SAW NESTING ALGORITHM ===");
            debugMessages.Add($"Sheet: {SheetWidth} x {SheetHeight}");
            debugMessages.Add($"Panels to nest: {PanelWidths.Count}");
            debugMessages.Add($"Kerf: {Kerf}");

            // Parse sheet grain
            SheetGrainDirection sheetGrainDir = SheetGrainDirection.Horizontal;
            if (!string.IsNullOrEmpty(SheetGrain))
            {
                if (SheetGrain.ToLower() == "vertical")
                    sheetGrainDir = SheetGrainDirection.Vertical;
            }
            debugMessages.Add($"Sheet grain: {sheetGrainDir}");

            // Parse cut orientation preference
            CutOrientation cutOrient = CutOrientation.Horizontal;
            if (!string.IsNullOrEmpty(CutOrientationPref))
            {
                if (CutOrientationPref.ToLower() == "vertical")
                    cutOrient = CutOrientation.Vertical;
            }

            // Parse sort strategy
            PanelSortStrategy sortStrat = PanelSortStrategy.AreaDescending;
            if (!string.IsNullOrEmpty(SortStrategy))
            {
                switch (SortStrategy.ToLower())
                {
                    case "largestfirst":
                        sortStrat = PanelSortStrategy.LargestFirst;
                        break;
                    case "smallestfirst":
                        sortStrat = PanelSortStrategy.SmallestFirst;
                        break;
                    case "areaascending":
                        sortStrat = PanelSortStrategy.AreaAscending;
                        break;
                }
            }

            // Create panels
            var panels = new List<Panel>();
            for (int i = 0; i < PanelWidths.Count; i++)
            {
                // Determine rotation constraint
                RotationConstraint rotConstraint = RotationConstraint.Rotation90Allowed;
                if (RotationAllowed != null && i < RotationAllowed.Count && !RotationAllowed[i])
                {
                    rotConstraint = RotationConstraint.NoRotation;
                }

                // Determine grain direction
                PanelGrainDirection grainDir = PanelGrainDirection.MatchSheet;
                if (PanelGrains != null && i < PanelGrains.Count && !string.IsNullOrEmpty(PanelGrains[i]))
                {
                    string grain = PanelGrains[i].ToLower();
                    if (grain.Contains("horizontal"))
                        grainDir = PanelGrainDirection.FixedHorizontal;
                    else if (grain.Contains("vertical"))
                        grainDir = PanelGrainDirection.FixedVertical;
                }

                panels.Add(new Panel(PanelWidths[i], PanelHeights[i], rotConstraint, grainDir, i, $"Panel_{i}"));
            }

            debugMessages.Add($"Panels created successfully");
            debugMessages.Add("");

            // Run algorithm
            var algorithm = new BeamSawNestingAlgorithm(
                SheetWidth,
                SheetHeight,
                sheetGrainDir,
                Kerf,
                cutOrient,
                sortStrat
            );

            algorithm.Nest(panels);

            var placed = algorithm.GetPlacedPanels();
            var remaining = algorithm.GetRemainingSubSheets();
            var cuts = algorithm.GetCutLines();
            var sequence = algorithm.GetCutSequence();

            debugMessages.Add($"=== NESTING RESULTS ===");
            debugMessages.Add($"Sheets used: {algorithm.GetSheetCount()}");
            debugMessages.Add($"Panels placed: {placed.Count} / {panels.Count}");
            debugMessages.Add($"Overall efficiency: {algorithm.GetOverallEfficiency():F2}%");
            debugMessages.Add($"Total cuts: {cuts.Count}");
            debugMessages.Add("");

            // Calculate grid layout for sheets
            int sheetCount = algorithm.GetSheetCount();
            int cols = (int)Math.Ceiling(Math.Sqrt(sheetCount));
            double spacing = Math.Max(SheetWidth, SheetHeight) * 0.2; // 20% spacing between sheets

            // Create sheet offset vectors for grid layout
            var sheetOffsets = new Dictionary<int, Vector3d>();
            for (int i = 0; i < sheetCount; i++)
            {
                int row = i / cols;
                int col = i % cols;
                double offsetX = col * (SheetWidth + spacing);
                double offsetY = -row * (SheetHeight + spacing); // Negative Y to go down
                sheetOffsets[i] = new Vector3d(offsetX, offsetY, 0);
            }

            // Generate outputs with grid layout
            var placedRects = new List<Rectangle3d>();
            var panelInfoList = new List<string>();
            var transformList = new List<Transform>();
            var panelTagList = new List<string>();

            foreach (var p in placed)
            {
                // Get sheet offset for grid layout
                Vector3d offset = sheetOffsets[p.SheetIndex];

                // Create rectangle in grid position
                Plane plane = new Plane(new Point3d(p.X + offset.X, p.Y + offset.Y, 0), Vector3d.ZAxis);
                placedRects.Add(new Rectangle3d(plane, p.Width, p.Height));

                // Panel info
                panelInfoList.Add($"Panel {p.Panel.Id}: " +
                    $"Pos=({p.X:F1},{p.Y:F1}), " +
                    $"Size={p.Width:F1}x{p.Height:F1}, " +
                    $"Rotation={p.RotationDegrees}°, " +
                    $"Grain={p.FinalGrainDirection}, " +
                    $"Sheet={p.SheetIndex}");

                // Create transformation from origin to final position
                Transform transform = Transform.Translation(p.X + offset.X, p.Y + offset.Y, 0);
                if (p.RotationDegrees != 0)
                {
                    // Apply rotation around panel center point
                    Point3d center = new Point3d(
                        p.X + p.Width / 2 + offset.X,
                        p.Y + p.Height / 2 + offset.Y,
                        0
                    );
                    Transform rotation = Transform.Rotation(
                        Math.PI / 2 * (p.RotationDegrees / 90),
                        Vector3d.ZAxis,
                        center
                    );
                    transform = transform * rotation;
                }
                transformList.Add(transform);

                // Create panel tag
                panelTagList.Add($"{p.Width:F0}×{p.Height:F0} (#{p.Panel.Id})");
            }

            PlacedRectangles = placedRects;
            PanelInfo = panelInfoList;
            Transforms = transformList;
            PanelTags = panelTagList;

            // Sheet rectangles in grid layout
            var sheetRects = new List<Rectangle3d>();
            for (int i = 0; i < sheetCount; i++)
            {
                Vector3d offset = sheetOffsets[i];
                Plane plane = new Plane(new Point3d(offset.X, offset.Y, 0), Vector3d.ZAxis);
                sheetRects.Add(new Rectangle3d(plane, SheetWidth, SheetHeight));
            }
            SheetRectangles = sheetRects;

            // Cut lines in grid layout
            var cutLineGeometry = new List<Line>();
            foreach (var c in cuts)
            {
                Vector3d offset = sheetOffsets[c.SheetIndex];
                Line line = c.GetLine();
                line.Transform(Transform.Translation(offset));
                cutLineGeometry.Add(line);
            }
            CutLines = cutLineGeometry;

            // Kerf regions in grid layout
            var kerfRects = new List<Rectangle3d>();
            foreach (var c in cuts)
            {
                Vector3d offset = sheetOffsets[c.SheetIndex];
                Rectangle3d kerfRect = c.GetKerfRectangle();
                kerfRect.Transform(Transform.Translation(offset));
                kerfRects.Add(kerfRect);
            }
            KerfRegions = kerfRects;

            // Remaining sub-sheets in grid layout
            var remainingRects = new List<Rectangle3d>();
            foreach (var r in remaining)
            {
                Vector3d offset = sheetOffsets[r.SheetIndex];
                Rectangle3d rect = r.GetRectangle();
                rect.Transform(Transform.Translation(offset));
                remainingRects.Add(rect);
            }
            RemainingSheets = remainingRects;

            // Cut sequence
            var sequenceInfo = new List<string>();
            foreach (var op in sequence)
            {
                sequenceInfo.Add($"Step {op.SequenceNumber}: {op.Description}");
            }
            CutSequence = sequenceInfo;

            // Statistics
            var stats = new List<string>();
            stats.Add($"Total sheets: {algorithm.GetSheetCount()}");
            stats.Add($"Panels placed: {placed.Count} / {panels.Count}");
            stats.Add($"Total cuts: {cuts.Count}");
            stats.Add($"Overall efficiency: {algorithm.GetOverallEfficiency():F2}%");

            var utilization = algorithm.GetSheetUtilization();
            for (int i = 0; i < utilization.Count; i++)
            {
                stats.Add($"Sheet {i} efficiency: {utilization[i]:F2}%");
            }
            Statistics = stats;

            debugMessages.Add("SUCCESS: Algorithm completed");
            A = string.Join("\n", debugMessages);
        }
        catch (Exception ex)
        {
            debugMessages.Add($"ERROR: {ex.Message}");
            debugMessages.Add($"Stack trace: {ex.StackTrace}");
            A = string.Join("\n", debugMessages);
        }
    }
}
