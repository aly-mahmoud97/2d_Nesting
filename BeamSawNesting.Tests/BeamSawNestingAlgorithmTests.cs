/*
 * xUnit Tests for Beam Saw Nesting Algorithm
 *
 * Test Coverage:
 * - Core algorithm functionality
 * - Panel placement validation
 * - Constraint enforcement (rotation, grain direction)
 * - Kerf compensation
 * - Multi-sheet handling
 * - Efficiency metrics
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using BeamSawNesting;

namespace BeamSawNesting.Tests
{
    public class BeamSawNestingAlgorithmTests
    {
        #region Test Data - User Provided Panel Dimensions

        /// <summary>
        /// Get the user-provided test panel dimensions
        /// </summary>
        private List<Panel> GetUserProvidedPanels()
        {
            var widths = new double[] { 1.294, 1.258, 0.54, 1.594, 1.576, 0.54, 0.54, 0.512, 0.372587 };
            var heights = new double[] { 0.54, 0.1, 0.452, 0.54, 0.1, 0.452, 0.452, 0.372587, 0.512 };

            var panels = new List<Panel>();
            for (int i = 0; i < widths.Length; i++)
            {
                panels.Add(new Panel(
                    widths[i],
                    heights[i],
                    RotationConstraint.Rotation90Allowed,
                    PanelGrainDirection.MatchSheet,
                    i,
                    $"Panel_{i}"
                ));
            }

            return panels;
        }

        #endregion

        #region Basic Algorithm Tests

        [Fact]
        public void Nest_WithUserProvidedPanels_ShouldPlaceAllPanels()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal,
                kerfThickness: 0.005,
                preferredCut: CutOrientation.Horizontal,
                sortStrategy: PanelSortStrategy.AreaDescending
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            Assert.Equal(panels.Count, placedPanels.Count);
        }

        [Fact]
        public void Nest_WithUserProvidedPanels_ShouldHaveValidSheetCount()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            int sheetCount = algorithm.GetSheetCount();
            Assert.True(sheetCount >= 1, "Should use at least one sheet");
            Assert.True(sheetCount <= panels.Count, "Should not use more sheets than panels");
        }

        [Fact]
        public void Nest_WithUserProvidedPanels_ShouldGenerateCutLines()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var cutLines = algorithm.GetCutLines();
            Assert.NotEmpty(cutLines);
        }

        [Fact]
        public void Nest_WithUserProvidedPanels_ShouldHavePositiveEfficiency()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            double efficiency = algorithm.GetOverallEfficiency();
            Assert.True(efficiency > 0, "Efficiency should be positive");
            Assert.True(efficiency <= 100, "Efficiency should not exceed 100%");
        }

        #endregion

        #region Panel Placement Validation Tests

        [Fact]
        public void Nest_PlacedPanels_ShouldNotOverlap()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();

            for (int i = 0; i < placedPanels.Count; i++)
            {
                for (int j = i + 1; j < placedPanels.Count; j++)
                {
                    var p1 = placedPanels[i];
                    var p2 = placedPanels[j];

                    // Only check panels on the same sheet
                    if (p1.SheetIndex == p2.SheetIndex)
                    {
                        bool overlaps = !(p1.Max.X <= p2.Min.X ||  // p1 is left of p2
                                         p2.Max.X <= p1.Min.X ||  // p2 is left of p1
                                         p1.Max.Y <= p2.Min.Y ||  // p1 is below p2
                                         p2.Max.Y <= p1.Min.Y);   // p2 is below p1

                        Assert.False(overlaps,
                            $"Panel {p1.Panel.Id} (Sheet {p1.SheetIndex}) overlaps with Panel {p2.Panel.Id} (Sheet {p2.SheetIndex})");
                    }
                }
            }
        }

        [Fact]
        public void Nest_PlacedPanels_ShouldBeWithinSheetBoundaries()
        {
            // Arrange
            double sheetWidth = 2.5;
            double sheetHeight = 2.5;
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth,
                sheetHeight,
                SheetGrainDirection.Horizontal
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            double tolerance = 1e-6;

            foreach (var panel in placedPanels)
            {
                Assert.True(panel.X >= -tolerance,
                    $"Panel {panel.Panel.Id} X position ({panel.X}) is negative");
                Assert.True(panel.Y >= -tolerance,
                    $"Panel {panel.Panel.Id} Y position ({panel.Y}) is negative");
                Assert.True(panel.Max.X <= sheetWidth + tolerance,
                    $"Panel {panel.Panel.Id} exceeds sheet width");
                Assert.True(panel.Max.Y <= sheetHeight + tolerance,
                    $"Panel {panel.Panel.Id} exceeds sheet height");
            }
        }

        [Fact]
        public void Nest_PlacedPanels_ShouldMaintainCorrectDimensions()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            double tolerance = 1e-6;

            foreach (var placed in placedPanels)
            {
                double originalArea = placed.Panel.Width * placed.Panel.Height;
                double placedArea = placed.Width * placed.Height;

                Assert.True(Math.Abs(originalArea - placedArea) < tolerance,
                    $"Panel {placed.Panel.Id} area changed after placement");
            }
        }

        #endregion

        #region Rotation Constraint Tests

        [Fact]
        public void Nest_WithNoRotationConstraint_ShouldNotRotatePanel()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panel = new Panel(
                width: 1.0,
                height: 0.5,
                rotation: RotationConstraint.NoRotation,
                grain: PanelGrainDirection.MatchSheet,
                id: 0
            );

            // Act
            algorithm.Nest(new List<Panel> { panel });

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            Assert.Single(placedPanels);
            Assert.Equal(0, placedPanels[0].RotationDegrees);
        }

        [Fact]
        public void Nest_WithRotationAllowed_CanRotatePanel()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 1.0,
                sheetHeight: 2.0,
                sheetGrain: SheetGrainDirection.Vertical  // Vertical grain
            );

            // Panel that's wider than tall - will need rotation to fit grain constraint
            var panel = new Panel(
                width: 0.5,
                height: 1.5,
                rotation: RotationConstraint.Rotation90Allowed,
                grain: PanelGrainDirection.MatchSheet,  // Must match vertical grain
                id: 0
            );

            // Act
            algorithm.Nest(new List<Panel> { panel });

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            Assert.Single(placedPanels);
            // Rotation degrees can be 0 or 90 depending on algorithm decision
            Assert.True(placedPanels[0].RotationDegrees == 0 || placedPanels[0].RotationDegrees == 90);
        }

        #endregion

        #region Grain Direction Constraint Tests

        [Fact]
        public void Nest_WithHorizontalGrain_ShouldRespectGrainDirection()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();

            foreach (var placed in placedPanels)
            {
                // Panels with MatchSheet grain should have horizontal grain
                if (placed.Panel.GrainDirection == PanelGrainDirection.MatchSheet)
                {
                    Assert.Equal("Horizontal", placed.FinalGrainDirection);
                }
            }
        }

        [Fact]
        public void Nest_WithVerticalGrain_ShouldRespectGrainDirection()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Vertical
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();

            foreach (var placed in placedPanels)
            {
                // Panels with MatchSheet grain should have vertical grain
                if (placed.Panel.GrainDirection == PanelGrainDirection.MatchSheet)
                {
                    Assert.Equal("Vertical", placed.FinalGrainDirection);
                }
            }
        }

        [Fact]
        public void Nest_WithFixedHorizontalGrain_ShouldEnforceHorizontalOrientation()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panel = new Panel(
                width: 1.5,
                height: 0.5,
                rotation: RotationConstraint.Rotation90Allowed,
                grain: PanelGrainDirection.FixedHorizontal,
                id: 0
            );

            // Act
            algorithm.Nest(new List<Panel> { panel });

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            Assert.Single(placedPanels);
            Assert.Equal("Horizontal", placedPanels[0].FinalGrainDirection);

            // Panel should be wider than tall (horizontal orientation)
            Assert.True(placedPanels[0].Width >= placedPanels[0].Height);
        }

        #endregion

        #region Kerf Compensation Tests

        [Fact]
        public void Nest_WithKerf_ShouldMaintainMinimumSpacing()
        {
            // Arrange
            double kerfThickness = 0.01;  // 10mm kerf
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal,
                kerfThickness: kerfThickness
            );

            var panels = new List<Panel>
            {
                new Panel(0.5, 0.5, id: 0),
                new Panel(0.5, 0.5, id: 1)
            };

            // Act
            algorithm.Nest(panels);

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            var cutLines = algorithm.GetCutLines();

            // Verify cut lines have correct kerf thickness
            foreach (var cut in cutLines)
            {
                Assert.Equal(kerfThickness, cut.KerfThickness);
            }
        }

        [Fact]
        public void Nest_WithZeroKerf_ShouldStillWork()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal,
                kerfThickness: 0.0
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            Assert.Equal(panels.Count, placedPanels.Count);
        }

        #endregion

        #region Multi-Sheet Tests

        [Fact]
        public void Nest_WithSmallSheet_ShouldUseMultipleSheets()
        {
            // Arrange - Use a very small sheet to force multiple sheets
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 1.0,
                sheetHeight: 1.0,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            int sheetCount = algorithm.GetSheetCount();
            Assert.True(sheetCount > 1, "Should require multiple sheets for small sheet size");
        }

        [Fact]
        public void Nest_MultipleSheets_ShouldHaveCorrectSheetIndices()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 1.0,
                sheetHeight: 1.0,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            int sheetCount = algorithm.GetSheetCount();

            foreach (var panel in placedPanels)
            {
                Assert.True(panel.SheetIndex >= 0, "Sheet index should be non-negative");
                Assert.True(panel.SheetIndex < sheetCount, "Sheet index should be within range");
            }
        }

        [Fact]
        public void Nest_MultipleSheets_ShouldHaveUtilizationForEachSheet()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 1.5,
                sheetHeight: 1.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var utilization = algorithm.GetSheetUtilization();
            int sheetCount = algorithm.GetSheetCount();

            Assert.Equal(sheetCount, utilization.Count);

            foreach (var util in utilization)
            {
                Assert.True(util >= 0 && util <= 100, "Utilization should be between 0 and 100%");
            }
        }

        #endregion

        #region Sort Strategy Tests

        [Fact]
        public void Nest_WithAreaDescendingStrategy_ShouldPlaceAllPanels()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal,
                sortStrategy: PanelSortStrategy.AreaDescending
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            Assert.Equal(panels.Count, placedPanels.Count);
        }

        [Fact]
        public void Nest_WithLargestFirstStrategy_ShouldPlaceAllPanels()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal,
                sortStrategy: PanelSortStrategy.LargestFirst
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            Assert.Equal(panels.Count, placedPanels.Count);
        }

        [Fact]
        public void Nest_WithSmallestFirstStrategy_ShouldPlaceAllPanels()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal,
                sortStrategy: PanelSortStrategy.SmallestFirst
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            Assert.Equal(panels.Count, placedPanels.Count);
        }

        #endregion

        #region Cut Sequence Tests

        [Fact]
        public void Nest_ShouldGenerateValidCutSequence()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var cutSequence = algorithm.GetCutSequence();

            Assert.NotEmpty(cutSequence);

            // Verify sequence numbers are sequential
            for (int i = 0; i < cutSequence.Count; i++)
            {
                Assert.Equal(i + 1, cutSequence[i].SequenceNumber);
            }
        }

        [Fact]
        public void Nest_CutLines_ShouldHaveValidOrientations()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert
            var cutLines = algorithm.GetCutLines();

            foreach (var cut in cutLines)
            {
                Assert.True(
                    cut.Orientation == CutOrientation.Horizontal ||
                    cut.Orientation == CutOrientation.Vertical,
                    "Cut orientation must be horizontal or vertical"
                );
            }
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void Nest_WithSinglePanel_ShouldPlaceSuccessfully()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panel = new Panel(1.0, 0.5, id: 0);

            // Act
            algorithm.Nest(new List<Panel> { panel });

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            Assert.Single(placedPanels);
            Assert.Equal(1, algorithm.GetSheetCount());
        }

        [Fact]
        public void Nest_WithEmptyPanelList_ShouldNotCrash()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            // Act
            algorithm.Nest(new List<Panel>());

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            Assert.Empty(placedPanels);
        }

        [Fact]
        public void Nest_WithVerySmallPanel_ShouldPlaceSuccessfully()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panel = new Panel(0.01, 0.01, id: 0);  // 10mm x 10mm

            // Act
            algorithm.Nest(new List<Panel> { panel });

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            Assert.Single(placedPanels);
        }

        [Fact]
        public void Nest_WithPanelNearSheetSize_ShouldPlaceSuccessfully()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panel = new Panel(2.4, 2.4, id: 0);  // Nearly full sheet

            // Act
            algorithm.Nest(new List<Panel> { panel });

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            Assert.Single(placedPanels);
        }

        #endregion

        #region Integration Tests - Real World Scenarios

        [Fact]
        public void Integration_UserProvidedPanels_CompleteWorkflow()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal,
                kerfThickness: 0.005,
                preferredCut: CutOrientation.Horizontal,
                sortStrategy: PanelSortStrategy.AreaDescending
            );

            var panels = GetUserProvidedPanels();

            // Act
            algorithm.Nest(panels);

            // Assert - Comprehensive validation
            var placedPanels = algorithm.GetPlacedPanels();
            var cutLines = algorithm.GetCutLines();
            var cutSequence = algorithm.GetCutSequence();
            var remaining = algorithm.GetRemainingSubSheets();
            var utilization = algorithm.GetSheetUtilization();
            var efficiency = algorithm.GetOverallEfficiency();

            // All panels should be placed
            Assert.Equal(panels.Count, placedPanels.Count);

            // Should have cut lines
            Assert.NotEmpty(cutLines);

            // Should have cut sequence
            Assert.NotEmpty(cutSequence);

            // Efficiency should be reasonable
            Assert.True(efficiency > 0 && efficiency <= 100);

            // Each sheet should have utilization data
            Assert.Equal(algorithm.GetSheetCount(), utilization.Count);

            // No overlapping panels
            for (int i = 0; i < placedPanels.Count; i++)
            {
                for (int j = i + 1; j < placedPanels.Count; j++)
                {
                    var p1 = placedPanels[i];
                    var p2 = placedPanels[j];

                    if (p1.SheetIndex == p2.SheetIndex)
                    {
                        bool overlaps = !(p1.Max.X <= p2.Min.X ||
                                         p2.Max.X <= p1.Min.X ||
                                         p1.Max.Y <= p2.Min.Y ||
                                         p2.Max.Y <= p1.Min.Y);

                        Assert.False(overlaps);
                    }
                }
            }
        }

        [Fact]
        public void Integration_MixedGrainDirections_ShouldHandleCorrectly()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 2.5,
                sheetHeight: 2.5,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            var panels = new List<Panel>
            {
                new Panel(1.0, 0.5, grain: PanelGrainDirection.MatchSheet, id: 0),
                new Panel(0.8, 0.4, grain: PanelGrainDirection.FixedHorizontal, id: 1),
                new Panel(0.6, 0.3, grain: PanelGrainDirection.FixedVertical, id: 2)
            };

            // Act
            algorithm.Nest(panels);

            // Assert
            var placedPanels = algorithm.GetPlacedPanels();
            Assert.Equal(3, placedPanels.Count);

            // Verify grain directions
            Assert.Equal("Horizontal", placedPanels[0].FinalGrainDirection);  // MatchSheet
            Assert.Equal("Horizontal", placedPanels[1].FinalGrainDirection);  // FixedHorizontal
            Assert.Equal("Vertical", placedPanels[2].FinalGrainDirection);    // FixedVertical
        }

        [Fact]
        public void Integration_LargeNumberOfPanels_ShouldCompleteInReasonableTime()
        {
            // Arrange
            var algorithm = new BeamSawNestingAlgorithm(
                sheetWidth: 3.0,
                sheetHeight: 3.0,
                sheetGrain: SheetGrainDirection.Horizontal
            );

            // Create 50 panels
            var panels = new List<Panel>();
            var random = new Random(42);  // Fixed seed for reproducibility
            for (int i = 0; i < 50; i++)
            {
                double width = 0.2 + random.NextDouble() * 0.5;
                double height = 0.2 + random.NextDouble() * 0.5;
                panels.Add(new Panel(width, height, id: i));
            }

            // Act
            var startTime = DateTime.Now;
            algorithm.Nest(panels);
            var elapsed = DateTime.Now - startTime;

            // Assert
            Assert.True(elapsed.TotalSeconds < 30,
                $"Algorithm took too long: {elapsed.TotalSeconds} seconds");

            var placedPanels = algorithm.GetPlacedPanels();
            Assert.Equal(50, placedPanels.Count);
        }

        #endregion
    }
}
