/*
 * GRASSHOPPER C# SCRIPT COMPONENT - BEAM SAW PANEL NESTING
 *
 * Copy this entire script into a C# Script component in Grasshopper
 *
 * ===== INPUTS (Right-click component → Manage Inputs) =====
 * Name              Type          Description
 * ----------------------------------------------------------------
 * SheetWidth        double        Width of the sheet material
 * SheetHeight       double        Height of the sheet material
 * SheetGrain        string        "Horizontal" or "Vertical"
 * PanelWidths       List<double>  List of panel widths
 * PanelHeights      List<double>  List of panel heights
 * RotationAllowed   List<bool>    Can each panel rotate 90°? (optional, default: all true)
 * PanelGrains       List<string>  Grain per panel: "MatchSheet", "FixedHorizontal", "FixedVertical" (optional)
 * Kerf              double        Saw blade thickness (default: 5.0)
 * CutOrientation    string        "Horizontal" or "Vertical" - preferred first cut (optional)
 * SortStrategy      string        "LargestFirst", "SmallestFirst", "AreaDescending", "AreaAscending" (optional)
 * Run               bool          Set to true to run the algorithm
 *
 * ===== OUTPUTS (Right-click component → Manage Outputs) =====
 * Name              Type          Description
 * ----------------------------------------------------------------
 * PlacedRectangles  List          Rectangles showing placed panel positions
 * PanelInfo         List          Detailed info for each placed panel
 * SheetRectangles   List          Sheet boundaries
 * CutLines          List          All guillotine cut lines
 * KerfRegions       List          Visual representation of kerf (material removed)
 * RemainingSheets   List          Unused sub-sheet rectangles
 * CutSequence       List          Ordered cutting operations for manufacturing
 * Statistics        List          Summary statistics
 * Debug             string        Debug messages
 *
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

#region Enumerations

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

#endregion

#region Data Classes

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

    public SubSheet(double x, double y, double width, double height, int level = 0, int parentCutId = -1)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Level = level;
        ParentCutId = parentCutId;
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
        return new SubSheet(X, Y, Width, Height, Level, ParentCutId);
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

#endregion

#region Algorithm

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
                    Print($"Warning: Panel {panel.Id} (size {panel.Width}x{panel.Height}) is too large for sheet {sheetWidth}x{sheetHeight}");
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
        SubSheet newSheet = new SubSheet(0, 0, sheetWidth, sheetHeight, 0, -1);
        remainingSubSheets.Add(newSheet);
    }

    private bool TryPlacePanel(Panel panel)
    {
        var sortedSubSheets = remainingSubSheets.OrderBy(s => s.Area).ToList();

        foreach (var subSheet in sortedSubSheets)
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
            currentSheetIndex
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
                    subSheet.X, subSheet.X + subSheet.Width, kerfThickness, currentSheetIndex, subSheet);
                cutLines.Add(hCut);

                var topSheet = new SubSheet(subSheet.X, cutY + kerfThickness,
                    subSheet.Width, remainingHeight, subSheet.Level + 1, hCut.Id);
                remainingSubSheets.Add(topSheet);

                cutSequence.Add(new CutOperation(cutSequence.Count + 1, hCut,
                    $"Horizontal cut at Y={cutY:F2}", null, topSheet));
            }

            if (remainingWidth > 1e-6)
            {
                double cutX = subSheet.X + usedWidth;
                var vCut = new CutLine(nextCutId++, CutOrientation.Vertical, cutX,
                    subSheet.Y, subSheet.Y + usedHeight, kerfThickness, currentSheetIndex, subSheet);
                cutLines.Add(vCut);

                var rightSheet = new SubSheet(cutX + kerfThickness, subSheet.Y,
                    remainingWidth, usedHeight, subSheet.Level + 1, vCut.Id);
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
                    subSheet.Y, subSheet.Y + subSheet.Height, kerfThickness, currentSheetIndex, subSheet);
                cutLines.Add(vCut);

                var rightSheet = new SubSheet(cutX + kerfThickness, subSheet.Y,
                    remainingWidth, subSheet.Height, subSheet.Level + 1, vCut.Id);
                remainingSubSheets.Add(rightSheet);

                cutSequence.Add(new CutOperation(cutSequence.Count + 1, vCut,
                    $"Vertical cut at X={cutX:F2}", rightSheet, null));
            }

            if (remainingHeight > 1e-6)
            {
                double cutY = subSheet.Y + usedHeight;
                var hCut = new CutLine(nextCutId++, CutOrientation.Horizontal, cutY,
                    subSheet.X, subSheet.X + usedWidth, kerfThickness, currentSheetIndex, subSheet);
                cutLines.Add(hCut);

                var topSheet = new SubSheet(subSheet.X, cutY + kerfThickness,
                    usedWidth, remainingHeight, subSheet.Level + 1, hCut.Id);
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

#endregion

// ============================================================================
// GRASSHOPPER SCRIPT MAIN CODE
// ============================================================================

private void RunScript(
    double SheetWidth,
    double SheetHeight,
    string SheetGrain,
    List<double> PanelWidths,
    List<double> PanelHeights,
    List<bool> RotationAllowed,
    List<string> PanelGrains,
    double Kerf,
    string CutOrientation,
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
    ref object Debug)
{
    var debugMessages = new List<string>();

    try
    {
        if (!Run)
        {
            Debug = "Set 'Run' to true to execute the algorithm";
            return;
        }

        // Validate inputs
        if (PanelWidths == null || PanelHeights == null || PanelWidths.Count == 0)
        {
            Debug = "ERROR: PanelWidths and PanelHeights are required";
            return;
        }

        if (PanelWidths.Count != PanelHeights.Count)
        {
            Debug = "ERROR: PanelWidths and PanelHeights must have the same count";
            return;
        }

        if (SheetWidth <= 0 || SheetHeight <= 0)
        {
            Debug = "ERROR: SheetWidth and SheetHeight must be positive";
            return;
        }

        if (Kerf < 0)
        {
            Debug = "ERROR: Kerf cannot be negative";
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
        if (!string.IsNullOrEmpty(CutOrientation))
        {
            if (CutOrientation.ToLower() == "vertical")
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

        // Generate outputs
        var placedRects = new List<Rectangle3d>();
        var panelInfoList = new List<string>();

        foreach (var p in placed)
        {
            placedRects.Add(p.GetRectangle());
            panelInfoList.Add($"Panel {p.Panel.Id}: " +
                $"Pos=({p.X:F1},{p.Y:F1}), " +
                $"Size={p.Width:F1}x{p.Height:F1}, " +
                $"Rotation={p.RotationDegrees}°, " +
                $"Grain={p.FinalGrainDirection}, " +
                $"Sheet={p.SheetIndex}");
        }

        PlacedRectangles = placedRects;
        PanelInfo = panelInfoList;

        // Sheet rectangles
        var sheetRects = new List<Rectangle3d>();
        for (int i = 0; i < algorithm.GetSheetCount(); i++)
        {
            Plane plane = new Plane(new Point3d(0, 0, 0), Vector3d.ZAxis);
            sheetRects.Add(new Rectangle3d(plane, SheetWidth, SheetHeight));
        }
        SheetRectangles = sheetRects;

        // Cut lines
        var cutLineGeometry = cuts.Select(c => c.GetLine()).ToList();
        CutLines = cutLineGeometry;

        // Kerf regions
        var kerfRects = cuts.Select(c => c.GetKerfRectangle()).ToList();
        KerfRegions = kerfRects;

        // Remaining sub-sheets
        var remainingRects = remaining.Select(r => r.GetRectangle()).ToList();
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
        Debug = string.Join("\n", debugMessages);
    }
    catch (Exception ex)
    {
        debugMessages.Add($"ERROR: {ex.Message}");
        debugMessages.Add($"Stack trace: {ex.StackTrace}");
        Debug = string.Join("\n", debugMessages);
    }
}
