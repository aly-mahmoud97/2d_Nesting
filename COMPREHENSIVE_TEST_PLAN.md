# Comprehensive Test Plan for Beam Saw Nesting Algorithm

**Purpose:** Detect critical bugs and ensure correctness
**Target Coverage:** 85%+ code coverage
**Priority:** Focus on critical bugs first

---

## Test Suite Structure

```
BeamSawNesting.Tests/
├── CriticalBugs/
│   ├── Bug1_GuillotineCuttingTests.cs
│   ├── Bug2_ToleranceTests.cs
│   ├── Bug3_BoundaryValidationTests.cs
│   ├── Bug4_FailedPanelTrackingTests.cs
│   └── Bug5_GrainDirectionTests.cs
├── DataStructures/
│   ├── PanelTests.cs
│   ├── SubSheetTests.cs
│   ├── PlacedPanelTests.cs
│   └── CutLineTests.cs
├── Algorithm/
│   ├── NestingWorkflowTests.cs
│   ├── PanelSortingTests.cs
│   ├── PlacementStrategyTests.cs
│   └── UtilizationTests.cs
├── Integration/
│   ├── RealWorldScenariosTests.cs
│   ├── PerformanceTests.cs
│   └── EdgeCaseTests.cs
└── Helpers/
    └── TestDataBuilder.cs
```

---

## Critical Bug Tests

### Bug #1: Guillotine Cutting Violation

**File:** `CriticalBugs/Bug1_GuillotineCuttingTests.cs`

```csharp
using Xunit;
using FluentAssertions;
using BeamSawNesting;

namespace BeamSawNesting.Tests.CriticalBugs
{
    public class Bug1_GuillotineCuttingTests
    {
        [Fact]
        public void PerformGuillotineCut_VerticalCut_ShouldExtendFullHeight()
        {
            // Arrange
            var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal, kerf: 5);
            var panels = new List<Panel> { new Panel(600, 400) };

            // Act
            algo.Nest(panels);
            var cuts = algo.GetCutLines();

            // Assert - all vertical cuts should extend from Y=0 to Y=subSheetHeight
            var verticalCuts = cuts.Where(c => c.Orientation == CutOrientation.Vertical).ToList();

            foreach (var cut in verticalCuts)
            {
                var cutLength = cut.End - cut.Start;
                var expectedLength = cut.SourceSubSheet.Height;

                cutLength.Should().BeApproximately(expectedLength, 1e-6,
                    $"Vertical cut {cut.Id} should extend full sub-sheet height");
            }
        }

        [Fact]
        public void PerformGuillotineCut_HorizontalCut_ShouldExtendFullWidth()
        {
            // Arrange
            var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal, kerf: 5);
            var panels = new List<Panel> { new Panel(600, 400) };

            // Act
            algo.Nest(panels);
            var cuts = algo.GetCutLines();

            // Assert - all horizontal cuts should extend from X=0 to X=subSheetWidth
            var horizontalCuts = cuts.Where(c => c.Orientation == CutOrientation.Horizontal).ToList();

            foreach (var cut in horizontalCuts)
            {
                var cutLength = cut.End - cut.Start;
                var expectedLength = cut.SourceSubSheet.Width;

                cutLength.Should().BeApproximately(expectedLength, 1e-6,
                    $"Horizontal cut {cut.Id} should extend full sub-sheet width");
            }
        }

        [Fact]
        public void PerformGuillotineCut_ShouldNotCreateOverlappingSubSheets()
        {
            // Arrange
            var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal, kerf: 5);
            var panels = new List<Panel>
            {
                new Panel(1000, 600),
                new Panel(800, 400)
            };

            // Act
            algo.Nest(panels);
            var remainingSubSheets = algo.GetRemainingSubSheets();

            // Assert - no two sub-sheets should overlap
            for (int i = 0; i < remainingSubSheets.Count; i++)
            {
                for (int j = i + 1; j < remainingSubSheets.Count; j++)
                {
                    var sheet1 = remainingSubSheets[i];
                    var sheet2 = remainingSubSheets[j];

                    // Only check same sheet index
                    if (sheet1.SheetIndex != sheet2.SheetIndex)
                        continue;

                    // Check for overlap
                    bool overlapsX = sheet1.X < sheet2.X + sheet2.Width && sheet1.X + sheet1.Width > sheet2.X;
                    bool overlapsY = sheet1.Y < sheet2.Y + sheet2.Height && sheet1.Y + sheet1.Height > sheet2.Y;

                    (overlapsX && overlapsY).Should().BeFalse(
                        $"Sub-sheets {i} and {j} should not overlap. " +
                        $"Sheet1: ({sheet1.X:F1}, {sheet1.Y:F1}, {sheet1.Width:F1}x{sheet1.Height:F1}), " +
                        $"Sheet2: ({sheet2.X:F1}, {sheet2.Y:F1}, {sheet2.Width:F1}x{sheet2.Height:F1})");
                }
            }
        }

        [Fact]
        public void PerformGuillotineCut_MaterialConservation_AllAreasShouldSumCorrectly()
        {
            // Arrange
            var sheetWidth = 2440.0;
            var sheetHeight = 1220.0;
            var kerf = 5.0;
            var algo = new BeamSawNestingAlgorithm(sheetWidth, sheetHeight,
                SheetGrainDirection.Horizontal, kerf);

            var panels = new List<Panel>
            {
                new Panel(1000, 600),
                new Panel(800, 400)
            };

            // Act
            algo.Nest(panels);
            var placed = algo.GetPlacedPanels();
            var remaining = algo.GetRemainingSubSheets();
            var cuts = algo.GetCutLines();

            // Assert - for each sheet, material should be conserved
            for (int sheetIdx = 0; sheetIdx < algo.GetSheetCount(); sheetIdx++)
            {
                double sheetArea = sheetWidth * sheetHeight;

                double placedArea = placed
                    .Where(p => p.SheetIndex == sheetIdx)
                    .Sum(p => p.Width * p.Height);

                double remainingArea = remaining
                    .Where(r => r.SheetIndex == sheetIdx)
                    .Sum(r => r.Width * r.Height);

                double kerfArea = cuts
                    .Where(c => c.SheetIndex == sheetIdx)
                    .Sum(c => c.Orientation == CutOrientation.Horizontal
                        ? (c.End - c.Start) * c.KerfThickness
                        : (c.End - c.Start) * c.KerfThickness);

                double totalAccountedArea = placedArea + remainingArea + kerfArea;

                totalAccountedArea.Should().BeApproximately(sheetArea, 1.0,
                    $"Sheet {sheetIdx}: Placed + Remaining + Kerf should equal sheet area. " +
                    $"Sheet: {sheetArea:F2}, Placed: {placedArea:F2}, " +
                    $"Remaining: {remainingArea:F2}, Kerf: {kerfArea:F2}, " +
                    $"Total: {totalAccountedArea:F2}, Difference: {sheetArea - totalAccountedArea:F2}");
            }
        }

        [Fact]
        public void PerformGuillotineCut_SinglePanel_ShouldCreateTwoOrthogonalCuts()
        {
            // Arrange
            var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal, kerf: 5);
            var panels = new List<Panel> { new Panel(1000, 600) };

            // Act
            algo.Nest(panels);
            var cuts = algo.GetCutLines();

            // Assert
            cuts.Should().HaveCount(2, "placing one panel should create 2 guillotine cuts");

            var horizontalCuts = cuts.Where(c => c.Orientation == CutOrientation.Horizontal).ToList();
            var verticalCuts = cuts.Where(c => c.Orientation == CutOrientation.Vertical).ToList();

            horizontalCuts.Should().HaveCount(1, "should have 1 horizontal cut");
            verticalCuts.Should().HaveCount(1, "should have 1 vertical cut");
        }
    }
}
```

---

### Bug #2: Tolerance Direction

**File:** `CriticalBugs/Bug2_ToleranceTests.cs`

```csharp
public class Bug2_ToleranceTests
{
    [Fact]
    public void CanFit_PanelExactlyFits_ShouldReturnTrue()
    {
        // Arrange
        var subSheet = new SubSheet(0, 0, 1000, 500);

        // Act
        bool fits = subSheet.CanFit(1000.0, 500.0);

        // Assert
        fits.Should().BeTrue("panel exactly matching sub-sheet size should fit");
    }

    [Fact]
    public void CanFit_PanelSlightlySmaller_ShouldReturnTrue()
    {
        // Arrange
        var subSheet = new SubSheet(0, 0, 1000, 500);

        // Act
        bool fits = subSheet.CanFit(999.9999, 499.9999);

        // Assert
        fits.Should().BeTrue("panel slightly smaller should fit");
    }

    [Fact]
    public void CanFit_PanelSlightlyLarger_ShouldReturnFalse()
    {
        // Arrange
        var subSheet = new SubSheet(0, 0, 1000, 500);

        // Act
        bool fits = subSheet.CanFit(1000.0000001, 500.0);

        // Assert
        fits.Should().BeFalse("panel even slightly larger should NOT fit (safety margin)");
    }

    [Fact]
    public void CanFit_PanelMuchLarger_ShouldReturnFalse()
    {
        // Arrange
        var subSheet = new SubSheet(0, 0, 1000, 500);

        // Act
        bool fits = subSheet.CanFit(1100, 600);

        // Assert
        fits.Should().BeFalse("panel much larger should not fit");
    }

    [Theory]
    [InlineData(1000.0, 500.0, true)]   // Exact fit
    [InlineData(999.0, 499.0, true)]    // Smaller
    [InlineData(1000.00001, 500.0, false)] // Slightly larger width
    [InlineData(1000.0, 500.00001, false)] // Slightly larger height
    [InlineData(1001.0, 501.0, false)]  // Much larger
    [InlineData(0.0, 0.0, true)]        // Zero size
    public void CanFit_VariousSizes_ShouldValidateCorrectly(
        double panelWidth, double panelHeight, bool expectedFit)
    {
        // Arrange
        var subSheet = new SubSheet(0, 0, 1000, 500);

        // Act
        bool actualFit = subSheet.CanFit(panelWidth, panelHeight);

        // Assert
        actualFit.Should().Be(expectedFit,
            $"Panel {panelWidth}x{panelHeight} fit={expectedFit} in sub-sheet 1000x500");
    }

    [Fact]
    public void CanFit_AccumulatedFloatingPointErrors_ShouldNotExceedBoundary()
    {
        // Arrange - simulate accumulated floating-point errors
        double subSheetSize = 1000.0;
        var subSheet = new SubSheet(0, 0, subSheetSize, subSheetSize);

        // Simulate 100 cuts, each with tiny floating-point error
        double accumulatedSize = 0.0;
        for (int i = 0; i < 100; i++)
        {
            accumulatedSize += 10.0 + 1e-10; // 10mm + 0.1 nanometer error per cut
        }

        // Act
        bool fits = subSheet.CanFit(accumulatedSize, subSheetSize);

        // Assert - should NOT fit due to accumulated errors
        fits.Should().BeFalse(
            $"Accumulated size {accumulatedSize} should not fit in {subSheetSize} " +
            $"(difference: {accumulatedSize - subSheetSize})");
    }
}
```

---

### Bug #3: Boundary Validation

**File:** `CriticalBugs/Bug3_BoundaryValidationTests.cs`

```csharp
public class Bug3_BoundaryValidationTests
{
    [Fact]
    public void PlacePanel_WithinBoundary_ShouldSucceed()
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal);
        var panels = new List<Panel> { new Panel(1000, 600) };

        // Act
        algo.Nest(panels);
        var placed = algo.GetPlacedPanels();

        // Assert
        placed.Should().HaveCount(1);
        var panel = placed[0];

        (panel.X + panel.Width).Should().BeLessOrEqualTo(2440.0 + 1e-6,
            "panel should not exceed sheet width");
        (panel.Y + panel.Height).Should().BeLessOrEqualTo(1220.0 + 1e-6,
            "panel should not exceed sheet height");
    }

    [Fact]
    public void PlacePanel_AllPlacements_ShouldStayWithinSheetBoundaries()
    {
        // Arrange
        var sheetWidth = 2440.0;
        var sheetHeight = 1220.0;
        var algo = new BeamSawNestingAlgorithm(sheetWidth, sheetHeight,
            SheetGrainDirection.Horizontal);

        var panels = new List<Panel>
        {
            new Panel(1000, 600),
            new Panel(800, 400),
            new Panel(1200, 500),
            new Panel(600, 300)
        };

        // Act
        algo.Nest(panels);
        var placed = algo.GetPlacedPanels();

        // Assert
        foreach (var panel in placed)
        {
            (panel.X).Should().BeGreaterOrEqualTo(0, $"Panel {panel.Panel.Id} X should be >= 0");
            (panel.Y).Should().BeGreaterOrEqualTo(0, $"Panel {panel.Panel.Id} Y should be >= 0");

            (panel.X + panel.Width).Should().BeLessOrEqualTo(sheetWidth + 1e-6,
                $"Panel {panel.Panel.Id} should not exceed sheet width. " +
                $"Panel right edge: {panel.X + panel.Width:F6}, Sheet width: {sheetWidth}");

            (panel.Y + panel.Height).Should().BeLessOrEqualTo(sheetHeight + 1e-6,
                $"Panel {panel.Panel.Id} should not exceed sheet height. " +
                $"Panel bottom edge: {panel.Y + panel.Height:F6}, Sheet height: {sheetHeight}");
        }
    }

    [Fact]
    public void RemainingSubSheets_ShouldStayWithinSheetBoundaries()
    {
        // Arrange
        var sheetWidth = 2440.0;
        var sheetHeight = 1220.0;
        var algo = new BeamSawNestingAlgorithm(sheetWidth, sheetHeight,
            SheetGrainDirection.Horizontal);

        var panels = new List<Panel>
        {
            new Panel(1000, 600),
            new Panel(800, 400)
        };

        // Act
        algo.Nest(panels);
        var remaining = algo.GetRemainingSubSheets();

        // Assert
        foreach (var subSheet in remaining)
        {
            (subSheet.X).Should().BeGreaterOrEqualTo(0,
                $"Sub-sheet at level {subSheet.Level} X should be >= 0");
            (subSheet.Y).Should().BeGreaterOrEqualTo(0,
                $"Sub-sheet at level {subSheet.Level} Y should be >= 0");

            (subSheet.X + subSheet.Width).Should().BeLessOrEqualTo(sheetWidth + 1e-6,
                $"Sub-sheet at level {subSheet.Level} should not exceed sheet width. " +
                $"Right edge: {subSheet.X + subSheet.Width:F6}, Sheet width: {sheetWidth}");

            (subSheet.Y + subSheet.Height).Should().BeLessOrEqualTo(sheetHeight + 1e-6,
                $"Sub-sheet at level {subSheet.Level} should not exceed sheet height. " +
                $"Bottom edge: {subSheet.Y + subSheet.Height:F6}, Sheet height: {sheetHeight}");
        }
    }
}
```

---

### Bug #4: Failed Panel Tracking

**File:** `CriticalBugs/Bug4_FailedPanelTrackingTests.cs`

```csharp
public class Bug4_FailedPanelTrackingTests
{
    [Fact]
    public void Nest_PanelTooLarge_ShouldTrackAsFailure()
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(1000, 500, SheetGrainDirection.Horizontal);
        var panels = new List<Panel>
        {
            new Panel(2000, 1000) // Too large!
        };

        // Act
        algo.Nest(panels);

        // Assert
        algo.GetPlacedPanels().Should().BeEmpty("panel is too large to place");

        // REQUIRES IMPLEMENTATION:
        // algo.GetFailedPanels().Should().HaveCount(1, "failed panel should be tracked");
        // OR: Assert.Throws<NestingException>(() => algo.Nest(panels));
    }

    [Fact]
    public void Nest_MixedSuccessAndFailure_ShouldTrackBoth()
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(1000, 500, SheetGrainDirection.Horizontal);
        var panels = new List<Panel>
        {
            new Panel(600, 300),   // Fits
            new Panel(2000, 1000), // Too large
            new Panel(400, 200)    // Fits
        };

        // Act
        algo.Nest(panels);

        // Assert
        algo.GetPlacedPanels().Should().HaveCount(2, "2 panels should fit");

        // REQUIRES IMPLEMENTATION:
        // algo.GetFailedPanels().Should().HaveCount(1, "1 panel should fail");
    }

    [Fact]
    public void Nest_GrainConstraintImpossible_ShouldNotWasteSheets()
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal);
        var panels = new List<Panel>
        {
            new Panel(800, 600,
                rotation: RotationConstraint.NoRotation,
                grain: PanelGrainDirection.FixedVertical) // Impossible with horizontal sheet!
        };

        // Act
        algo.Nest(panels);

        // Assert
        var sheetCount = algo.GetSheetCount();

        // CURRENT BUG: Creates multiple empty sheets trying to place the panel
        // EXPECTED: Should fail quickly without wasting sheets

        // REQUIRES IMPLEMENTATION:
        // sheetCount.Should().Be(1, "should not create multiple sheets for impossible constraints");
        // algo.GetFailedPanels().Should().HaveCount(1);
    }

    [Fact]
    public void Nest_AllPanelsFit_ShouldHaveNoFailures()
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal);
        var panels = new List<Panel>
        {
            new Panel(1000, 600),
            new Panel(800, 400),
            new Panel(600, 300)
        };

        // Act
        algo.Nest(panels);

        // Assert
        algo.GetPlacedPanels().Should().HaveCount(3, "all panels should fit");

        // REQUIRES IMPLEMENTATION:
        // algo.GetFailedPanels().Should().BeEmpty("no panels should fail");
    }
}
```

---

### Bug #5: Grain Direction Validation

**File:** `CriticalBugs/Bug5_GrainDirectionTests.cs`

```csharp
public class Bug5_GrainDirectionTests
{
    [Fact]
    public void ValidateGrainDirection_HorizontalSheet_HorizontalPanel_ShouldPass()
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal);
        var panels = new List<Panel>
        {
            new Panel(1000, 600, grain: PanelGrainDirection.MatchSheet)
        };

        // Act
        algo.Nest(panels);
        var placed = algo.GetPlacedPanels();

        // Assert
        placed.Should().HaveCount(1);
        placed[0].FinalGrainDirection.Should().Be("Horizontal");
    }

    [Fact]
    public void ValidateGrainDirection_HorizontalSheet_VerticalPanel_NoRotation_ShouldFail()
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal);
        var panels = new List<Panel>
        {
            new Panel(600, 1000, // Vertical orientation
                rotation: RotationConstraint.NoRotation,
                grain: PanelGrainDirection.MatchSheet)
        };

        // Act
        algo.Nest(panels);
        var placed = algo.GetPlacedPanels();

        // Assert
        placed.Should().BeEmpty("vertical panel with no rotation should fail on horizontal sheet");
    }

    [Fact]
    public void ValidateGrainDirection_HorizontalSheet_VerticalPanel_WithRotation_ShouldSucceed()
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal);
        var panels = new List<Panel>
        {
            new Panel(600, 1000, // Vertical orientation
                rotation: RotationConstraint.Rotation90Allowed,
                grain: PanelGrainDirection.MatchSheet)
        };

        // Act
        algo.Nest(panels);
        var placed = algo.GetPlacedPanels();

        // Assert
        placed.Should().HaveCount(1, "panel should be rotated to match sheet grain");
        placed[0].RotationDegrees.Should().Be(90, "panel should be rotated 90°");
        placed[0].FinalGrainDirection.Should().Be("Horizontal");
    }

    [Fact]
    public void ValidateGrainDirection_SquarePanel_ShouldHandleGracefully()
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal);
        var panels = new List<Panel>
        {
            new Panel(600, 600, // Square - width == height
                grain: PanelGrainDirection.MatchSheet)
        };

        // Act
        algo.Nest(panels);
        var placed = algo.GetPlacedPanels();

        // Assert
        placed.Should().HaveCount(1, "square panel should be placed");
        placed[0].FinalGrainDirection.Should().Be("Horizontal",
            "square panel should match sheet grain");
    }

    [Theory]
    [InlineData(SheetGrainDirection.Horizontal, PanelGrainDirection.FixedHorizontal, true)]
    [InlineData(SheetGrainDirection.Horizontal, PanelGrainDirection.FixedVertical, false)]
    [InlineData(SheetGrainDirection.Vertical, PanelGrainDirection.FixedHorizontal, false)]
    [InlineData(SheetGrainDirection.Vertical, PanelGrainDirection.FixedVertical, true)]
    public void ValidateGrainDirection_FixedGrain_ShouldRespectConstraints(
        SheetGrainDirection sheetGrain,
        PanelGrainDirection panelGrain,
        bool shouldFit)
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(2440, 1220, sheetGrain);
        var panels = new List<Panel>
        {
            new Panel(1000, 600,
                rotation: RotationConstraint.NoRotation,
                grain: panelGrain)
        };

        // Act
        algo.Nest(panels);
        var placed = algo.GetPlacedPanels();

        // Assert
        if (shouldFit)
        {
            placed.Should().HaveCount(1,
                $"Panel with {panelGrain} should fit on {sheetGrain} sheet");
        }
        else
        {
            placed.Should().BeEmpty(
                $"Panel with {panelGrain} should NOT fit on {sheetGrain} sheet");
        }
    }
}
```

---

## Integration & Real-World Tests

**File:** `Integration/RealWorldScenariosTests.cs`

```csharp
public class RealWorldScenariosTests
{
    [Fact]
    public void KitchenCabinetCutList_ShouldNestEfficiently()
    {
        // Arrange - typical kitchen cabinet parts
        var algo = new BeamSawNestingAlgorithm(
            2440, 1220,
            SheetGrainDirection.Horizontal,
            kerf: 3.0);

        var panels = new List<Panel>
        {
            new Panel(1200, 600, grain: PanelGrainDirection.FixedHorizontal), // Tabletop
            new Panel(700, 300), // Side 1
            new Panel(700, 300), // Side 2
            new Panel(400, 250), // Shelf 1
            new Panel(400, 250), // Shelf 2
            new Panel(400, 250), // Shelf 3
        };

        // Act
        algo.Nest(panels);

        // Assert
        algo.GetPlacedPanels().Should().HaveCount(6, "all panels should be placed");
        algo.GetSheetCount().Should().Be(1, "should fit on single sheet");
        algo.GetOverallEfficiency().Should().BeGreaterThan(70.0,
            "efficiency should be reasonable for real-world scenario");
    }

    [Fact]
    public void LargeProjectWithManyPanels_ShouldCompleteWithoutErrors()
    {
        // Arrange - 50 panels of various sizes
        var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal);
        var random = new Random(42); // Fixed seed for reproducibility

        var panels = Enumerable.Range(0, 50)
            .Select(i => new Panel(
                random.Next(200, 1200),
                random.Next(200, 800)))
            .ToList();

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        algo.Nest(panels);
        stopwatch.Stop();

        // Assert
        algo.GetPlacedPanels().Should().NotBeEmpty("at least some panels should fit");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000,
            "should complete in reasonable time");

        // All placed panels should be within boundaries
        foreach (var placed in algo.GetPlacedPanels())
        {
            (placed.X + placed.Width).Should().BeLessOrEqualTo(2440.0 + 1e-6);
            (placed.Y + placed.Height).Should().BeLessOrEqualTo(1220.0 + 1e-6);
        }
    }

    [Fact]
    public void OptimalVsGreedy_GreedyShouldBeReasonable()
    {
        // Arrange - scenario where greedy might be suboptimal
        var algo = new BeamSawNestingAlgorithm(
            2440, 1220,
            SheetGrainDirection.Horizontal,
            sortStrategy: PanelSortStrategy.AreaDescending);

        var panels = new List<Panel>
        {
            new Panel(2400, 600), // Large panel
            new Panel(1200, 600), // Medium
            new Panel(1200, 600), // Medium
            new Panel(600, 300),  // Small
            new Panel(600, 300),  // Small
        };

        // Act
        algo.Nest(panels);

        // Assert
        var efficiency = algo.GetOverallEfficiency();

        // Greedy might not be optimal, but should be reasonable
        efficiency.Should().BeGreaterThan(60.0,
            "greedy strategy should achieve reasonable efficiency");

        algo.GetPlacedPanels().Should().HaveCount(5, "all panels should fit");
    }
}
```

---

## Performance Tests

**File:** `Integration/PerformanceTests.cs`

```csharp
public class PerformanceTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void Nest_VariousPanelCounts_ShouldCompleteInReasonableTime(int panelCount)
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal);
        var random = new Random(42);
        var panels = Enumerable.Range(0, panelCount)
            .Select(i => new Panel(random.Next(200, 1000), random.Next(200, 800)))
            .ToList();

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        algo.Nest(panels);
        stopwatch.Stop();

        // Assert
        var maxTime = panelCount * 50; // 50ms per panel maximum
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(maxTime,
            $"nesting {panelCount} panels should complete in < {maxTime}ms");

        _output.WriteLine($"{panelCount} panels: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Nest_RepeatedCalls_ShouldGiveConsistentResults()
    {
        // Arrange
        var panels = new List<Panel>
        {
            new Panel(1000, 600),
            new Panel(800, 400),
            new Panel(600, 300)
        };

        // Act - run algorithm 10 times
        var results = new List<int>();
        for (int i = 0; i < 10; i++)
        {
            var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal);
            algo.Nest(panels);
            results.Add(algo.GetPlacedPanels().Count);
        }

        // Assert - should get same result every time (deterministic)
        results.Should().AllBe(results[0],
            "algorithm should be deterministic with same inputs");
    }
}
```

---

## Edge Case Tests

**File:** `Integration/EdgeCaseTests.cs`

```csharp
public class EdgeCaseTests
{
    [Fact]
    public void Nest_EmptyPanelList_ShouldHandleGracefully()
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal);
        var panels = new List<Panel>();

        // Act
        algo.Nest(panels);

        // Assert
        algo.GetPlacedPanels().Should().BeEmpty();
        algo.GetSheetCount().Should().Be(1, "should initialize one sheet even with no panels");
    }

    [Fact]
    public void Nest_SinglePanelFullSheet_ShouldFitPerfectly()
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal);
        var panels = new List<Panel>
        {
            new Panel(2440, 1220) // Exact sheet size
        };

        // Act
        algo.Nest(panels);

        // Assert
        algo.GetPlacedPanels().Should().HaveCount(1);
        algo.GetOverallEfficiency().Should().BeApproximately(100.0, 0.1);
    }

    [Fact]
    public void Nest_VerySmallPanels_ShouldHandleAccurately()
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal, kerf: 5);
        var panels = Enumerable.Range(0, 100)
            .Select(_ => new Panel(50, 50)) // 100 tiny panels
            .ToList();

        // Act
        algo.Nest(panels);

        // Assert
        var placed = algo.GetPlacedPanels();
        placed.Count.Should().BeGreaterThan(0, "should place some panels");

        // Verify no overlaps
        for (int i = 0; i < placed.Count; i++)
        {
            for (int j = i + 1; j < placed.Count; j++)
            {
                if (placed[i].SheetIndex != placed[j].SheetIndex)
                    continue;

                bool overlaps =
                    placed[i].X < placed[j].X + placed[j].Width &&
                    placed[i].X + placed[i].Width > placed[j].X &&
                    placed[i].Y < placed[j].Y + placed[j].Height &&
                    placed[i].Y + placed[i].Height > placed[j].Y;

                overlaps.Should().BeFalse($"Panels {i} and {j} should not overlap");
            }
        }
    }

    [Fact]
    public void Nest_ZeroKerf_ShouldWorkCorrectly()
    {
        // Arrange
        var algo = new BeamSawNestingAlgorithm(2440, 1220,
            SheetGrainDirection.Horizontal, kerf: 0.0);

        var panels = new List<Panel>
        {
            new Panel(1000, 600),
            new Panel(800, 400)
        };

        // Act
        algo.Nest(panels);

        // Assert
        algo.GetPlacedPanels().Should().HaveCount(2);
        algo.GetCutLines().All(c => c.KerfThickness == 0.0).Should().BeTrue();
    }

    [Fact]
    public void Nest_LargeKerf_ShouldReducePlacementCapacity()
    {
        // Arrange - compare small vs large kerf
        var smallKerf = new BeamSawNestingAlgorithm(2440, 1220,
            SheetGrainDirection.Horizontal, kerf: 3.0);
        var largeKerf = new BeamSawNestingAlgorithm(2440, 1220,
            SheetGrainDirection.Horizontal, kerf: 20.0);

        var panels = Enumerable.Range(0, 20)
            .Select(_ => new Panel(400, 300))
            .ToList();

        // Act
        smallKerf.Nest(new List<Panel>(panels));
        largeKerf.Nest(new List<Panel>(panels));

        // Assert
        largeKerf.GetSheetCount().Should().BeGreaterOrEqualTo(smallKerf.GetSheetCount(),
            "larger kerf should require more sheets");
    }
}
```

---

## Test Execution Plan

### Phase 1: Critical Bug Detection (Week 1)
```bash
dotnet test --filter "FullyQualifiedName~CriticalBugs"
```
**Goal:** Detect and document all 6 critical bugs

### Phase 2: Fix Verification (Week 2)
1. Fix Bug #2 (tolerance)
2. Fix Bug #5 (parentheses)
3. Run tests to verify fixes
4. Fix Bug #3 (boundary validation)
5. Run tests again

### Phase 3: Algorithm Redesign (Week 3-4)
1. Fix Bug #1 (guillotine cuts) - major refactor
2. Fix Bug #4 (failure tracking)
3. Full test suite should pass

### Phase 4: Optimization (Week 5+)
1. Performance improvements
2. Better placement strategies
3. Integration with Grasshopper

---

## Test Metrics

**Target Metrics:**
- ✅ Line Coverage: 85%+
- ✅ Branch Coverage: 75%+
- ✅ All critical bugs detected: 6/6
- ✅ All critical bugs fixed: 0/6 → 6/6
- ✅ Integration tests passing: 0% → 100%
- ✅ Performance benchmarks met: < 50ms per panel

**Success Criteria:**
1. All critical bug tests FAIL initially (detecting bugs)
2. After fixes, all tests PASS
3. No regressions in existing functionality
4. Real-world scenarios perform acceptably

---

## Next Steps

1. ✅ **Create test project:**
   ```bash
   dotnet new xunit -n BeamSawNesting.Tests
   cd BeamSawNesting.Tests
   dotnet add package FluentAssertions
   dotnet add package xunit
   dotnet add package xunit.runner.visualstudio
   ```

2. ✅ **Implement critical bug tests** (copy code above)

3. ✅ **Run tests to verify bugs exist:**
   ```bash
   dotnet test --logger "console;verbosity=detailed"
   ```

4. ✅ **Fix bugs one by one, verifying with tests**

5. ✅ **Achieve 85%+ code coverage**

Would you like me to create the actual test project with all these tests?
